using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.WebUtilities;

namespace Identity.Tests.E2E.Oidc;

/// <summary>The real Identity host registers a public native app and completes its flow without a client credential.</summary>
public sealed class NativeOidcCodeFlowTests
{
    private const string ClientId = "native-e2e-app";
    private const string RedirectUri = "com.example.app:/signin";

    [Fact]
    public async Task NativeClient_ShouldRequireS256_AndExchangeAndRefreshWithoutASecret()
    {
        await using var host = new OidcHost(new Dictionary<string, string?>
        {
            ["Identity:Server:Clients:1:ClientId"] = ClientId,
            ["Identity:Server:Clients:1:Type"] = "Native",
            ["Identity:Server:Clients:1:RedirectUris:0"] = RedirectUri,
        });
        using var client = host.CreateClient();
        var userId = await client.SignUpAndSignInAsync($"native-{Guid.NewGuid():N}@example.test", "e2e-correct-horse-battery");
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = ClientId,
            ["redirect_uri"] = RedirectUri,
            ["response_type"] = "code",
            ["scope"] = OidcTestClient.Scopes,
            ["state"] = "native-state",
            ["nonce"] = "native-nonce",
        };

        using var withoutPkce = await client.Http.GetAsync(QueryHelpers.AddQueryString("/connect/authorize", query));
        (await OidcTestClient.ErrorOfAsync(withoutPkce)).Should().Be("invalid_request");

        var (verifier, challenge) = OidcTestClient.CreatePkce();
        query["code_challenge"] = challenge;
        query["code_challenge_method"] = "S256";
        using var authorization = await client.Http.GetAsync(QueryHelpers.AddQueryString("/connect/authorize", query));
        authorization.StatusCode.Should().Be(HttpStatusCode.Redirect);
        authorization.Headers.Location!.GetLeftPart(UriPartial.Path).Should().Be(RedirectUri);
        var callback = OidcTestClient.QueryOf(authorization);
        callback["state"].ToString().Should().Be("native-state");
        var code = callback["code"].ToString();
        code.Should().NotBeNullOrWhiteSpace();

        using var exchanged = await client.Http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = verifier,
        }));
        var tokens = await TokenSet.ReadAsync(exchanged);
        (await client.ValidateAsync(tokens.AccessToken, ClientId)).Subject.Should().Be(userId.ToString());

        using var refreshed = await client.Http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = tokens.RefreshToken,
        }));
        var renewed = await TokenSet.ReadAsync(refreshed);
        renewed.RefreshToken.Should().NotBe(tokens.RefreshToken);
    }
}
