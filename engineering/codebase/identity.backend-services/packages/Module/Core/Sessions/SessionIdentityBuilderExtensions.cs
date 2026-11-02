using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;

/// <summary>Opts sign-in sessions into an identity registration.</summary>
public static class SessionIdentityBuilderExtensions
{
    /// <summary>
    /// Register <see cref="UserSessionService"/> and its repository over <see cref="IdentityDbContext"/>, and check every
    /// cookie principal that carries a <c>sid</c> against its row: a revoked, expired or stamp-stale session is rejected
    /// and signed out, and a cookie whose sliding session moved its end is reissued to end with it. The account API then
    /// starts a session per cookie sign-in and maps <c>manage/sessions</c>.
    /// </summary>
    /// <remarks>A cookie without <c>sid</c> (issued before the slice, or not by the account API) passes; the stamp check still covers it.</remarks>
    /// <param name="builder">The identity builder.</param>
    /// <param name="configure">Optional expiration, lifetimes and activity interval; the host section <c>Identity:Sessions</c> is applied last.</param>
    public static IdentityBuilder AddSessions(this IdentityBuilder builder, Action<SessionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.AddModuleOptions(
            SessionOptions.SectionName,
            configure,
            options => options
                .Validate(o => Enum.IsDefined(o.Expiration), "Identity:Sessions:Expiration must be Fixed or Sliding.")
                .Validate(o => o.Lifetime > TimeSpan.Zero, "Identity:Sessions:Lifetime must be positive.")
                .Validate(o => o.ExtendedLifetime is null || o.ExtendedLifetime > o.Lifetime, "Identity:Sessions:ExtendedLifetime must be longer than Lifetime, or null to offer no choice to stay signed in.")
                .Validate(o => o.SlidingLifetime > TimeSpan.Zero, "Identity:Sessions:SlidingLifetime must be positive.")
                .Validate(o => o.ActivityInterval >= TimeSpan.Zero, "Identity:Sessions:ActivityInterval must not be negative.")
                .Validate(o => o.Expiration != SessionExpiration.Sliding || o.ActivityInterval < o.SlidingLifetime, "Identity:Sessions:ActivityInterval must be shorter than SlidingLifetime, or a used session ends before its end moves."));
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IUserSessionRepository, EfUserSessionRepository>();
        services.TryAddScoped<UserSessionService>();

        services.PostConfigureAll<CookieAuthenticationOptions>(cookie =>
        {
            var previous = cookie.Events.OnValidatePrincipal;
            cookie.Events.OnValidatePrincipal = async context =>
            {
                // Checked even when an earlier hook (the stamp check) rejected it, so a stale session's row ends now.
                var presented = context.Principal;
                await previous(context);
                if (presented?.Identity is not { IsAuthenticated: true })
                    return;

                var claims = context.HttpContext.RequestServices.GetRequiredService<IdentityCoreOptions>().Claims;
                var sessionId = presented.FindFirst(claims.SessionIdClaimType)?.Value;
                if (sessionId is null)
                    return;

                var sessions = context.HttpContext.RequestServices.GetRequiredService<UserSessionService>();
                var live = Guid.TryParse(sessionId, out var sid)
                    && Guid.TryParse(presented.FindFirst(claims.UserIdClaimType)?.Value, out var userId)
                        ? await sessions.FindLiveAsync(sid, userId, context.HttpContext.RequestAborted)
                        : null;
                if (live is null)
                {
                    if (context.Principal is not null)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(context.Scheme.Name);
                    }

                    return;
                }

                // A sliding session's end moved: reissue the cookie to end with it.
                if (context.Principal is not null && context.Properties.ExpiresUtc is { } cookieEnd && live.ExpiresAt > cookieEnd)
                {
                    context.Properties.IssuedUtc = live.LastSeenAt;
                    context.Properties.ExpiresUtc = live.ExpiresAt;
                    context.Properties.IsPersistent = true;
                    context.ShouldRenew = true;
                }
            };
        });

        return builder;
    }
}
