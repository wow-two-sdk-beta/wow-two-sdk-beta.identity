using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

namespace WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

/// <summary>Authenticates a request by the API key it presents — a marked Bearer token or the key header.</summary>
/// <remarks>
/// No key means no result, so another scheme — or the access gate's local pass — decides; a presented key must match
/// a live one in the product's <see cref="IApiKeyRepository"/>. Last use is written at most once per touch interval.
/// </remarks>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<ApiKeyOptions> keyOptions,
    ApiKeySecretFactory secrets,
    ApiKeySecretReader reader,
    TimeProvider time) : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    /// <summary>Holds the failure every unmatched key gets, so a response never tells shape from revocation.</summary>
    private const string NotValid = "The API key is not valid or was revoked.";

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var secret = reader.Read(Request);
        if (secret is null)
            return AuthenticateResult.NoResult();
        if (!secrets.IsSecretShaped(secret))
            return AuthenticateResult.Fail(NotValid);

        var store = Context.RequestServices.GetRequiredService<IApiKeyRepository>();
        var key = await store.FindLiveByHashAsync(ApiKeySecretFactory.ToHash(secret), Context.RequestAborted);
        if (key is null)
            return AuthenticateResult.Fail(NotValid);

        var now = time.GetUtcNow();
        if (key.ExpiresAt is { } expiresAt && expiresAt <= now)
            return AuthenticateResult.Fail(NotValid);

        if (key.LastUsedAt is not { } lastUsed || now - lastUsed >= keyOptions.Value.TouchInterval)
            await store.TouchAsync(key.Id, now, Context.RequestAborted);

        Claim[] claims =
        [
            new Claim(ApiKeyAuthenticationDefaults.KeyIdClaim, key.Id),
            new Claim(JwtRegisteredClaimNames.Sub, key.Id),
            new Claim(ClaimTypes.Name, key.Name),
            .. Optional(JwtRegisteredClaimNames.Iss, keyOptions.Value.Issuer),
            .. Optional(ServiceClaimTypeConstants.ClientId, key.ClientId),
            .. Optional(OrganizationClaimTypeConstants.Id, key.OrganizationId),
            .. Optional(ServiceClaimTypeConstants.Partition, key.Partition),
            .. key.Scopes
                .Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Distinct(StringComparer.Ordinal)
                .Select(scope => new Claim(ApiKeyAuthenticationDefaults.ScopeClaim, scope)),
        ];
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    private static IEnumerable<Claim> Optional(string type, string? value)
        => string.IsNullOrWhiteSpace(value) ? [] : [new Claim(type, value)];
}
