using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Identity.Tests.E2E.Oidc;

/// <summary>A relying party and its browser in one: the account API sign-in, the code flow with PKCE, and token checks against the JWKS.</summary>
public sealed class OidcTestClient : IDisposable
{
    /// <summary>The scopes the test client asks for.</summary>
    public const string Scopes = "openid profile email offline_access";

    private readonly HttpClient _http;
    private readonly TimeProvider _clock;

    internal OidcTestClient(WebApiTestHost<Program> host)
    {
        _http = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        _clock = host.Clock;
    }

    /// <summary>Gets the underlying client.</summary>
    public HttpClient Http => _http;

    /// <summary>Register an account through the account API and sign in with a session cookie; returns the user id.</summary>
    public async Task<Guid> RegisterAndSignInAsync(string email, string password)
    {
        using var registered = await _http.PostAsJsonAsync("/api/account/register", new { email, password });
        registered.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await registered.Content.ReadAsStringAsync());
        var userId = Guid.Parse(body.RootElement.GetProperty("data").GetProperty("userId").GetString()!);
        await SignInAsync(email, password);
        return userId;
    }

    /// <summary>Sign in through the account API with a session cookie.</summary>
    public async Task SignInAsync(string login, string password)
    {
        using var signedIn = await _http.PostAsJsonAsync("/api/account/login?useCookies=true", new { login, password });
        signedIn.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>A fresh PKCE pair: the verifier and its S256 challenge.</summary>
    public static (string Verifier, string Challenge) CreatePkce()
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (verifier, Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    /// <summary>Send an authorization request; the response is the server's redirect.</summary>
    public Task<HttpResponseMessage> AuthorizeAsync(string? challenge, string method = "S256", string? prompt = null, string nonce = "n-0S6_WzA2Mj")
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = OidcHost.ClientId,
            ["redirect_uri"] = OidcHost.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = Scopes,
            ["state"] = "af0ifjsldkj",
            ["nonce"] = nonce,
        };
        if (challenge is not null)
        {
            query["code_challenge"] = challenge;
            query["code_challenge_method"] = method;
        }

        if (prompt is not null)
            query["prompt"] = prompt;

        return _http.GetAsync(QueryHelpers.AddQueryString("/connect/authorize", query));
    }

    /// <summary>Run the code flow for the signed-in person: authorize, then redeem the code with the verifier.</summary>
    public async Task<TokenSet> SignInToAppAsync()
    {
        var (verifier, challenge) = CreatePkce();
        using var authorized = await AuthorizeAsync(challenge);
        var code = QueryOf(authorized)["code"].ToString();
        code.Should().NotBeNullOrEmpty($"the authorization redirect carries a code ({authorized.Headers.Location})");

        using var exchanged = await TokenAsync(new()
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = OidcHost.RedirectUri,
            ["code_verifier"] = verifier,
        });
        return await TokenSet.ReadAsync(exchanged);
    }

    /// <summary>Redeem a refresh token.</summary>
    public async Task<HttpResponseMessage> RefreshAsync(string refreshToken)
        => await TokenAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken });

    /// <summary>Call the token endpoint as the confidential test client.</summary>
    public Task<HttpResponseMessage> TokenAsync(Dictionary<string, string> form)
    {
        form["client_id"] = OidcHost.ClientId;
        form["client_secret"] = OidcHost.ClientSecret;
        return _http.PostAsync("/connect/token", new FormUrlEncodedContent(form));
    }

    /// <summary>The discovery document.</summary>
    public async Task<JsonElement> DiscoverAsync()
    {
        using var document = JsonDocument.Parse(await _http.GetStringAsync("/.well-known/openid-configuration"));
        return document.RootElement.Clone();
    }

    /// <summary>The published key set.</summary>
    public async Task<JsonWebKeySet> GetKeysAsync() => new(await _http.GetStringAsync("/.well-known/jwks"));

    /// <summary>
    /// Validate <paramref name="token"/> as a relying party: signature against the JWKS, issuer, audience and lifetime,
    /// on a clock in step with the server's.
    /// </summary>
    public async Task<JsonWebToken> ValidateAsync(string token, string audience)
    {
        var issuer = (await DiscoverAsync()).GetProperty("issuer").GetString();
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKeys = (await GetKeysAsync()).GetSigningKeys(),
            ValidateLifetime = true,
            LifetimeValidator = (notBefore, expires, _, parameters) =>
            {
                var now = _clock.GetUtcNow().UtcDateTime;
                return (notBefore is null || notBefore <= now + parameters.ClockSkew) && (expires is null || expires >= now - parameters.ClockSkew);
            },
        });

        result.IsValid.Should().BeTrue(result.Exception?.Message);
        return (JsonWebToken)result.SecurityToken;
    }

    /// <summary>The query of a redirect's location.</summary>
    public static Dictionary<string, Microsoft.Extensions.Primitives.StringValues> QueryOf(HttpResponseMessage redirect)
    {
        redirect.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = redirect.Headers.Location!;
        return QueryHelpers.ParseQuery(location.IsAbsoluteUri ? location.Query : new Uri(new Uri("https://localhost"), location).Query);
    }

    /// <summary>
    /// The OIDC error of an authorization response: in the redirect's query once the redirect URI is trusted, else in the
    /// body OpenIddict renders itself (<c>error:…</c> lines), which a request failing before that check gets.
    /// </summary>
    public static async Task<string?> ErrorOfAsync(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Redirect)
            return QueryOf(response)["error"].ToString();

        var body = await response.Content.ReadAsStringAsync();
        return body.Split('\n').FirstOrDefault(line => line.StartsWith("error:", StringComparison.Ordinal))?["error:".Length..].Trim();
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();
}

/// <summary>The tokens a token response carries.</summary>
/// <param name="AccessToken">The access token.</param>
/// <param name="IdentityToken">The ID token.</param>
/// <param name="RefreshToken">The refresh token.</param>
/// <param name="ExpiresIn">The access token's lifetime in seconds.</param>
public sealed record TokenSet(string AccessToken, string IdentityToken, string RefreshToken, int ExpiresIn)
{
    /// <summary>Read a successful token response.</summary>
    public static async Task<TokenSet> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        return new TokenSet(
            root.GetProperty("access_token").GetString()!,
            root.GetProperty("id_token").GetString()!,
            root.GetProperty("refresh_token").GetString()!,
            root.GetProperty("expires_in").GetInt32());
    }
}
