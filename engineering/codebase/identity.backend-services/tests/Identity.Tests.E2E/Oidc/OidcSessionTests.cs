using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;

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
        var userId = await client.RegisterAndSignInAsync($"sid-{Guid.NewGuid():N}@example.test", Password);
        var session = (await SessionsAsync(client)).Should().ContainSingle().Subject;
        session.GetProperty("current").GetBoolean().Should().BeTrue();
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
        var userId = await client.RegisterAndSignInAsync($"bye-{Guid.NewGuid():N}@example.test", Password);
        var tokens = await client.SignInToAppAsync();

        using var ended = await client.Http.GetAsync($"/connect/endsession?id_token_hint={tokens.IdentityToken}");
        ended.StatusCode.Should().Be(HttpStatusCode.Redirect);

        await using var scope = fixture.Server.Host.Services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Sessions.AsNoTracking().SingleAsync(s => s.UserId == userId);
        row.RevokeReason.Should().Be(SessionRevokeReasonConstants.SignOut);
        (await GrantStatusesAsync(userId)).Should().OnlyContain(status => status == OpenIddictConstants.Statuses.Revoked);
    }

    [Fact]
    public async Task CookieRing_ShouldLiveInTheDatabase()
    {
        using var client = fixture.Server.CreateClient();
        await client.RegisterAndSignInAsync($"ring-{Guid.NewGuid():N}@example.test", Password);

        await using var scope = fixture.Server.Host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().DataProtectionKeys.CountAsync()).Should().BePositive();
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
