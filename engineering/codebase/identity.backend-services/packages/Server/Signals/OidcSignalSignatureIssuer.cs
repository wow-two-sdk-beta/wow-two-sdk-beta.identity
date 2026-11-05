using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;
using WoW.Two.Sdk.Beta.Be.Identity.Keys;
using WoW.Two.Sdk.Beta.Be.Identity.OidcServer;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks.Issuers;

namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>
/// Signs a signal with the instance's token key, so a receiver verifies it through the JWKS with no shared secret: a short JWT
/// in the signature header, bound to the target URL and to the hash of the body as sent.
/// </summary>
/// <param name="server">OpenIddict's options, holding the signing credentials.</param>
/// <param name="options">The pinned issuer.</param>
internal sealed class OidcSignalSignatureIssuer(IOptionsMonitor<OpenIddictServerOptions> server, OidcServerOptions options) : IWebhookSignatureIssuer
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly JsonWebTokenHandler _tokens = new() { SetDefaultTimesOnTokenCreation = false };

    /// <inheritdoc />
    public string Scheme => WebhookSchemeNameConstants.KeySigned;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Issue(WebhookSigningModel signing)
    {
        ArgumentNullException.ThrowIfNull(signing);
        var issuer = options.Issuer ?? throw new InvalidOperationException($"{OidcServerOptions.SectionName}:Issuer is required for signals; the signature names it.");
        var now = signing.Timestamp.UtcDateTime;
        var credentials = server.CurrentValue.SigningCredentials;
        var signature = _tokens.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer.AbsoluteUri,
            Audience = signing.Url?.AbsoluteUri,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + Lifetime,
            TokenType = SignalClaimTypeConstants.TokenType,
            SigningCredentials = ActiveKeySelectionHandler.Select(credentials, candidate => candidate.Key, signing.Timestamp) ?? credentials[0],
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [JwtRegisteredClaimNames.Jti] = signing.DeliveryId,
                [SignalClaimTypeConstants.Event] = signing.EventType,
                [SignalClaimTypeConstants.BodyHash] = Base64Url.EncodeToString(SHA256.HashData(signing.Payload.Span)),
            },
        });
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [WebhookHeaderConstants.Signature] = signature,
            [WebhookHeaderConstants.Timestamp] = signing.Timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            [WebhookHeaderConstants.Id] = signing.DeliveryId,
        };
    }
}
