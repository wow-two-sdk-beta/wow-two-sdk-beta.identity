using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

namespace Identity.Tests.E2E.Oidc;

/// <summary>
/// Service tokens across the real service: client credentials bound to one audience, token exchange keeping the person and
/// adding <c>act</c>, an ended session refused, and each grant reaching only what its client names.
/// </summary>
public sealed class ServiceTokenTests : IAsyncLifetime
{
    private const string Password = "e2e-correct-horse-battery";
    private const string TokenExchange = "urn:ietf:params:oauth:grant-type:token-exchange";
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";
    private const string Desk = "https://desk.example.test/";
    private const string Files = "https://files.example.test/";
    private const string Worker = "e2e-worker";
    private const string WorkerSecret = "e2e-test-only-worker-secret";
    private const string OtherApp = "e2e-other-app";
    private const string OtherAppSecret = "e2e-test-only-other-secret";

    private OidcHost _server = null!;

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        _server = new OidcHost(new Dictionary<string, string?>
        {
            ["Identity:Server:Resources:0:Name"] = "desk",
            ["Identity:Server:Resources:0:Uri"] = Desk,
            ["Identity:Server:Resources:0:Scopes:0"] = "desk.media",
            ["Identity:Server:Resources:0:Scopes:1"] = "desk.orders",
            ["Identity:Server:Resources:0:Scopes:2"] = "desk.admin",
            ["Identity:Server:Resources:0:PermissionNamespace"] = "desk",
            ["Identity:Server:Resources:1:Name"] = "files",
            ["Identity:Server:Resources:1:Uri"] = Files,
            ["Identity:Server:Resources:1:Scopes:0"] = "files.read",
            ["Identity:Server:Resources:1:Scopes:1"] = "files.write",
            ["Identity:Server:Resources:1:PermissionNamespace"] = "files",
            // The test app signs people in and acts for them on Files; the worker calls Desk as itself and nothing else.
            ["Identity:Server:Clients:0:Grants:0"] = "authorization_code",
            ["Identity:Server:Clients:0:Grants:1"] = "token_exchange",
            ["Identity:Server:Clients:0:ExchangeResources:0"] = "files",
            ["Identity:Server:Clients:0:Scopes:0"] = "files.read",
            ["Identity:Server:Clients:1:ClientId"] = Worker,
            ["Identity:Server:Clients:1:ClientSecret"] = WorkerSecret,
            ["Identity:Server:Clients:1:Grants:0"] = "client_credentials",
            ["Identity:Server:Clients:1:Resources:0"] = "desk",
            ["Identity:Server:Clients:1:Scopes:0"] = "desk.media",
            ["Identity:Server:Clients:1:Scopes:1"] = "desk.orders",
            // Another app that may act for people on Files, though never with a token issued to the test app.
            ["Identity:Server:Clients:2:ClientId"] = OtherApp,
            ["Identity:Server:Clients:2:ClientSecret"] = OtherAppSecret,
            ["Identity:Server:Clients:2:Grants:0"] = "token_exchange",
            ["Identity:Server:Clients:2:ExchangeResources:0"] = "files",
            ["Identity:Server:Clients:2:Scopes:0"] = "files.read",
            ["Identity:Organizations:Roles:owner:0"] = "e2e:deploy",
            ["Identity:Organizations:Roles:owner:1"] = "files:share",
        });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task ClientCredentials_ShouldIssueAShortTokenForItsOneAudience_NamingTheClient()
    {
        using var client = _server.CreateClient();
        using var response = await WorkerTokenAsync(client, Desk, "desk.media");
        var body = await ReadAsync(response);

        var token = await client.ValidateAsync(body.GetProperty("access_token").GetString()!, Desk);
        token.Subject.Should().Be(Worker);
        token.GetClaim("client_id").Value.Should().Be(Worker);
        token.GetClaim("scope").Value.Split(' ').Should().BeEquivalentTo("desk.media", "desk.orders");
        token.Audiences.Should().Equal(Desk);
        body.GetProperty("expires_in").GetInt32().Should().BeInRange(295, 300);
        body.TryGetProperty("refresh_token", out _).Should().BeFalse("a service asks again");
        body.TryGetProperty("id_token", out _).Should().BeFalse();
        token.TryGetClaim("sid", out _).Should().BeFalse();
        await RowsShouldBeAsync(0);
    }

    [Fact]
    public async Task ClientCredentials_ShouldListEveryScopeOfTheTarget_WhateverItAskedFor()
    {
        using var client = _server.CreateClient();
        foreach (var asked in new[] { null, "desk.media", "desk.orders desk.media" })
        {
            using var response = await WorkerTokenAsync(client, Desk, asked);
            var body = await ReadAsync(response);

            (await client.ValidateAsync(body.GetProperty("access_token").GetString()!, Desk)).GetClaim("scope").Value.Split(' ')
                .Should().BeEquivalentTo(["desk.media", "desk.orders"], "one token per target, never one per action");
        }
    }

    [Fact]
    public async Task ClientCredentials_ShouldRefuseWhatItsClientDoesNotName()
    {
        using var client = _server.CreateClient();

        await ShouldFailAsync(await WorkerTokenAsync(client, resource: null, "desk.media"), "invalid_target");
        await ShouldFailAsync(await WorkerTokenAsync(client, "https://unknown.example.test/", "desk.media"), "invalid_target");
        await ShouldFailAsync(await WorkerTokenAsync(client, Files, "files.read"), "invalid_request", "invalid_scope");
        await ShouldFailAsync(await WorkerTokenAsync(client, Desk, "desk.admin"), "invalid_request", "invalid_scope");
        await ShouldFailAsync(await WorkerTokenAsync(client, Desk, "openid"), "invalid_scope");
        await ShouldFailAsync(await client.Http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = Worker,
            ["client_secret"] = "wrong",
            ["resource"] = Desk,
        })), "invalid_client");
    }

    [Fact]
    public async Task ClientCredentials_ShouldBeRefused_ForAClientThatOnlySignsPeopleIn()
    {
        using var client = _server.CreateClient();
        using var response = await client.TokenAsync(new() { ["grant_type"] = "client_credentials", ["resource"] = Files });
        await ShouldFailAsync(response, "unauthorized_client");
    }

    [Fact]
    public async Task TokenExchange_ShouldKeepThePerson_AddAct_AndKeepPermissionsToTheResourcesNamespace()
    {
        using var client = _server.CreateClient();
        var person = await client.SignUpAndSignInAsync($"exchange-{Guid.NewGuid():N}@example.test", Password);
        var organization = await JoinOrganizationAsync(person);
        var tokens = await client.SignInToAppAsync();
        var subject = await client.ValidateAsync(tokens.AccessToken, OidcHost.Audience);

        using var response = await ExchangeAsync(client, tokens.AccessToken, Files, "files.read");
        var body = await ReadAsync(response);

        var token = await client.ValidateAsync(body.GetProperty("access_token").GetString()!, Files);
        token.Subject.Should().Be(person.ToString("D"));
        token.Audiences.Should().Equal(Files);
        token.GetClaim("client_id").Value.Should().Be(OidcHost.ClientId);
        token.GetClaim("scope").Value.Should().Be("files.read");
        token.GetClaim("sid").Value.Should().Be(subject.GetClaim("sid").Value);
        token.GetClaim("org_id").Value.Should().Be(organization.ToString("D"));
        token.GetClaim("org_role").Value.Should().Be($"{organization:D}:owner");
        token.Claims.Where(c => c.Type == "org_permission").Select(c => c.Value).Should().Equal($"{organization:D}:files:share");
        using (var actor = JsonDocument.Parse(token.GetClaim("act").Value))
            actor.RootElement.GetProperty("sub").GetString().Should().Be(OidcHost.ClientId);
        body.GetProperty("issued_token_type").GetString().Should().Be(AccessTokenType);
        body.GetProperty("expires_in").GetInt32().Should().BeInRange(295, 300);
        body.TryGetProperty("refresh_token", out _).Should().BeFalse();
        token.TryGetClaim("email", out _).Should().BeFalse("no profile scope was granted");
    }

    [Fact]
    public async Task TokenExchange_ShouldRefuseAnEndedSession()
    {
        using var client = _server.CreateClient();
        await client.SignUpAndSignInAsync($"ended-{Guid.NewGuid():N}@example.test", Password);
        var tokens = await client.SignInToAppAsync();
        (await ReadAsync(await ExchangeAsync(client, tokens.AccessToken, Files, "files.read"))).GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();

        var sid = (await client.ValidateAsync(tokens.AccessToken, OidcHost.Audience)).GetClaim("sid").Value;
        (await client.Http.DeleteAsync($"/api/account/manage/sessions/{sid}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await ShouldFailAsync(await ExchangeAsync(client, tokens.AccessToken, Files, "files.read"), "invalid_grant");
    }

    [Fact]
    public async Task TokenExchange_ShouldRefuseAServiceToken_AnExchangedToken_AndAnotherAppsToken()
    {
        using var client = _server.CreateClient();
        await client.SignUpAndSignInAsync($"chain-{Guid.NewGuid():N}@example.test", Password);
        var tokens = await client.SignInToAppAsync();
        var exchanged = (await ReadAsync(await ExchangeAsync(client, tokens.AccessToken, Files, "files.read"))).GetProperty("access_token").GetString()!;
        var service = (await ReadAsync(await WorkerTokenAsync(client, Desk, "desk.media"))).GetProperty("access_token").GetString()!;

        await ShouldFailAsync(await ExchangeAsync(client, exchanged, Files, "files.read"), "invalid_grant");
        await ShouldFailAsync(await ExchangeAsync(client, service, Files, "files.read"), "invalid_grant");
        await ShouldFailAsync(await client.Http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = TokenExchange,
            ["client_id"] = OtherApp,
            ["client_secret"] = OtherAppSecret,
            ["subject_token"] = tokens.AccessToken,
            ["subject_token_type"] = AccessTokenType,
            ["resource"] = Files,
        })), "invalid_grant");
    }

    [Fact]
    public async Task TokenExchange_ShouldRefuseAResourceOrScopeOutsideTheExchangeGrant()
    {
        using var client = _server.CreateClient();
        await client.SignUpAndSignInAsync($"scope-{Guid.NewGuid():N}@example.test", Password);
        var tokens = await client.SignInToAppAsync();

        await ShouldFailAsync(await ExchangeAsync(client, tokens.AccessToken, Desk, "desk.media"), "invalid_request", "invalid_scope");
        await ShouldFailAsync(await ExchangeAsync(client, tokens.AccessToken, Files, "files.write"), "invalid_scope", "invalid_request");
    }

    [Fact]
    public async Task Discovery_ShouldAdvertiseBothGrants()
    {
        using var client = _server.CreateClient();
        var grants = (await client.DiscoverAsync()).GetProperty("grant_types_supported").EnumerateArray().Select(g => g.GetString());
        grants.Should().Contain(["client_credentials", TokenExchange]);
    }

    private static Task<HttpResponseMessage> ExchangeAsync(OidcTestClient client, string subject, string resource, string scope)
        => client.TokenAsync(new()
        {
            ["grant_type"] = TokenExchange,
            ["subject_token"] = subject,
            ["subject_token_type"] = AccessTokenType,
            ["resource"] = resource,
            ["scope"] = scope,
        });

    private static Task<HttpResponseMessage> WorkerTokenAsync(OidcTestClient client, string? resource, string? scope)
    {
        var form = new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = Worker, ["client_secret"] = WorkerSecret };
        if (resource is not null)
            form["resource"] = resource;
        if (scope is not null)
            form["scope"] = scope;
        return client.Http.PostAsync("/connect/token", new FormUrlEncodedContent(form));
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static async Task ShouldFailAsync(HttpResponseMessage response, params string[] errors)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            response.IsSuccessStatusCode.Should().BeFalse(body);
            using var document = JsonDocument.Parse(body);
            errors.Should().Contain(document.RootElement.GetProperty("error").GetString(), body);
        }
    }

    private async Task<Guid> JoinOrganizationAsync(Guid person)
    {
        await using var scope = _server.Host.Services.CreateAsyncScope();
        var owner = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(person))!;
        var organization = new IdentityOrganization { Name = "Exchange", Slug = $"exchange-{Guid.NewGuid():N}"[..20] };
        (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(organization, owner)).IsSuccess.Should().BeTrue();
        return organization.Id;
    }

    private async Task RowsShouldBeAsync(int expected)
    {
        await using var scope = _server.Host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await context.Set<IdentityOidcToken>().CountAsync()).Should().Be(expected, "service tokens stay out of the token table");
    }
}
