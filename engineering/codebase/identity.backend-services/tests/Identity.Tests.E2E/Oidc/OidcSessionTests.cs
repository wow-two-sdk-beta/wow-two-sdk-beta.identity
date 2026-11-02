using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;
using WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

namespace Identity.Tests.E2E.Oidc;

/// <summary>
/// Sessions across the real service: a cookie sign-in is a session, its <c>sid</c> rides in the app's tokens, and
/// revoking it, or signing out through end-session, ends the cookie, the app's grant and its refresh chain. The sign-in
/// log names the session, and the cookie's ring lives in the database.
/// </summary>
[Collection(OidcHostCollection.Name)]
public sealed class OidcSessionTests(OidcHostFixture fixture)
{
    private const string Password = "e2e-correct-horse-battery";

    [Fact]
    public async Task RevokingTheSession_ShouldEndTheCookieAndTheAppsTokens()
    {
        using var client = fixture.Server.CreateClient();
        var userId = await client.SignUpAndSignInAsync($"sid-{Guid.NewGuid():N}@example.test", Password);
        var session = (await SessionsAsync(client)).Should().ContainSingle().Subject;
        session.GetProperty("isCurrent").GetBoolean().Should().BeTrue();
        var sid = session.GetProperty("id").GetString();

        var tokens = await client.SignInToAppAsync();
        (await client.ValidateAsync(tokens.IdentityToken, OidcHost.ClientId)).GetClaim("sid").Value.Should().Be(sid);
        (await client.ValidateAsync(tokens.AccessToken, OidcHost.Audience)).GetClaim("sid").Value.Should().Be(sid);
        (await LogAsync(userId)).Should().ContainSingle(row => row.Result == SignInResultConstants.Succeeded).Which.SessionId.Should().Be(Guid.Parse(sid!));

        (await client.Http.DeleteAsync($"/api/account/manage/sessions/{sid}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var refused = await client.RefreshAsync(tokens.RefreshToken);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("invalid_grant");
        using var userInfo = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        userInfo.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        (await client.Http.SendAsync(userInfo)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the access token's session has ended");
        using var authorize = await client.AuthorizeAsync(OidcTestClient.CreatePkce().Challenge);
        authorize.Headers.Location!.OriginalString.Should().StartWith("/sign-in?returnUrl=", "the cookie died with its session");
        (await GrantStatusesAsync(userId)).Should().OnlyContain(status => status == OpenIddictConstants.Statuses.Revoked);
    }

    [Fact]
    public async Task EndSession_ShouldEndTheCookiesSession()
    {
        using var client = fixture.Server.CreateClient();
        var userId = await client.SignUpAndSignInAsync($"bye-{Guid.NewGuid():N}@example.test", Password);
        var tokens = await client.SignInToAppAsync();

        using var ended = await client.Http.GetAsync($"/connect/endsession?id_token_hint={tokens.IdentityToken}");
        ended.StatusCode.Should().Be(HttpStatusCode.Redirect);

        await using var scope = fixture.Server.Host.Services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Sessions.AsNoTracking().SingleAsync(s => s.UserId == userId);
        row.RevokeReason.Should().Be(SessionRevokeReasonConstants.SignOut);
        (await GrantStatusesAsync(userId)).Should().OnlyContain(status => status == OpenIddictConstants.Statuses.Revoked);
    }

    [Fact]
    public async Task Lockout_ShouldRefuseNewPasswordAttempts_ButKeepThePersonsSessionAndAppGrant()
    {
        using var client = fixture.Server.CreateClient();
        var email = $"locked-{Guid.NewGuid():N}@example.test";
        await client.SignUpAndSignInAsync(email, Password);
        var tokens = await client.SignInToAppAsync();

        using var attacker = fixture.Server.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var guess = await attacker.Http.PostAsJsonAsync("/api/account/sign-in?useCookies=true", new { identifier = email, password = "wrong-guess" });
            statuses.Add(guess.StatusCode);
        }

        statuses[^1].Should().Be(HttpStatusCode.TooManyRequests, "the fifth wrong guess locks new password attempts out");
        (await client.RefreshAsync(tokens.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK, "someone else's guesses must not end the person's app session");
        using var authorized = await client.AuthorizeAsync(OidcTestClient.CreatePkce().Challenge);
        OidcTestClient.QueryOf(authorized)["code"].ToString().Should().NotBeNullOrEmpty("the person's own cookie still opens the app");
    }

    [Fact]
    public async Task EndSession_ShouldEndNothing_OnABareGetAnotherSiteCouldSend()
    {
        using var client = fixture.Server.CreateClient();
        var userId = await client.SignUpAndSignInAsync($"csrf-{Guid.NewGuid():N}@example.test", Password);
        var tokens = await client.SignInToAppAsync();

        using var forged = await client.Http.GetAsync("/connect/endsession");
        using var crossSite = await PostEndSessionAsync(client, origin: "https://evil.example");

        forged.StatusCode.Should().Be(HttpStatusCode.Redirect);
        crossSite.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await SessionRowAsync(userId)).RevokedAt.Should().BeNull("neither a hint nor a same-origin POST proved the person asked");
        (await client.Http.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.OK, "the cookie still signs the person in");
        (await client.RefreshAsync(tokens.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK, "the app's grant lives on");
    }

    [Fact]
    public async Task EndSession_ShouldEndTheSession_OnASameOriginPost()
    {
        using var client = fixture.Server.CreateClient();
        var userId = await client.SignUpAndSignInAsync($"own-{Guid.NewGuid():N}@example.test", Password);
        await client.SignInToAppAsync();

        using var ended = await PostEndSessionAsync(client, origin: "https://localhost");

        ended.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await SessionRowAsync(userId)).RevokeReason.Should().Be(SessionRevokeReasonConstants.SignOut);
        (await GrantStatusesAsync(userId)).Should().OnlyContain(status => status == OpenIddictConstants.Statuses.Revoked);
    }

    [Fact]
    public async Task PromptLogin_ShouldNeedASignInAfterTheRequest_NotTheOldCookie()
    {
        using var client = fixture.Server.CreateClient();
        var email = $"fresh-{Guid.NewGuid():N}@example.test";
        await client.SignUpAndSignInAsync(email, Password);
        fixture.Server.Host.Clock.Advance(TimeSpan.FromSeconds(1));

        using var asked = await client.AuthorizeAsync(OidcTestClient.CreatePkce().Challenge, prompt: "login");
        var returnUrl = ReturnUrlOf(asked);
        returnUrl.Should().Contain(OidcServerEndpointRouteBuilderExtensions.FreshSignInParameter + "=").And.NotContain("prompt=");

        using var replayed = await client.Http.GetAsync(returnUrl);
        ReturnUrlOf(replayed).Should().NotBeNull("the cookie's session signed in before the request asked for a fresh sign-in");

        await client.SignInAsync(email, Password);
        using var returned = await client.Http.GetAsync(returnUrl);
        OidcTestClient.QueryOf(returned)["code"].ToString().Should().NotBeNullOrEmpty("a session signed in after the request passes");

        using var forged = await client.Http.GetAsync(returnUrl.Replace(OidcServerEndpointRouteBuilderExtensions.FreshSignInParameter + "=", OidcServerEndpointRouteBuilderExtensions.FreshSignInParameter + "=x", StringComparison.Ordinal));
        ReturnUrlOf(forged).Should().NotBeNull("a marker that does not unseal asks for the sign-in again");
    }

    [Fact]
    public async Task CookieRing_ShouldLiveInTheDatabase()
    {
        using var client = fixture.Server.CreateClient();
        await client.SignUpAndSignInAsync($"ring-{Guid.NewGuid():N}@example.test", Password);

        await using var scope = fixture.Server.Host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().DataProtectionKeys.CountAsync()).Should().BePositive();
    }

    private static Task<HttpResponseMessage> PostEndSessionAsync(OidcTestClient client, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/connect/endsession") { Content = new FormUrlEncodedContent([]) };
        request.Headers.Add("Origin", origin);
        return client.Http.SendAsync(request);
    }

    /// <summary>The return URL of a redirect to the sign-in page, or null when the response is no such redirect.</summary>
    private static string? ReturnUrlOf(HttpResponseMessage response)
    {
        var location = response.Headers.Location?.OriginalString;
        if (response.StatusCode != HttpStatusCode.Redirect || location is null || !location.StartsWith("/sign-in?", StringComparison.Ordinal))
            return null;

        return QueryHelpers.ParseQuery(new Uri(new Uri("https://localhost"), location).Query)["returnUrl"].ToString();
    }

    private async Task<IdentitySession> SessionRowAsync(Guid userId)
    {
        await using var scope = fixture.Server.Host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Sessions.AsNoTracking().SingleAsync(s => s.UserId == userId);
    }

    private static async Task<IReadOnlyList<JsonElement>> SessionsAsync(OidcTestClient client)
    {
        using var response = await client.Http.GetAsync("/api/account/manage/sessions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. document.RootElement.GetProperty("data").EnumerateArray().Select(element => element.Clone())];
    }

    private async Task<List<IdentitySignInLogEntry>> LogAsync(Guid userId)
    {
        await using var scope = fixture.Server.Host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().SignInLog.AsNoTracking().Where(row => row.UserId == userId).ToListAsync();
    }

    /// <summary>The status of every authorization and token issued to the person.</summary>
    private async Task<List<string?>> GrantStatusesAsync(Guid userId)
    {
        await using var scope = fixture.Server.Host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var subject = userId.ToString();
        var authorizations = await context.OidcAuthorizations.Where(a => a.Subject == subject).Select(a => a.Status).ToListAsync();
        var tokens = await context.OidcTokens.Where(t => t.Subject == subject).Select(t => t.Status).ToListAsync();
        authorizations.Should().NotBeEmpty();
        return [.. authorizations, .. tokens];
    }
}
