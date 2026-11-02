namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;

/// <summary>Holds how long a sign-in session lasts and how often its last use is recorded.</summary>
/// <remarks>
/// Set in code with <c>.AddSessions(o => …)</c> or in the host section <c>Identity:Sessions</c>, which is applied last.
/// The session's end is the end of everything issued under it: the cookie, and the OIDC refresh chain of every app.
/// </remarks>
public sealed record SessionOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:Sessions";

    /// <summary>Gets or sets how the session's end is set. Default <see cref="SessionExpiration.Fixed"/>.</summary>
    public SessionExpiration Expiration { get; set; } = SessionExpiration.Fixed;

    /// <summary>
    /// Gets or sets the absolute lifetime of a <see cref="SessionExpiration.Fixed"/> session from sign-in, when the person
    /// does not ask to stay signed in; its cookie then ends with the browser too. Default 12 hours.
    /// </summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// Gets or sets the absolute lifetime of a <see cref="SessionExpiration.Fixed"/> session whose sign-in asked to stay
    /// signed in (<c>isPersistent</c>), with a cookie that outlives the browser; null offers no such choice. Default 30 days.
    /// </summary>
    public TimeSpan? ExtendedLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Gets or sets how long a <see cref="SessionExpiration.Sliding"/> session lives after its last use: each use, at most once
    /// per <see cref="ActivityInterval"/>, moves its end this far ahead. Default 7 days.
    /// </summary>
    public TimeSpan SlidingLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Gets or sets how stale <c>last_seen_at</c> may grow before a request refreshes it. Default 5 minutes.</summary>
    public TimeSpan ActivityInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Whether a sign-in may ask to stay signed in: a fixed session with an <see cref="ExtendedLifetime"/>.</summary>
    public bool IsPersistenceOffered => Expiration == SessionExpiration.Fixed && ExtendedLifetime is not null;

    /// <summary>Whether a session's cookie outlives the browser: always while sliding, else when the sign-in asked and may.</summary>
    /// <param name="isPersistent">Whether the sign-in asked to stay signed in.</param>
    public bool IsCookiePersistent(bool isPersistent)
        => Expiration == SessionExpiration.Sliding || (isPersistent && IsPersistenceOffered);

    /// <summary>The lifetime a new session takes; a request to stay signed in that is not offered takes <see cref="Lifetime"/>.</summary>
    /// <param name="isPersistent">Whether the sign-in asked to stay signed in.</param>
    public TimeSpan LifetimeOf(bool isPersistent) => Expiration switch
    {
        SessionExpiration.Sliding => SlidingLifetime,
        _ when isPersistent && ExtendedLifetime is { } extended => extended,
        _ => Lifetime,
    };
}
