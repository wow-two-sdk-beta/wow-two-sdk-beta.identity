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
    /// and signed out. The account API then starts a session per cookie sign-in and maps <c>manage/sessions</c>.
    /// </summary>
    /// <remarks>A cookie without <c>sid</c> (issued before the slice, or not by the account API) passes; the stamp check still covers it.</remarks>
    /// <param name="builder">The identity builder.</param>
    /// <param name="configure">Optional lifetime and activity interval; the host section <c>Identity:Sessions</c> is applied last.</param>
    public static IdentityBuilder AddSessions(this IdentityBuilder builder, Action<SessionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.AddModuleOptions(
            SessionOptions.SectionName,
            configure,
            options => options
                .Validate(o => o.Lifetime > TimeSpan.Zero, "SessionOptions.Lifetime must be positive.")
                .Validate(o => o.ActivityInterval >= TimeSpan.Zero, "SessionOptions.ActivityInterval must not be negative."));
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
                    && await sessions.ValidateAsync(sid, userId, context.HttpContext.RequestAborted);
                if (!live && context.Principal is not null)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(context.Scheme.Name);
                }
            };
        });

        return builder;
    }
}
