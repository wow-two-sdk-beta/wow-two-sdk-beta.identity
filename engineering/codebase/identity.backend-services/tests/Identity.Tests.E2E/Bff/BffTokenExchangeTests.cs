using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2TokenExchange;
using Identity.Tests.E2E.Oidc;

namespace Identity.Tests.E2E.Bff;

/// <summary>A BFF acting for its signed-in person: the subject token comes from the session, with no bearer header on the request.</summary>
public sealed class BffTokenExchangeTests
{
    private const string Files = "https://files.example.test/";
    private const string Password = "e2e-correct-horse-battery";

    [Fact]
    public async Task HttpClient_ShouldExchangeTheSessionsToken_ForTheResource()
    {
        await using var identity = new OidcHost(new Dictionary<string, string?>
        {
            ["Identity:Server:Clients:0:RedirectUris:0"] = BffHost.Origin + "/api/identity/callback",
            ["Identity:Server:Clients:0:PostSignOutRedirectUris:0"] = BffHost.Origin + "/",
            ["Identity:Server:Clients:0:Grants:0"] = "authorization_code",
            ["Identity:Server:Clients:0:Grants:1"] = "token_exchange",
            ["Identity:Server:Clients:0:ExchangeResources:0"] = "files",
            ["Identity:Server:Clients:0:Scopes:0"] = "files.read",
            ["Identity:Server:Resources:0:Name"] = "files",
            ["Identity:Server:Resources:0:Uri"] = Files,
            ["Identity:Server:Resources:0:Scopes:0"] = "files.read",
        });
        using (identity.Host.CreateClient())
        {
        }

        var resource = new RecordingResource();
        await using var app = await BffHost.StartAsync(
            identity,
            services: services =>
            {
                services.AddHttpClient(OAuth2HttpClientNames.TokenEndpoint).ConfigurePrimaryHttpMessageHandler(() => identity.Host.Server.CreateHandler());
                services.AddHttpClient("files", client => client.BaseAddress = new Uri(Files))
                    .ConfigurePrimaryHttpMessageHandler(() => resource)
                    .AddOAuth2TokenExchange(options =>
                    {
                        options.TokenEndpoint = new Uri("https://localhost/connect/token");
                        options.ClientId = OidcHost.ClientId;
                        options.ClientSecret = OidcHost.ClientSecret;
                        options.Resource = Files;
                    });
            },
            routes: routes => routes.MapGet("/api/files", async (IHttpClientFactory clients) =>
            {
                using var response = await clients.CreateClient("files").GetAsync("/list");
                return Results.StatusCode((int)response.StatusCode);
            }).RequireAuthorization());
        using var browser = new BffBrowser(identity, app);
        var person = await browser.SignUpAtTheInstanceAsync($"bff-act-{Guid.NewGuid():N}@example.test", Password);
        await browser.SignInToTheAppAsync();

        (await browser.GetAsync("/api/files")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await browser.GetAsync("/api/files")).StatusCode.Should().Be(HttpStatusCode.OK);

        resource.Bearers.Should().HaveCount(2);
        resource.Bearers.Distinct().Should().ContainSingle("one token per person and target");
        using var relyingParty = identity.CreateClient();
        var token = await relyingParty.ValidateAsync(resource.Bearers[0], Files);
        token.Subject.Should().Be(person.ToString("D"));
        using var actor = JsonDocument.Parse(token.GetClaim("act").Value);
        actor.RootElement.GetProperty("sub").GetString().Should().Be(OidcHost.ClientId);
    }

    private sealed class RecordingResource : HttpMessageHandler
    {
        public List<string> Bearers { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bearers.Add(request.Headers.Authorization!.Parameter!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
