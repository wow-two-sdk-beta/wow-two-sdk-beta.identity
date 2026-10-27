using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// One signed-in browser or device: a cookie sign-in creates it, its id travels as the <c>sid</c> claim in the cookie
/// and in every OIDC token issued under it, and revoking it ends all of them. Consumed by the sessions slice.
/// </summary>
/// <remarks>A session lives while it is unrevoked, unexpired and its <see cref="SecurityStamp"/> equals the person's: a stamp rotation ends every session.</remarks>
public sealed class IdentitySession : IKeyedEntity<Guid>
{
    /// <summary>Primary key and the <c>sid</c> claim: a uuid v7 made at sign-in.</summary>
    public Guid Id { get; set; }

    /// <summary>The signed-in person.</summary>
    public Guid UserId { get; set; }

    /// <summary>How the person signed in, as <c>amr</c> values (<c>pwd</c>, <c>hwk</c>, <c>otp</c>, <c>mfa</c>).</summary>
    public string[] AuthenticationMethods { get; set; } = [];

    /// <summary>The person's security stamp at sign-in; a rotated stamp ends the session.</summary>
    public string? SecurityStamp { get; set; }

    /// <summary>When the person signed in (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the session was last used (UTC), refreshed at most once per <c>SessionOptions.ActivityInterval</c>.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>When the session ends on its own (UTC): the absolute lifetime from sign-in.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>When the session was ended early; null while live.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Why it ended early, one of <c>SessionRevokeReasonConstants</c>; null while live.</summary>
    public string? RevokeReason { get; set; }

    /// <summary>The address it signed in from, masked to its network (IPv4 /24, IPv6 /48).</summary>
    public string? IpMasked { get; set; }

    /// <summary>The browser's user agent at sign-in, cut to 256 characters.</summary>
    public string? UserAgent { get; set; }
}
