using Microsoft.AspNetCore.Http;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;

/// <summary>
/// Provides sign-in sessions (I16): one row per signed-in browser or device, its id the <c>sid</c> claim. A person lists
/// their live sessions and revokes one or all; a revoked, expired or stamp-stale session fails validation, and every
/// <see cref="ISessionRevocationHandler"/> ends what it issued.
/// </summary>
/// <remarks>
///   - a session lives while unrevoked, unexpired and its stamp equals the person's: a stamp rotation ends every
///     session, marked <see cref="SessionRevokeReasonConstants.SecurityStamp"/> when next seen
///   - <c>last_seen_at</c> moves at most once per <see cref="SessionOptions.ActivityInterval"/>
/// </remarks>
/// <param name="sessions">The session store.</param>
/// <param name="users">The core user store, for the current stamp.</param>
/// <param name="options">The lifetime and activity interval.</param>
/// <param name="time">The clock sessions take their instants from.</param>
/// <param name="handlers">What else ends with a session.</param>
/// <param name="http">The current request, for the address and user agent; null outside a request.</param>
/// <param name="signInLog">The sign-in log, to tie a logged attempt to its session; null without the slice.</param>
public sealed class UserSessionService(
    IUserSessionRepository sessions,
    IUserRepository users,
    SessionOptions options,
    TimeProvider time,
    IEnumerable<ISessionRevocationHandler> handlers,
    IHttpContextAccessor? http = null,
    SignInLogService? signInLog = null)
{
    /// <summary>Start a session for a completed sign-in.</summary>
    /// <param name="user">The signed-in person.</param>
    /// <param name="methods">How they signed in, as <c>amr</c> values.</param>
    /// <param name="signInLogEntryId">The logged attempt that signed them in, which then names the session; null when none.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentitySession> StartAsync(IdentityUser user, IEnumerable<string> methods, long? signInLogEntryId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(methods);
        var now = time.GetUtcNow();
        var (ipMasked, userAgent) = PersonalDataMasks.Describe(http?.HttpContext);
        var session = new IdentitySession
        {
            Id = Guid.CreateVersion7(now),
            UserId = user.Id,
            AuthenticationMethods = [.. methods.Distinct(StringComparer.Ordinal)],
            SecurityStamp = user.SecurityStamp,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + options.Lifetime,
            IpMasked = ipMasked,
            UserAgent = userAgent,
        };
        await sessions.AddAsync(session, cancellationToken);
        if (signInLogEntryId is { } entry && signInLog is not null)
            await signInLog.AttachSessionAsync(entry, session.Id, cancellationToken);

        return session;
    }

    /// <summary>Whether <paramref name="sessionId"/> is a live session of <paramref name="userId"/>, recording the use; a stamp-stale one ends on the way.</summary>
    /// <param name="sessionId">The <c>sid</c> presented.</param>
    /// <param name="userId">The person the cookie or token names.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> ValidateAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var session = await sessions.FindAsync(sessionId, cancellationToken);
        if (session is null || session.UserId != userId || session.RevokedAt is not null || session.ExpiresAt <= now)
            return false;

        if (await users.FindByIdAsync(userId, cancellationToken) is not { } user)
            return false;

        if (!IsCurrent(session, user))
        {
            await EndAsync([session.Id], SessionRevokeReasonConstants.SecurityStamp, cancellationToken);
            return false;
        }

        if (now - session.LastSeenAt >= options.ActivityInterval)
            await sessions.TouchAsync(session.Id, now, cancellationToken);

        return true;
    }

    /// <summary>The person's live sessions, newest first; sessions a stamp rotation ended are marked so on the way.</summary>
    /// <param name="user">The person.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<SessionModel>> ListAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var unrevoked = await sessions.ListUnrevokedAsync(user.Id, time.GetUtcNow(), cancellationToken);
        var stale = unrevoked.Where(session => !IsCurrent(session, user)).Select(session => session.Id).ToList();
        if (stale.Count > 0)
            await EndAsync(stale, SessionRevokeReasonConstants.SecurityStamp, cancellationToken);

        return [.. unrevoked.Where(session => IsCurrent(session, user)).Select(SessionModel.From)];
    }

    /// <summary>Revoke one of the person's sessions; false when it is not theirs or already ended.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="sessionId">The session.</param>
    /// <param name="reason">Why, one of <see cref="SessionRevokeReasonConstants"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> RevokeAsync(Guid userId, Guid sessionId, string reason = SessionRevokeReasonConstants.Revoked, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var session = await sessions.FindAsync(sessionId, cancellationToken);
        return session is not null && session.UserId == userId && (await EndAsync([sessionId], reason, cancellationToken)).Count == 1;
    }

    /// <summary>Revoke every unrevoked session of the person, but <paramref name="keep"/>; returns how many ended.</summary>
    /// <param name="user">The person.</param>
    /// <param name="keep">A session to leave live, such as the caller's own; null ends all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<int> RevokeAllAsync(IdentityUser user, Guid? keep = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var ids = (await sessions.ListUnrevokedAsync(user.Id, time.GetUtcNow(), cancellationToken))
            .Select(session => session.Id)
            .Where(id => id != keep)
            .ToList();
        return (await EndAsync(ids, SessionRevokeReasonConstants.RevokedAll, cancellationToken)).Count;
    }

    private static bool IsCurrent(IdentitySession session, IdentityUser user)
        => string.Equals(session.SecurityStamp, user.SecurityStamp, StringComparison.Ordinal);

    private async Task<IReadOnlyList<IdentitySession>> EndAsync(IReadOnlyCollection<Guid> ids, string reason, CancellationToken cancellationToken)
    {
        var revoked = await sessions.RevokeAsync(ids, time.GetUtcNow(), reason, cancellationToken);
        if (revoked.Count > 0)
        {
            foreach (var handler in handlers)
                await handler.HandleAsync(revoked, cancellationToken);
        }

        return revoked;
    }
}
