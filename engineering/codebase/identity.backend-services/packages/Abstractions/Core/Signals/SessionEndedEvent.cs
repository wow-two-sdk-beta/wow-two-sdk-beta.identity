using WoW.Two.Sdk.Beta.Be.Messaging;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;

/// <summary>A person's sign-in session ended: what it issued stops counting, at every service that hears this.</summary>
public sealed record SessionEndedEvent : IEvent
{
    /// <summary>Gets the issuer that signed the signal, compared with the signature's own <c>iss</c>; null inside the issuer's process.</summary>
    public string? Issuer { get; init; }

    /// <summary>Gets the ended session's <c>sid</c>.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the person's <c>sub</c>.</summary>
    public required string Subject { get; init; }

    /// <summary>Gets when the session ended.</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>Gets when the last token issued under the session has expired by; a deny-list entry can go then. Null leaves the receiver its default.</summary>
    public DateTimeOffset? TokensExpireBy { get; init; }
}
