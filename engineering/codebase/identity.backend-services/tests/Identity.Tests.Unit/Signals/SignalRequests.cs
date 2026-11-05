using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Signals;

/// <summary>What one signal's signature says; every field defaults to what the sender writes for the signal's own body and target.</summary>
internal sealed record SignalShape
{
    /// <summary>Gets the <c>iss</c>; null is the signal's issuer.</summary>
    public string? Issuer { get; init; }

    /// <summary>Gets the <c>aud</c>; null is the target.</summary>
    public string? Audience { get; init; }

    /// <summary>Gets the <c>typ</c>.</summary>
    public string TokenType { get; init; } = SignalClaimTypeConstants.TokenType;

    /// <summary>Gets the <c>jti</c>; null draws a new delivery id.</summary>
    public string? DeliveryId { get; init; }

    /// <summary>Gets the <c>event</c> claim; null is the event type.</summary>
    public string? SignedEvent { get; init; }

    /// <summary>Gets the <c>X-Webhook-Event</c> header; null is the event type.</summary>
    public string? EventHeader { get; init; }

    /// <summary>Gets the <c>body_sha256</c> claim; null is the hash of the body.</summary>
    public string? BodyHash { get; init; }

    /// <summary>Gets the body sent in place of the one signed.</summary>
    public string? SentBody { get; init; }

    /// <summary>Gets when the signature was signed; null is now.</summary>
    public DateTimeOffset? SignedAt { get; init; }

    /// <summary>Gets when the signature expires; null is 5 minutes after it was signed.</summary>
    public DateTimeOffset? Expires { get; init; }
}

/// <summary>Builds a signed signal POST by hand, with what <c>OidcSignalSignatureIssuer</c> and the webhook publisher send.</summary>
/// <param name="key">The throwaway signing key.</param>
/// <param name="issuer">The issuer the signal and its signature name.</param>
/// <param name="target">The signal URL, the signature's <c>aud</c>.</param>
/// <param name="path">The route the request goes to.</param>
internal sealed class SignalRequests(SecurityKey key, string issuer, string target, string path)
{
    private static readonly JsonWebTokenHandler Tokens = new() { SetDefaultTimesOnTokenCreation = false };

    /// <summary>Gets the issuer signals name.</summary>
    public string Issuer { get; } = issuer;

    /// <summary>A session's end, as the sender serializes it.</summary>
    public SessionEndedEvent SessionEnded(string sessionId, string subject, DateTimeOffset? tokensExpireBy = null) => new()
    {
        Issuer = Issuer,
        SessionId = sessionId,
        Subject = subject,
        At = DateTimeOffset.UtcNow,
        TokensExpireBy = tokensExpireBy ?? DateTimeOffset.UtcNow.AddMinutes(10),
    };

    /// <summary>A changed access, as the sender serializes it.</summary>
    public AccessChangedEvent AccessChanged(string subject, string organizationId, AccessChangeKind kind, DateTimeOffset? at = null, DateTimeOffset? tokensExpireBy = null) => new()
    {
        Issuer = Issuer,
        Subject = subject,
        OrganizationId = organizationId,
        Kind = kind,
        At = at ?? DateTimeOffset.UtcNow,
        TokensExpireBy = tokensExpireBy ?? DateTimeOffset.UtcNow.AddMinutes(10),
    };

    /// <summary>The POST carrying <paramref name="signal"/>, serialized as the publisher does.</summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="signal">The event.</param>
    /// <param name="shape">What the signature says, where it differs from the sender.</param>
    public HttpRequestMessage Post<TEvent>(TEvent signal, SignalShape? shape = null)
        where TEvent : class
        => Post(typeof(TEvent).Name, JsonSerializer.SerializeToUtf8Bytes(signal, signal.GetType()), shape);

    /// <summary>The POST carrying <paramref name="body"/> as an event of <paramref name="eventType"/>.</summary>
    /// <param name="eventType">The event type.</param>
    /// <param name="body">The body as signed.</param>
    /// <param name="shape">What the signature says, where it differs from the sender.</param>
    public HttpRequestMessage Post(string eventType, byte[] body, SignalShape? shape = null)
    {
        var said = shape ?? new SignalShape();
        var signedAt = said.SignedAt ?? DateTimeOffset.UtcNow;
        var deliveryId = said.DeliveryId ?? Guid.NewGuid().ToString("N");
        var signature = Tokens.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = said.Issuer ?? Issuer,
            Audience = said.Audience ?? target,
            IssuedAt = signedAt.UtcDateTime,
            NotBefore = signedAt.UtcDateTime,
            Expires = (said.Expires ?? signedAt.AddMinutes(5)).UtcDateTime,
            TokenType = said.TokenType,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [JwtRegisteredClaimNames.Jti] = deliveryId,
                [SignalClaimTypeConstants.Event] = said.SignedEvent ?? eventType,
                [SignalClaimTypeConstants.BodyHash] = said.BodyHash ?? Base64Url.EncodeToString(SHA256.HashData(body)),
            },
        });

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new ByteArrayContent(said.SentBody is { } sent ? Encoding.UTF8.GetBytes(sent) : body),
        };
        request.Headers.TryAddWithoutValidation(WebhookHeaderConstants.Signature, signature);
        request.Headers.TryAddWithoutValidation(WebhookHeaderConstants.Id, deliveryId);
        request.Headers.TryAddWithoutValidation(WebhookHeaderConstants.Event, said.EventHeader ?? eventType);
        return request;
    }
}
