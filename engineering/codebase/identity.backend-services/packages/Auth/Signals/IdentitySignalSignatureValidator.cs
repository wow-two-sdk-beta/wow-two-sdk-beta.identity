using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks.Inbound;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks.Inbound.Validators;

namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>
/// Validates a revocation signal under <see cref="WebhookSchemeNameConstants.KeySigned"/>: the signature JWT against the keys of
/// the issuer it names, its <c>typ</c>, <c>aud</c> and lifetime, then that it covers this event and this body. A bad signature
/// is a failed result; a signature that can't be checked yet, with the issuer's discovery down, also marks the request, so the
/// gate answers <c>503</c> and the sender retries. The delivery id comes from the signed <c>jti</c>, never the plain header.
/// </summary>
internal sealed partial class IdentitySignalSignatureValidator : IWebhookSignatureValidator, IDisposable
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions BodyReading = new(JsonSerializerDefaults.Web);

    private readonly TrustedIssuerDiscovery _discovery;
    private readonly TrustedIssuerTokenHandler _tokens;
    private readonly TokenValidationParameters _parameters;
    private readonly ILogger<IdentitySignalSignatureValidator> _logger;
    private readonly IHttpContextAccessor _httpContext;

    /// <param name="options">The signal URLs and the issuers.</param>
    /// <param name="scopes">The token handler's scope factory; no resolver runs here.</param>
    /// <param name="httpContext">Supplies the request's abort signal to the token handler.</param>
    /// <param name="logger">Records a signature that could not be checked at all.</param>
    public IdentitySignalSignatureValidator(
        IOptions<IdentitySignalReceiverOptions> options,
        IServiceScopeFactory scopes,
        IHttpContextAccessor httpContext,
        ILogger<IdentitySignalSignatureValidator> logger)
    {
        var settings = options.Value;
        _logger = logger;
        _httpContext = httpContext;

        // An issuer's audiences describe its tokens: left in place they would replace the signal URL in the handler.
        var issuers = settings.TrustedIssuers
            .DistinctBy(issuer => issuer.Issuer, StringComparer.Ordinal)
            .Select(issuer => issuer with { Audiences = [], Partition = null })
            .ToArray();

        // A discovery of its own: the bearer scheme's has its own handler and its own HTTPS rule.
        _discovery = new TrustedIssuerDiscovery(settings.BackchannelHttpHandler, requireHttps: !settings.IsInsecureMetadataAllowedInDevelopment);
        _tokens = new TrustedIssuerTokenHandler(issuers, settings.AcceptedAudiences, hasResolver: false, scopes, _discovery, httpContext);
        _parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            IgnoreTrailingSlashWhenValidatingAudience = false,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidAlgorithms = JwtServiceCollectionExtensions.AsymmetricAlgorithms,
            ValidTypes = [SignalClaimTypeConstants.TokenType],
        };
    }

    /// <inheritdoc />
    /// <remarks>Checking waits on the issuer's JWKS, so this scheme validates only through <see cref="ValidateAsync"/>.</remarks>
    public WebhookSignatureResult Validate(IHeaderDictionary headers, ReadOnlySpan<byte> body, WebhookReceiverOptions receiver, DateTimeOffset now)
        => WebhookSignatureResult.Failed(WebhookSignatureFailure.Malformed);

    /// <inheritdoc />
    public async ValueTask<WebhookSignatureResult> ValidateAsync(IHeaderDictionary headers, ReadOnlyMemory<byte> body, WebhookReceiverOptions receiver, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(headers);
        string? signature = headers[WebhookHeaderConstants.Signature];
        string? eventType = headers[WebhookHeaderConstants.Event];
        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(eventType))
            return WebhookSignatureResult.Failed(WebhookSignatureFailure.Missing);

        TokenValidationResult validated;
        try
        {
            validated = await _tokens.ValidateTokenAsync(signature, ParametersAt(now));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogUncheckable(_logger, exception.GetType().Name);
            if (_httpContext.HttpContext is { } http)
                http.Items[IdentitySignalReceiverConstants.UncheckableItem] = true;

            return WebhookSignatureResult.Failed(WebhookSignatureFailure.Mismatch);
        }

        if (!validated.IsValid || validated.SecurityToken is not JsonWebToken token)
            return WebhookSignatureResult.Failed(IsLifetimeFailure(validated.Exception) ? WebhookSignatureFailure.Stale : WebhookSignatureFailure.Mismatch);

        var covers = !string.IsNullOrEmpty(token.Id)
            && token.TryGetPayloadValue<string>(SignalClaimTypeConstants.Event, out var signedEvent)
            && string.Equals(signedEvent, eventType, StringComparison.Ordinal)
            && token.TryGetPayloadValue<string>(SignalClaimTypeConstants.BodyHash, out var signedHash)
            && HashMatches(signedHash, body.Span)
            && string.Equals(IssuerOf(body.Span), token.Issuer, StringComparison.Ordinal);
        if (!covers)
            return WebhookSignatureResult.Failed(WebhookSignatureFailure.Mismatch);

        return new WebhookSignatureResult
        {
            DeliveryId = token.Id,
            EventType = eventType,
            Timestamp = token.IssuedAt == default ? null : new DateTimeOffset(token.IssuedAt, TimeSpan.Zero),
        };
    }

    /// <inheritdoc />
    public void Dispose() => _discovery.Dispose();

    /// <summary>The parameters for one signal: the lifetime is measured against the receiver's clock, not the machine's.</summary>
    private TokenValidationParameters ParametersAt(DateTimeOffset now)
    {
        var parameters = _parameters.Clone();
        var moment = now.UtcDateTime;
        parameters.LifetimeValidator = (notBefore, expires, _, _) =>
            expires is { } end
            && moment <= end.ToUniversalTime() + ClockSkew
            && (notBefore is not { } start || moment >= start.ToUniversalTime() - ClockSkew);
        return parameters;
    }

    private static bool IsLifetimeFailure(Exception? exception)
        => exception is SecurityTokenInvalidLifetimeException or SecurityTokenExpiredException or SecurityTokenNotYetValidException or SecurityTokenNoExpirationException;

    private static bool HashMatches(string signed, ReadOnlySpan<byte> body)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(signed),
            Encoding.UTF8.GetBytes(Base64Url.EncodeToString(SHA256.HashData(body))));

    /// <summary>The body's <c>Issuer</c>, read as the endpoint reads the event, so the two can't disagree over a duplicate or a cased name.</summary>
    private static string? IssuerOf(ReadOnlySpan<byte> body)
    {
        try
        {
            return JsonSerializer.Deserialize<SignalBody>(body, BodyReading)?.Issuer;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [LoggerMessage(EventId = 7431, Level = LogLevel.Warning, Message = "A signal's signature could not be checked ({Failure}); the signal is refused")]
    private static partial void LogUncheckable(ILogger logger, string failure);

    /// <summary>The one property every signal's body carries for this check.</summary>
    private sealed record SignalBody
    {
        /// <summary>Gets the issuer the body names.</summary>
        public string? Issuer { get; init; }
    }
}
