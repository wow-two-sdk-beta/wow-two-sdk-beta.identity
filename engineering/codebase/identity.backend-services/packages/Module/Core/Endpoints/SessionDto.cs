namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>One of the account's live sign-in sessions, as the session list shows it.</summary>
public sealed record SessionDto
{
    /// <summary>The session id (the <c>sid</c> claim), for revocation.</summary>
    public required Guid Id { get; init; }

    /// <summary>When it signed in.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When it was last used, to within a few minutes.</summary>
    public required DateTimeOffset LastSeenAt { get; init; }

    /// <summary>When it ends on its own.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>How it signed in, as <c>amr</c> values: <c>pwd</c>, <c>hwk</c>, <c>otp</c>, <c>mfa</c>.</summary>
    public required IReadOnlyList<string> AuthenticationMethods { get; init; }

    /// <summary>The network it signed in from (IPv4 /24, IPv6 /48).</summary>
    public string? IpMasked { get; init; }

    /// <summary>The browser's user agent at sign-in.</summary>
    public string? UserAgent { get; init; }

    /// <summary>Whether this is the session of the request asking.</summary>
    public required bool Current { get; init; }
}
