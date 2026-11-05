using WoW.Two.Sdk.Beta.Be.Messaging;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;

/// <summary>What a person may do in an organization changed: tokens issued before <see cref="At"/> carry the old answer.</summary>
public sealed record AccessChangedEvent : IEvent
{
    /// <summary>Gets the issuer that signed the signal, compared with the signature's own <c>iss</c>; null inside the issuer's process.</summary>
    public string? Issuer { get; init; }

    /// <summary>Gets the person's <c>sub</c>.</summary>
    public required string Subject { get; init; }

    /// <summary>Gets the organization's <c>org_id</c>.</summary>
    public required string OrganizationId { get; init; }

    /// <summary>Gets what changed.</summary>
    public required AccessChangeKind Kind { get; init; }

    /// <summary>Gets when it changed; a token issued from then on already carries the new answer.</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>Gets when the last token issued before the change has expired by; a deny-list entry can go then. Null leaves the receiver its default.</summary>
    public DateTimeOffset? TokensExpireBy { get; init; }
}
