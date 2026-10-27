namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;

/// <summary>Carries one live session of a person, as their session list shows it.</summary>
public sealed record SessionModel
{
    /// <summary>The session id, the <c>sid</c> claim.</summary>
    public required Guid Id { get; init; }

    /// <summary>When the person signed in (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the session was last used (UTC), to within <see cref="SessionOptions.ActivityInterval"/>.</summary>
    public required DateTimeOffset LastSeenAt { get; init; }

    /// <summary>When the session ends on its own (UTC).</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>How the person signed in, as <c>amr</c> values.</summary>
    public required IReadOnlyList<string> AuthenticationMethods { get; init; }

    /// <summary>The network it signed in from (IPv4 /24, IPv6 /48).</summary>
    public string? IpMasked { get; init; }

    /// <summary>The browser's user agent at sign-in.</summary>
    public string? UserAgent { get; init; }

    /// <summary>The model of <paramref name="session"/>.</summary>
    /// <param name="session">The row.</param>
    public static SessionModel From(IdentitySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new SessionModel
        {
            Id = session.Id,
            CreatedAt = session.CreatedAt,
            LastSeenAt = session.LastSeenAt,
            ExpiresAt = session.ExpiresAt,
            AuthenticationMethods = session.AuthenticationMethods,
            IpMasked = session.IpMasked,
            UserAgent = session.UserAgent,
        };
    }
}
