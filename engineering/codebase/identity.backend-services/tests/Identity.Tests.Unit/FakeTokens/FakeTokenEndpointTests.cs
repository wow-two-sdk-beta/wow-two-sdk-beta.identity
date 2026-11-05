using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using WoW.Two.Sdk.Beta.Be.Identity.Testing;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.FakeTokens;

/// <summary>Posts to the fake's token endpoint as the be sdk's token handlers do, with a plain <see cref="HttpClient"/>.</summary>
public sealed class FakeTokenEndpointTests : IDisposable
{
    private const string Endpoint = "https://fake-issuer.test/connect/token";
    private const string ExchangeGrant = "urn:ietf:params:oauth:grant-type:token-exchange";
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";
    private const string Files = "https://files.example.test/";

    private static readonly Guid Organization = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly FakePerson Person = new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        OrganizationId = Organization,
        Role = "owner",
        Permissions = ["files:share", "files:read"],
        SessionId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
    };

    private readonly FakeTokenIssuer _issuer = new();
    private readonly HttpClient _client;

    public FakeTokenEndpointTests() => _client = new HttpClient(_issuer.CreateTokenEndpointHandler());

    public void Dispose()
    {
        _client.Dispose();
        _issuer.Dispose();
    }

    [Fact]
    public async Task ResourceScopes_ShouldListEveryScopeOfTheResource_WhateverWasAsked()
    {
        _issuer.ResourceScopes[Files] = ["files.read", "files.write"];

        using var response = await PostAsync(new()
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "worker",
            ["client_secret"] = "any",
            ["resource"] = Files,
            ["scope"] = "files.read",
        });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        new JsonWebToken(body.GetProperty("access_token").GetString()!).GetClaim("scope").Value.Should().Be("files.read files.write");
    }

    [Fact]
    public async Task ClientCredentials_ShouldIssueTheClientsToken()
    {
        using var response = await PostAsync(new()
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "worker",
            ["client_secret"] = "any",
            ["resource"] = Files,
            ["scope"] = "files.read files.write",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("token_type").GetString().Should().Be("Bearer");
        body.GetProperty("expires_in").GetInt64().Should().Be(300);
        body.TryGetProperty("issued_token_type", out _).Should().BeFalse();
        body.TryGetProperty("refresh_token", out _).Should().BeFalse();

        var token = new JsonWebToken(body.GetProperty("access_token").GetString());
        token.Subject.Should().Be("worker");
        token.GetClaim("client_id").Value.Should().Be("worker");
        token.GetClaim("scope").Value.Should().Be("files.read files.write");
        token.Audiences.Should().Equal(Files);
    }

    [Fact]
    public async Task Exchange_ShouldKeepThePerson_AndAddAct()
    {
        var subject = _issuer.IssuePersonToken(Person, "https://app.example.test/", "editorial");

        using var response = await PostAsync(Exchange(subject));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("issued_token_type").GetString().Should().Be(AccessTokenType);
        body.GetProperty("expires_in").GetInt64().Should().Be(300);

        var token = new JsonWebToken(body.GetProperty("access_token").GetString());
        token.Subject.Should().Be(Person.Id.ToString("D"));
        token.Audiences.Should().Equal(Files);
        token.GetClaim("client_id").Value.Should().Be("editorial");
        token.GetClaim("scope").Value.Should().Be("files.read");
        token.GetClaim("org_id").Value.Should().Be(Organization.ToString("D"));
        token.GetClaim("org_role").Value.Should().Be($"{Organization:D}:owner");
        token.Claims.Where(claim => claim.Type == "org_permission").Select(claim => claim.Value)
            .Should().Equal($"{Organization:D}:files:share", $"{Organization:D}:files:read");
        token.GetClaim("sid").Value.Should().Be(Person.SessionId!.Value.ToString("D"));
        using var actor = JsonDocument.Parse(token.GetClaim("act").Value);
        actor.RootElement.GetProperty("sub").GetString().Should().Be("editorial");
    }

    [Fact]
    public async Task Exchange_ShouldKeepAPersonWithNoOrganization()
    {
        var subject = _issuer.IssuePersonToken(new FakePerson { Id = Person.Id }, "https://app.example.test/", "editorial");

        using var response = await PostAsync(Exchange(subject));

        var token = new JsonWebToken((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString());
        token.Subject.Should().Be(Person.Id.ToString("D"));
        token.TryGetClaim("org_id", out _).Should().BeFalse();
        token.TryGetClaim("sid", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Exchange_ShouldAcceptTheSubjectTokensAudience_AsTheCaller()
    {
        var subject = _issuer.IssuePersonToken(Person, "editorial", "another-app");

        using var response = await PostAsync(Exchange(subject));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Exchange_ShouldRefuseTokensThatAreNotAPersonsOwn()
    {
        var service = _issuer.IssueServiceToken("editorial", "https://app.example.test/");
        var exchanged = _issuer.IssueExchangedToken(Person, "editorial", "https://app.example.test/");
        var anotherApps = _issuer.IssuePersonToken(Person, "https://app.example.test/", "another-app");
        using var other = new FakeTokenIssuer();
        var anotherIssuers = other.IssuePersonToken(Person, "https://app.example.test/", "editorial");

        await ShouldFailAsync(Exchange(service), "invalid_grant");
        await ShouldFailAsync(Exchange(exchanged), "invalid_grant");
        await ShouldFailAsync(Exchange(anotherApps), "invalid_grant");
        await ShouldFailAsync(Exchange(anotherIssuers), "invalid_grant");
        await ShouldFailAsync(Exchange("not-a-token"), "invalid_grant");
    }

    [Fact]
    public async Task Exchange_ShouldRefuseAnExpiredSubject_ByTheIssuersClock()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var issuer = new FakeTokenIssuer(clock: clock);
        using var client = new HttpClient(issuer.CreateTokenEndpointHandler());
        var subject = issuer.IssuePersonToken(Person, "https://app.example.test/", "editorial");

        clock.Advance(TimeSpan.FromMinutes(6));

        using var response = await client.PostAsync(new Uri(Endpoint), new FormUrlEncodedContent(Exchange(subject)));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task Exchange_ShouldRefuseAMalformedRequest()
    {
        var subject = _issuer.IssuePersonToken(Person, "https://app.example.test/", "editorial");

        await ShouldFailAsync(Exchange(subject, form => form.Remove("subject_token")), "invalid_request");
        await ShouldFailAsync(Exchange(subject, form => form["subject_token_type"] = "urn:ietf:params:oauth:token-type:id_token"), "invalid_request");
        await ShouldFailAsync(Exchange(subject, form => form["requested_token_type"] = "urn:ietf:params:oauth:token-type:id_token"), "invalid_request");
        await ShouldFailAsync(Exchange(subject, form => form["actor_token"] = "anything"), "invalid_request");
    }

    [Fact]
    public async Task Request_ShouldFail_WithAnErrorAnd400()
    {
        await ShouldFailAsync(new() { ["client_id"] = "worker", ["resource"] = Files }, "unsupported_grant_type");
        await ShouldFailAsync(new() { ["grant_type"] = "password", ["client_id"] = "worker", ["resource"] = Files }, "unsupported_grant_type");
        await ShouldFailAsync(new() { ["grant_type"] = "client_credentials", ["resource"] = Files }, "invalid_client");
        await ShouldFailAsync(new() { ["grant_type"] = "client_credentials", ["client_id"] = "worker" }, "invalid_target");
    }

    [Fact]
    public async Task Request_ShouldFail_WhenItNamesSeveralResources()
    {
        using var content = new FormUrlEncodedContent(
        [
            new("grant_type", "client_credentials"),
            new("client_id", "worker"),
            new("resource", Files),
            new("resource", "https://desk.example.test/"),
        ]);

        using var response = await _client.PostAsync(new Uri(Endpoint), content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("invalid_target");
    }

    [Fact]
    public async Task Request_ShouldFail_WhenItIsNotAPost()
    {
        using var response = await _client.GetAsync(new Uri(Endpoint));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("invalid_request");
    }

    private static Dictionary<string, string> Exchange(string subject, Action<Dictionary<string, string>>? change = null)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = ExchangeGrant,
            ["client_id"] = "editorial",
            ["client_secret"] = "any",
            ["subject_token"] = subject,
            ["subject_token_type"] = AccessTokenType,
            ["resource"] = Files,
            ["scope"] = "files.read",
        };
        change?.Invoke(form);
        return form;
    }

    private async Task<HttpResponseMessage> PostAsync(Dictionary<string, string> form)
    {
        using var content = new FormUrlEncodedContent(form);
        return await _client.PostAsync(new Uri(Endpoint), content);
    }

    private async Task ShouldFailAsync(Dictionary<string, string> form, string error)
    {
        using var response = await PostAsync(form);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be(error);
    }
}
