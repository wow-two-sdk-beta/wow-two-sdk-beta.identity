using WoW.Two.Sdk.Beta.Be.Identity.Core.SecurityStamps;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;

/// <summary>
/// The rule for a person whose roles changed: an instance role granted or removed, an organization joined (a member added,
/// an invitation redeemed), a role changed, a membership removed or its organization deleted. Every claim a sign-in issued
/// (the Identity cookie's roles, an app's <c>org_role</c> and <c>org_permission</c>, a BFF session's tokens) still carries
/// the old roles, so the person signs in again: their security stamp rotates and every sign-in session ends at once, marked
/// <see cref="SessionRevokeReasonConstants.RolesChanged"/>.
/// </summary>
/// <remarks>
///   - the revocation runs every <see cref="ISessionRevocationHandler"/>: the cookie fails its next request, each app's
///     refresh chain is refused at its next refresh, and so each BFF session ends at its next refresh
///   - an access token already issued lives out its own lifetime at an API that checks it offline; userinfo refuses it at once
///   - a registration without sessions (no <c>AddSessions</c>) only rotates the stamp, which the stamp check and the OIDC
///     token endpoint enforce on their own
///   - this host's cached stamp is dropped (<see cref="SecurityStampValidator.Forget"/>), so a sign-in after the change (the
///     invitation sign-up, an invited phone sign-in, signing in again) takes the new stamp and stays signed in at once
/// </remarks>
public static class RoleChangeSignOut
{
    /// <summary>Rotate <paramref name="user"/>'s security stamp and revoke all their sign-in sessions; the rotation's result.</summary>
    /// <param name="user">The person whose roles changed; their stamp rotates in place.</param>
    /// <param name="accounts">The core account service, for stamp rotation.</param>
    /// <param name="sessions">The sign-in sessions; null without the sessions slice.</param>
    /// <param name="stamps">The stamp check, whose cached stamp the change drops; null without stamp validation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<IdentityResult> ApplyAsync(
        IdentityUser user,
        UserAccountService accounts,
        UserSessionService? sessions,
        SecurityStampValidator? stamps,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(accounts);

        // Rotate first: a session the revocation misses, started meanwhile under the old stamp, still ends as stamp-stale.
        var rotated = await accounts.RotateSecurityStampAsync(user, cancellationToken);
        if (!rotated.IsSuccess)
            return rotated;

        stamps?.Forget(user.Id);
        if (sessions is not null)
            await sessions.RevokeAllAsync(user, keep: null, SessionRevokeReasonConstants.RolesChanged, cancellationToken);

        return rotated;
    }
}
