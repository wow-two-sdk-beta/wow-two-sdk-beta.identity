using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Identity.Bff;
using WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Bff;

/// <summary>
/// A BFF pipeline in memory over a stand-in instance (discovery only): API requests answer problems, pages redirect,
/// the session routes, antiforgery, and the organization policies over a session signed in by a test route.
/// </summary>
public sealed class BffPipelineTests
{
    private const string Authority = "https://id.example.test";
    private static readonly Guid Organization = Guid.Parse("01900000-0000-7000-8000-000000000001");

    [Fact]
    public async Task NoSession_ShouldAnswerProblemsToTheApi_AndRedirectPagesToTheLoginRoute()
    {
        await using var host = await BffTestHost.StartAsync();

        using var api = await host.Client.GetAsync("/api/me");
        using var post = await host.Client.PostAsync("/api/desks", null);
        using var page = await host.Client.GetAsync("/desk?tab=2");
        using var session = await host.Client.GetAsync("/auth/session");

        api.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        api.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await CodeOf(api)).Should().Be(BffErrorCodeConstants.SignInRequired);
        post.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "without a session the antiforgery check has nothing to bind to");
        page.StatusCode.Should().Be(HttpStatusCode.Redirect);
        page.Headers.Location!.ToString().Should().Be("https://app.example.test/auth/login?returnUrl=%2Fdesk%3Ftab%3D2");
        session.StatusCode.Should().Be(HttpStatusCode.OK);
        session.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await session.Content.ReadAsStringAsync()).Should().Be("""{"data":{"isAuthenticated":false}}""");
    }

    [Fact]
    public async Task Login_ShouldRedirectToTheAuthorizeEndpoint_WithTheCodeFlowAndPkce()
    {
        await using var host = await BffTestHost.StartAsync();

        using var login = await host.Client.GetAsync("/auth/login?returnUrl=%2Fdesk");

        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = login.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).Should().Be(Authority + "/connect/authorize");
        var query = QueryHelpers.ParseQuery(location.Query);
        query["client_id"].ToString().Should().Be("desk");
        query["redirect_uri"].ToString().Should().Be("https://app.example.test/auth/callback");
        query["response_type"].ToString().Should().Be("code");
        query.ContainsKey("response_mode").Should().BeFalse("query is the code flow's default mode, so the handler leaves it out");
        query["code_challenge_method"].ToString().Should().Be("S256");
        query["scope"].ToString().Should().Be("openid profile email phone offline_access");
    }

    [Fact]
    public async Task Session_ShouldKeepTheTokensServerSide_AndAnswerThePerson()
    {
        await using var host = await BffTestHost.StartAsync();

        using var signedIn = await host.Client.PostAsync("/test/sign-in", null);
        var cookie = signedIn.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(".app.session=", StringComparison.Ordinal));
        var session = await SessionAsync(host);

        cookie.Length.Should().BeLessThan(1024, "the cookie carries the reference only");
        cookie.Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=lax");
        session.GetProperty("isAuthenticated").GetBoolean().Should().BeTrue();
        session.GetProperty("person").GetProperty("sub").GetString().Should().Be("person-1");
        session.GetProperty("person").GetProperty("email").GetString().Should().Be("alice@example.test");
        session.GetProperty("person").GetProperty("phoneNumber").ValueKind.Should().Be(JsonValueKind.Null);
        session.GetProperty("organization").GetProperty("id").GetGuid().Should().Be(Organization);
        session.GetProperty("organization").GetProperty("role").GetString().Should().Be("owner");
        session.GetProperty("permissions").EnumerateArray().Select(value => value.GetString()).Should().Equal("desk:edit");
        session.GetProperty("csrfToken").GetString().Should().NotBeNullOrEmpty();
        session.GetProperty("expiresAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(12), TimeSpan.FromMinutes(1));
        (await host.Client.GetStringAsync("/api/token")).Should().Be("access-0", "GetTokenAsync reads the server-side token");
    }

    [Fact]
    public async Task UnsafeApiRequests_ShouldNeedTheCsrfHeaderBoundToTheSession()
    {
        await using var host = await BffTestHost.StartAsync();
        using var signedIn = await host.Client.PostAsync("/test/sign-in", null);
        var token = (await SessionAsync(host)).GetProperty("csrfToken").GetString()!;

        using var missing = await host.Client.PostAsync("/api/desks", null);
        using var wrong = await SendAsync(host, HttpMethod.Post, "/api/desks", "forged");
        using var allowed = await SendAsync(host, HttpMethod.Post, "/api/desks", token);
        using var read = await host.Client.GetAsync("/api/me");

        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(missing)).Should().Be(BffErrorCodeConstants.AntiforgeryValidationFailed);
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        read.StatusCode.Should().Be(HttpStatusCode.OK, "safe methods need no header");
    }

    [Fact]
    public async Task Policies_ShouldAnswer403WithoutThePermission_And404ForAnotherOrganization()
    {
        await using var host = await BffTestHost.StartAsync();
        using var signedIn = await host.Client.PostAsync("/test/sign-in", null);
        var token = (await SessionAsync(host)).GetProperty("csrfToken").GetString()!;

        using var denied = await SendAsync(host, HttpMethod.Post, "/api/admin", token);
        using var own = await host.Client.GetAsync($"/api/orgs/{Organization}/desks");
        using var foreign = await host.Client.GetAsync($"/api/orgs/{Guid.CreateVersion7()}/desks");

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeOf(denied)).Should().Be(BffErrorCodeConstants.AccessDenied);
        own.StatusCode.Should().Be(HttpStatusCode.OK);
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeOf(foreign)).Should().Be(BffErrorCodeConstants.NotFound);
    }

    [Fact]
    public async Task Logout_ShouldNeedTheCsrfHeader_EndTheSession_AndAnswerTheEndSessionUrl()
    {
        await using var host = await BffTestHost.StartAsync();
        using var signedIn = await host.Client.PostAsync("/test/sign-in", null);
        var token = (await SessionAsync(host)).GetProperty("csrfToken").GetString()!;

        using var refused = await host.Client.PostAsync("/auth/logout", null);
        using var loggedOut = await SendAsync(host, HttpMethod.Post, "/auth/logout", token);
        var url = new Uri(JsonDocument.Parse(await loggedOut.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("endSessionUrl").GetString()!);

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        loggedOut.StatusCode.Should().Be(HttpStatusCode.OK);
        url.GetLeftPart(UriPartial.Path).Should().Be(Authority + "/connect/endsession");
        var query = QueryHelpers.ParseQuery(url.Query);
        query["id_token_hint"].ToString().Should().Be("id-0");
        query["client_id"].ToString().Should().Be("desk");
        query["post_logout_redirect_uri"].ToString().Should().Be("https://app.example.test/");
        (await SessionAsync(host)).GetProperty("isAuthenticated").GetBoolean().Should().BeFalse();
        (await host.Services.GetRequiredService<IBffSessionRepository>().FindAsync("sid-1")).Should().BeNull("the server-side session is gone");
        using var again = await host.Client.PostAsync("/auth/logout", null);
        again.StatusCode.Should().Be(HttpStatusCode.OK, "logging out without a session needs no header");
    }

    private static async Task<JsonElement> SessionAsync(BffTestHost host)
    {
        using var document = JsonDocument.Parse(await host.Client.GetStringAsync("/auth/session"));
        return document.RootElement.GetProperty("data").Clone();
    }

    private static Task<HttpResponseMessage> SendAsync(BffTestHost host, HttpMethod method, string path, string csrfToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(BffHeaderConstants.CsrfToken, csrfToken);
        return host.Client.SendAsync(request);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString();
    }

    /// <summary>The BFF in memory, a browser with cookies, and a stand-in instance answering discovery.</summary>
    private sealed class BffTestHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public IServiceProvider Services => app.Services;

        public static async Task<BffTestHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:Client:Authority"] = Authority,
                ["Identity:Client:ClientId"] = "desk",
                ["Identity:Client:ClientSecret"] = "test-only-secret",
            });
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.Services.AddProblemDetails();
            builder.Services.AddIdentityBff();
            builder.Services.Configure<OpenIdConnectOptions>(BffAuthenticationSchemeConstants.OpenIdConnect, o => o.BackchannelHttpHandler = new Discovery());

            var app = builder.Build();
            app.UseIdentityBff();
            app.MapIdentityBffEndpoints();
            app.MapPost("/test/sign-in", SignInAsync);
            app.MapGet("/desk", () => "desk").RequireAuthorization();
            var api = app.MapGroup("/api");
            api.MapGet("/me", (ClaimsPrincipal user) => user.FindFirst("sub")!.Value).RequireAuthorization();
            // A value-returning lambda, not "async http => await …", which would bind as a RequestDelegate and drop the value.
            api.MapGet("/token", async (HttpContext http) => await http.GetTokenAsync("access_token") ?? "none").RequireAuthorization();
            api.MapPost("/desks", () => "ok").RequirePermission("desk:edit");
            api.MapPost("/admin", () => "ok").RequirePermission("desk:admin");
            api.MapGet("/orgs/{organizationId:guid}/desks", () => "ok").RequireRouteOrganization();
            await app.StartAsync();

            var client = new HttpClient(new CookieContainerHandler { InnerHandler = app.GetTestServer().CreateHandler() })
            {
                BaseAddress = new Uri("https://app.example.test"),
            };
            return new BffTestHost(app, client);
        }

        /// <summary>Signs a person in as the code flow's callback would: the ID token's claims and the saved tokens.</summary>
        private static async Task SignInAsync(HttpContext http)
        {
            var identity = new ClaimsIdentity(
            [
                new Claim("sub", "person-1"), new Claim("sid", "sid-1"), new Claim("name", "Alice"), new Claim("email", "alice@example.test"),
                new Claim("org_id", Organization.ToString()), new Claim("org_role", $"{Organization}:owner"),
                new Claim("org_permission", $"{Organization}:desk:edit"),
            ], "oidc", "name", "role");
            var properties = new AuthenticationProperties();
            properties.StoreTokens(
            [
                new AuthenticationToken { Name = "access_token", Value = "access-0" },
                new AuthenticationToken { Name = "refresh_token", Value = "refresh-0" },
                new AuthenticationToken { Name = "id_token", Value = "id-0" },
                new AuthenticationToken { Name = "expires_at", Value = DateTimeOffset.UtcNow.AddMinutes(10).ToString("o") },
            ]);
            await http.SignInAsync(BffAuthenticationSchemeConstants.Cookie, new ClaimsPrincipal(identity), properties);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    /// <summary>The stand-in instance's discovery document and an empty key set.</summary>
    private sealed class Discovery : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.RequestUri!.AbsolutePath switch
            {
                "/.well-known/openid-configuration" => JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["issuer"] = Authority + "/",
                    ["authorization_endpoint"] = Authority + "/connect/authorize",
                    ["token_endpoint"] = Authority + "/connect/token",
                    ["end_session_endpoint"] = Authority + "/connect/endsession",
                    ["jwks_uri"] = Authority + "/.well-known/jwks",
                }),
                _ => """{"keys":[]}""",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
