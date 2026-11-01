using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Identity.Bff;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using Identity.Tests.E2E.Oidc;

namespace Identity.Tests.E2E.Bff;

/// <summary>
/// A BFF on the <c>Client</c> package against the real instance: sign-in → the instance's authorize → the callback, the
/// session route, the API's 401, 403 and 404 problems and antiforgery, the refresh inside the window, and sign-out through
/// the instance's end-session.
/// </summary>
[Collection(BffIdentityCollection.Name)]
public sealed class BffSignInTests(BffIdentityFixture fixture)
{
    private const string Password = "e2e-correct-horse-battery";

    [Fact]
    public async Task CodeFlow_ShouldSignInThroughTheInstance_ServeTheSessionAndTheApi_ThenSignOut()
    {
        await using var app = await BffHost.StartAsync(fixture.Identity);
        using var browser = new BffBrowser(fixture.Identity, app);
        var email = $"bff-{Guid.NewGuid():N}@example.test";
        var userId = await browser.SignUpAtTheInstanceAsync(email, Password);
        var organization = await CreateOrganizationAsync(userId);

        // Signed out: the API answers a 401 problem, a page redirects to the sign-in route.
        using (var api = await browser.GetAsync("/api/me"))
        {
            api.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await CodeOf(api)).Should().Be(BffErrorCodeConstants.SignInRequired);
        }

        using (var page = await browser.GetAsync("/desk"))
            page.Headers.Location!.ToString().Should().Be(BffHost.Origin + "/api/identity/sign-in?returnUrl=%2Fdesk");

        // The code flow lands back on the page, and the session route shows the person, the role and the permissions.
        (await browser.SignInToTheAppAsync("/desk")).Should().Be("/desk");
        var cookie = browser.Cookies.GetCookies(new Uri(BffHost.Origin))[".app.session"]!;
        cookie.Value.Length.Should().BeLessThan(1024, "the cookie carries only the session reference");
        cookie.HttpOnly.Should().BeTrue();

        var session = await browser.SessionAsync();
        session.GetProperty("isAuthenticated").GetBoolean().Should().BeTrue();
        var person = session.GetProperty("person");
        person.GetProperty("sub").GetString().Should().Be(userId.ToString());
        person.GetProperty("name").GetString().Should().Be(email);
        person.GetProperty("email").GetString().Should().Be(email);
        person.GetProperty("phoneNumber").ValueKind.Should().Be(JsonValueKind.Null);
        session.GetProperty("organization").GetProperty("id").GetGuid().Should().Be(organization);
        session.GetProperty("organization").GetProperty("role").GetString().Should().Be("owner");
        session.GetProperty("permissions").EnumerateArray().Select(value => value.GetString()).Should().Equal(BffHost.GrantedPermission);
        session.GetProperty("expiresAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(12), TimeSpan.FromMinutes(2));
        var csrf = session.GetProperty("csrfToken").GetString()!;

        // The API behind the session: reads pass, an unsafe request needs the header, the policies answer 403 and 404.
        using (var me = await browser.GetAsync("/api/me"))
        {
            me.StatusCode.Should().Be(HttpStatusCode.OK);
            JsonDocument.Parse(await me.Content.ReadAsStringAsync()).RootElement.GetProperty("sub").GetString().Should().Be(userId.ToString());
        }

        using (var forged = await browser.SendAsync(HttpMethod.Post, "/api/desks"))
        {
            forged.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await CodeOf(forged)).Should().Be(BffErrorCodeConstants.AntiforgeryValidationFailed);
        }

        using (var allowed = await browser.SendAsync(HttpMethod.Post, "/api/desks", csrf))
            allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var denied = await browser.SendAsync(HttpMethod.Post, "/api/admin", csrf))
        {
            denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await CodeOf(denied)).Should().Be(BffErrorCodeConstants.AccessDenied);
        }

        using (var own = await browser.GetAsync($"/api/orgs/{organization}/desks"))
            own.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var foreign = await browser.GetAsync($"/api/orgs/{Guid.CreateVersion7()}/desks"))
        {
            foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await CodeOf(foreign)).Should().Be(BffErrorCodeConstants.NotFound);
        }

        // Signing out ends the local session and hands the SPA the instance's end-session URL.
        using (var refused = await browser.SendAsync(HttpMethod.Post, "/api/identity/sign-out"))
            refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Uri endSession;
        using (var loggedOut = await browser.SendAsync(HttpMethod.Post, "/api/identity/sign-out", csrf))
        {
            loggedOut.StatusCode.Should().Be(HttpStatusCode.OK);
            using var body = JsonDocument.Parse(await loggedOut.Content.ReadAsStringAsync());
            endSession = new Uri(body.RootElement.GetProperty("data").GetProperty("endSessionUrl").GetString()!);
        }

        endSession.GetLeftPart(UriPartial.Path).Should().Be(BffBrowser.IdentityOrigin + "/connect/endsession");
        QueryHelpers.ParseQuery(endSession.Query)["post_logout_redirect_uri"].ToString().Should().Be(BffHost.Origin + "/");
        (await browser.SessionAsync()).GetProperty("isAuthenticated").GetBoolean().Should().BeFalse();
        using (var afterSignOut = await browser.GetAsync("/api/me"))
            afterSignOut.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The instance ends its own session and returns the browser to the BFF; a new sign-in asks for the credentials again.
        using (var ended = await browser.GetAsync(endSession.ToString()))
        {
            ended.StatusCode.Should().Be(HttpStatusCode.Redirect);
            ended.Headers.Location!.ToString().Should().Be(BffHost.Origin + "/");
        }

        using var signIn = await browser.GetAsync("/api/identity/sign-in?returnUrl=%2Fdesk");
        using var authorize = await browser.GetAsync(signIn.Headers.Location!.ToString());
        authorize.Headers.Location!.OriginalString.Should().StartWith("/sign-in?returnUrl=", "the instance's session ended too");
    }

    [Fact]
    public async Task Refresh_ShouldRotateTheTokensInsideTheWindow_AndEndTheSessionOnceTheInstanceRefuses()
    {
        // A margin past the 10 minute access token makes every request refresh.
        await using var app = await BffHost.StartAsync(fixture.Identity, options => options.AccessTokenRefreshMargin = TimeSpan.FromMinutes(11));
        using var browser = new BffBrowser(fixture.Identity, app);
        var userId = await browser.SignUpAtTheInstanceAsync($"bff-refresh-{Guid.NewGuid():N}@example.test", Password);
        await browser.SignInToTheAppAsync();

        var first = await AccessTokenAsync(browser);
        var second = await AccessTokenAsync(browser);
        second.Should().NotBe(first, "each request past the margin refreshed");
        (await browser.SessionAsync()).GetProperty("person").GetProperty("sub").GetString().Should().Be(userId.ToString());

        // A rotated security stamp makes the instance refuse the refresh: the session ends.
        await using (var scope = fixture.Identity.Host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<UserAccountService>();
            (await accounts.RotateSecurityStampAsync((await accounts.FindByIdAsync(userId))!)).IsSuccess.Should().BeTrue();
        }

        using (var refused = await browser.GetAsync("/api/token"))
            refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await browser.SessionAsync()).GetProperty("isAuthenticated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Session_ShouldEnd_PastItsAbsoluteWindow()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var app = await BffHost.StartAsync(fixture.Identity, time: time);
        using var browser = new BffBrowser(fixture.Identity, app);
        await browser.SignUpAtTheInstanceAsync($"bff-window-{Guid.NewGuid():N}@example.test", Password);
        await browser.SignInToTheAppAsync();
        (await browser.SessionAsync()).GetProperty("isAuthenticated").GetBoolean().Should().BeTrue();

        time.Advance(TimeSpan.FromHours(12) + TimeSpan.FromMinutes(1));

        (await browser.SessionAsync()).GetProperty("isAuthenticated").GetBoolean().Should().BeFalse();
        using var api = await browser.GetAsync("/api/me");
        api.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<string> AccessTokenAsync(BffBrowser browser)
    {
        using var response = await browser.GetAsync("/api/token");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("accessToken").GetString()!;
    }

    private async Task<Guid> CreateOrganizationAsync(Guid userId)
    {
        await using var scope = fixture.Identity.Host.Services.CreateAsyncScope();
        var user = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(userId))!;
        var organization = new IdentityOrganization { Name = "BFF", Slug = $"bff-{Guid.NewGuid():N}"[..20] };
        (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(organization, user)).IsSuccess.Should().BeTrue();
        return organization.Id;
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString();
    }
}

/// <summary>The instance the BFF tests sign in through: the test client registered with the BFF's callback and post-sign-out URIs.</summary>
public sealed class BffIdentityFixture : IAsyncLifetime
{
    /// <summary>Gets the instance.</summary>
    public OidcHost Identity { get; } = new(new Dictionary<string, string?>
    {
        ["Identity:Server:Clients:0:RedirectUris:0"] = BffHost.Origin + "/api/identity/callback",
        ["Identity:Server:Clients:0:PostSignOutRedirectUris:0"] = BffHost.Origin + "/",
    });

    Task IAsyncLifetime.InitializeAsync()
    {
        // Building a client starts the instance: migrations, the key check and the client seeding.
        using var client = Identity.Host.CreateClient();
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync() => await Identity.DisposeAsync();
}

/// <summary>The BFF tests' collection, with its own instance.</summary>
[CollectionDefinition(Name)]
public sealed class BffIdentityCollection : ICollectionFixture<BffIdentityFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "identity-bff";
}
