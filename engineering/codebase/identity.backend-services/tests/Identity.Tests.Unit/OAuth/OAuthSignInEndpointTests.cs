using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Claims;
using WoW.Two.Sdk.Beta.Be.Identity.Cookies;
using WoW.Two.Sdk.Beta.Be.Identity.OAuth.Endpoints;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.OAuth;

/// <summary>Drives <see cref="OAuthSignInEndpointRouteBuilderExtensions.MapOAuthSignInEndpoints"/> over an in-memory
/// host: a stand-in provider scheme echoes the return address it was challenged with, and a header scheme signs a
/// request in.</summary>
public sealed class OAuthSignInEndpointTests
{
    private const string Provider = "Probe";
    private const string NotConfigured = "Probe sign-in is not configured. Set Probe:ClientId.";

    [Theory]
    [InlineData("/products", "/products")]
    [InlineData("/p/haven/deploys?tab=1", "/p/haven/deploys?tab=1")]
    [InlineData(null, "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example/", "/")]
    [InlineData("/\\evil.example/", "/")]
    public async Task SignIn_ShouldChallengeTheProvider_ReturningOnlyToALocalAddress(string? returnUrl, string expected)
    {
        await using var host = await Host.StartAsync(providerConfigured: true);

        var path = returnUrl is null ? "/api/identity/sign-in" : "/api/identity/sign-in?returnUrl=" + Uri.EscapeDataString(returnUrl);
        using var response = await host.Client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(expected);
    }

    [Fact]
    public async Task SignIn_ShouldAnswer503WithTheHostsDetail_WhileTheProviderIsNotRegistered()
    {
        await using var host = await Host.StartAsync(providerConfigured: false);

        using var response = await host.Client.GetAsync(new Uri("/api/identity/sign-in?returnUrl=%2F", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("detail").GetString().Should().Be(NotConfigured);
        body.RootElement.GetProperty("title").GetString().Should().Be("Service Unavailable");
    }

    [Fact]
    public async Task Me_ShouldAnswer401AsProblem_WhenTheCallerIsAnonymous()
    {
        await using var host = await Host.StartAsync(providerConfigured: true);

        using var response = await host.Client.GetAsync(new Uri("/api/identity/me", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("title").GetString().Should().Be("Unauthorized");
    }

    [Fact]
    public async Task Me_ShouldReadTheNormalizedClaims_WhenTheCallerIsSignedIn()
    {
        await using var host = await Host.StartAsync(providerConfigured: true);

        using var response = await host.Client.SendAsync(SignedIn(
            "/api/identity/me",
            new Claim(NormalizedClaimTypeConstants.Username, "octocat"),
            new Claim(NormalizedClaimTypeConstants.DisplayName, "The Octocat"),
            new Claim(NormalizedClaimTypeConstants.Avatar, "https://avatars.example/octocat.png")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(
            """{"login":"octocat","name":"The Octocat","avatar":"https://avatars.example/octocat.png"}""");
    }

    [Fact]
    public async Task Me_ShouldFallBackToTheHandleAndTheNameClaim_WhenNormalizedClaimsAreMissing()
    {
        await using var host = await Host.StartAsync(providerConfigured: true);

        using var handleOnly = await host.Client.SendAsync(SignedIn("/api/identity/me", new Claim(NormalizedClaimTypeConstants.Username, "octocat")));
        using var nameOnly = await host.Client.SendAsync(SignedIn("/api/identity/me", new Claim(ClaimTypes.Name, "octo")));

        (await handleOnly.Content.ReadAsStringAsync()).Should().Be("""{"login":"octocat","name":"octocat","avatar":null}""");
        (await nameOnly.Content.ReadAsStringAsync()).Should().Be("""{"login":"octo","name":"","avatar":null}""");
    }

    [Fact]
    public async Task SignOut_ShouldClearTheSessionCookie_WhenTheCallerIsSignedIn()
    {
        await using var host = await Host.StartAsync(providerConfigured: true);

        using var response = await host.Client.SendAsync(SignedIn("/api/identity/sign-out", HttpMethod.Post, new Claim(NormalizedClaimTypeConstants.Username, "octocat")));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Set-Cookie").Should().ContainSingle(cookie => cookie.StartsWith(".test.auth.dev=;"));
    }

    [Fact]
    public async Task SignOut_ShouldAnswer401_WhenTheCallerIsAnonymous()
    {
        await using var host = await Host.StartAsync(providerConfigured: true);

        using var response = await host.Client.PostAsync(new Uri("/api/identity/sign-out", UriKind.Relative), null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static HttpRequestMessage SignedIn(string path, params Claim[] claims) => SignedIn(path, HttpMethod.Get, claims);

    private static HttpRequestMessage SignedIn(string path, HttpMethod method, params Claim[] claims)
    {
        var request = new HttpRequestMessage(method, path);
        foreach (var claim in claims)
            request.Headers.Add(HeaderSchemeHandler.ClaimHeader, claim.Type + "=" + claim.Value);
        return request;
    }

    private sealed class Host(WebApplication app) : IAsyncDisposable
    {
        public HttpClient Client { get; } = app.GetTestClient();

        public static async Task<Host> StartAsync(bool providerConfigured)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddProblemDetails();
            builder.Services.AddCookieAuthentication(o =>
            {
                o.Mode = AuthChallengeMode.Api;
                o.CookieName = ".test.auth";
                o.Environment = "dev";
            });
            var authentication = builder.Services.AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = HeaderSchemeHandler.SchemeName;
                    o.DefaultChallengeScheme = HeaderSchemeHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, HeaderSchemeHandler>(HeaderSchemeHandler.SchemeName, null);
            if (providerConfigured)
                authentication.AddScheme<AuthenticationSchemeOptions, ProviderStandIn>(Provider, null);
            builder.Services.AddAuthorization();

            var app = builder.Build();
            app.UseStatusCodePages();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGroup("/api/identity").MapOAuthSignInEndpoints(Provider, o => o.NotConfiguredDetail = NotConfigured);
            await app.StartAsync();
            return new Host(app);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    /// <summary>Signs a request in from <c>X-Test-Claim: type=value</c> headers; challenges with a bare 401.</summary>
    private sealed class HeaderSchemeHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Header";
        public const string ClaimHeader = "X-Test-Claim";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(ClaimHeader, out var values))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = values.Select(value => value!.Split('=', 2)).Select(pair => new Claim(pair[0], pair[1]));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }

    /// <summary>Stands in for an OAuth provider: a challenge redirects straight to the return address it was given.</summary>
    private sealed class ProviderStandIn(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.Redirect(properties.RedirectUri!);
            return Task.CompletedTask;
        }
    }
}
