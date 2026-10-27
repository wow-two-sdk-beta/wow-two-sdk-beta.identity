using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

namespace Identity.Tests.E2E.Oidc;

/// <summary>
/// A relying party against the real service: sign in through the account API, authorize with PKCE, redeem the code,
/// check both tokens against the JWKS, read userinfo, rotate the refresh token, end the session.
/// </summary>
[Collection(OidcHostCollection.Name)]
public sealed class OidcCodeFlowTests(OidcHostFixture fixture)
{
    private const string Password = "e2e-correct-horse-battery";

    [Fact]
    public async Task CodeFlow_ShouldIssueTokensThatPassOnTheJwks_ThenRefreshAndEndTheSession()
    {
        using var client = fixture.Server.CreateClient();
        var email = $"flow-{Guid.NewGuid():N}@example.test";
        var userId = await client.RegisterAndSignInAsync(email, Password);
        var organization = await CreateOrganizationAsync(userId);

        var tokens = await client.SignInToAppAsync();

        // The ID token: for the client, the person's profile, how they signed in and the active organization.
        var idToken = await client.ValidateAsync(tokens.IdentityToken, OidcHost.ClientId);
        idToken.Subject.Should().Be(userId.ToString());
        idToken.GetClaim("nonce").Value.Should().Be("n-0S6_WzA2Mj");
        idToken.GetClaim("email").Value.Should().Be(email);
        idToken.GetClaim("name").Value.Should().Be(email);
        Values(idToken, "amr").Should().Equal(AuthenticationMethodConstants.Password);
        idToken.GetPayloadValue<long>("auth_time").Should().BeGreaterThan(0, "auth_time is a NumericDate");
        idToken.GetPayloadValue<bool>("email_verified").Should().BeFalse("email_verified is a JSON boolean");
        idToken.GetClaim("org_id").Value.Should().Be(organization.ToString());
        idToken.GetClaim("org_role").Value.Should().Be($"{organization}:owner");
        Values(idToken, "org_permission").Should().Equal($"{organization}:e2e:deploy");

        // The access token: an unencrypted JWT for the client's own audience, verified offline.
        var accessToken = await client.ValidateAsync(tokens.AccessToken, OidcHost.Audience);
        accessToken.Subject.Should().Be(userId.ToString());
        accessToken.GetClaim("org_id").Value.Should().Be(organization.ToString());
        Values(accessToken, "amr").Should().Equal(AuthenticationMethodConstants.Password);
        accessToken.TryGetClaim("auth_time", out _).Should().BeFalse();
        accessToken.TryGetClaim("AspNet.Identity.SecurityStamp", out _).Should().BeFalse("the stamp stays inside codes and refresh tokens");
        tokens.ExpiresIn.Should().BeInRange(590, 600);

        // Userinfo answers the current profile for the access token.
        using var userInfoRequest = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        userInfoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var userInfo = await client.Http.SendAsync(userInfoRequest);
        userInfo.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var info = JsonDocument.Parse(await userInfo.Content.ReadAsStringAsync()))
        {
            info.RootElement.GetProperty("sub").GetString().Should().Be(userId.ToString());
            info.RootElement.GetProperty("email").GetString().Should().Be(email);
            info.RootElement.GetProperty("email_verified").GetBoolean().Should().BeFalse();
            info.RootElement.GetProperty("org_permission")[0].GetString().Should().Be($"{organization}:e2e:deploy");
        }

        // A refresh rotates the token and keeps the chain's absolute expiry.
        using var refreshed = await client.RefreshAsync(tokens.RefreshToken);
        var renewed = await TokenSet.ReadAsync(refreshed);
        renewed.RefreshToken.Should().NotBe(tokens.RefreshToken);
        (await client.ValidateAsync(renewed.AccessToken, OidcHost.Audience)).Subject.Should().Be(userId.ToString());
        Values(await client.ValidateAsync(renewed.IdentityToken, OidcHost.ClientId), "amr").Should().Equal(AuthenticationMethodConstants.Password);
        var expiries = await RefreshExpiriesAsync(userId);
        expiries.Should().HaveCount(2).And.OnlyContain(expiry => expiry == expiries[0], "rotation never extends the 12 h chain");
        expiries[0].Should().BeCloseTo(fixture.Server.Host.Clock.GetUtcNow().UtcDateTime.AddHours(12), TimeSpan.FromSeconds(5));

        // End-session ends the Identity session and the app's tokens, then returns to the registered URI.
        using var ended = await client.Http.GetAsync(
            $"/connect/endsession?id_token_hint={renewed.IdentityToken}&post_logout_redirect_uri={Uri.EscapeDataString(OidcHost.PostLogoutRedirectUri)}&state=bye");
        ended.StatusCode.Should().Be(HttpStatusCode.Redirect);
        ended.Headers.Location!.GetLeftPart(UriPartial.Path).Should().Be(OidcHost.PostLogoutRedirectUri);
        OidcTestClient.QueryOf(ended)["state"].ToString().Should().Be("bye");

        using var afterSignOut = await client.AuthorizeAsync(OidcTestClient.CreatePkce().Challenge);
        afterSignOut.Headers.Location!.OriginalString.Should().StartWith("/sign-in?returnUrl=", "the session is gone");
        using var refusedRefresh = await client.RefreshAsync(renewed.RefreshToken);
        refusedRefresh.StatusCode.Should().Be(HttpStatusCode.BadRequest, "end-session revoked the app's tokens");
    }

    [Fact]
    public async Task Authorize_ShouldSendAPersonWithoutASessionToTheSignInPath()
    {
        using var client = fixture.Server.CreateClient();
        var (_, challenge) = OidcTestClient.CreatePkce();

        using var anonymous = await client.AuthorizeAsync(challenge);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = anonymous.Headers.Location!.OriginalString;
        location.Should().StartWith("/sign-in?returnUrl=%2Fconnect%2Fauthorize%3F");
        Uri.UnescapeDataString(location).Should().Contain($"code_challenge={challenge}").And.Contain("client_id=e2e-app");

        using var silent = await client.AuthorizeAsync(challenge, prompt: "none");
        OidcTestClient.QueryOf(silent)["error"].ToString().Should().Be("login_required");
    }

    [Fact]
    public async Task Authorize_ShouldRequirePkceWithS256()
    {
        using var client = fixture.Server.CreateClient();
        await client.RegisterAndSignInAsync($"pkce-{Guid.NewGuid():N}@example.test", Password);

        using var missing = await client.AuthorizeAsync(challenge: null);
        missing.StatusCode.Should().NotBe(HttpStatusCode.Redirect, "no code without a challenge");
        (await OidcTestClient.ErrorOfAsync(missing)).Should().Be("invalid_request");

        var (verifier, _) = OidcTestClient.CreatePkce();
        using var plain = await client.AuthorizeAsync(verifier, method: "plain");
        (await OidcTestClient.ErrorOfAsync(plain)).Should().Be("invalid_request");
    }

    [Fact]
    public async Task Refresh_ShouldBeRefused_AfterTheStampRotates()
    {
        using var client = fixture.Server.CreateClient();
        var userId = await client.RegisterAndSignInAsync($"stamp-{Guid.NewGuid():N}@example.test", Password);
        var tokens = await client.SignInToAppAsync();

        await using (var scope = fixture.Server.Host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<UserAccountService>();
            (await accounts.RotateSecurityStampAsync((await accounts.FindByIdAsync(userId))!)).Succeeded.Should().BeTrue();
        }

        using var refused = await client.RefreshAsync(tokens.RefreshToken);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("invalid_grant");
    }

    [Fact]
    public async Task Discovery_ShouldAdvertiseOnlyTheCodeFlowWithS256()
    {
        using var client = fixture.Server.CreateClient();

        var discovery = await client.DiscoverAsync();

        Strings(discovery, "grant_types_supported").Should().BeEquivalentTo("authorization_code", "refresh_token");
        Strings(discovery, "response_types_supported").Should().Equal("code");
        Strings(discovery, "code_challenge_methods_supported").Should().Equal("S256");
        Strings(discovery, "id_token_signing_alg_values_supported").Should().Contain("RS256");
        Strings(discovery, "claims_supported").Should().Contain(["sub", "amr", "org_id", "org_role", "org_permission"]);
        discovery.GetProperty("end_session_endpoint").GetString().Should().EndWith("/connect/endsession");
        discovery.GetProperty("userinfo_endpoint").GetString().Should().EndWith("/connect/userinfo");
        (await client.GetKeysAsync()).Keys.Should().ContainSingle().Which.Kty.Should().Be("RSA");
    }

    private async Task<Guid> CreateOrganizationAsync(Guid userId)
    {
        await using var scope = fixture.Server.Host.Services.CreateAsyncScope();
        var user = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(userId))!;
        var organization = new IdentityOrganization { Name = "E2E", Slug = $"e2e-{Guid.NewGuid():N}"[..20] };
        (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(organization, user)).Succeeded.Should().BeTrue();
        return organization.Id;
    }

    private async Task<List<DateTime>> RefreshExpiriesAsync(Guid userId)
    {
        await using var scope = fixture.Server.Host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().OidcTokens
            .Where(token => token.Subject == userId.ToString() && token.Type == OpenIddictConstants.TokenTypeIdentifiers.RefreshToken)
            .Select(token => token.ExpirationDate!.Value)
            .ToListAsync();
    }

    private static string[] Values(Microsoft.IdentityModel.JsonWebTokens.JsonWebToken token, string type)
        => token.Claims.Where(claim => claim.Type == type).Select(claim => claim.Value).ToArray();

    private static string[] Strings(JsonElement document, string property)
        => document.GetProperty(property).EnumerateArray().Select(value => value.GetString()!).ToArray();
}
