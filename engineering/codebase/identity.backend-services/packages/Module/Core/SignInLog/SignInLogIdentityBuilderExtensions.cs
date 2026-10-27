using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;

/// <summary>Opts the sign-in log into an identity registration.</summary>
public static class SignInLogIdentityBuilderExtensions
{
    /// <summary>
    /// Register <see cref="SignInLogService"/> and its repository over <see cref="IdentityDbContext"/>, so
    /// <c>SignInService</c> records every attempt, and the cleanup job that deletes rows past
    /// <see cref="SignInLogOptions.RetentionDays"/>.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    /// <param name="configure">Optional retention and interval; the host section <c>Identity:SignInLog</c> is applied last.</param>
    public static IdentityBuilder AddSignInLog(this IdentityBuilder builder, Action<SignInLogOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.AddModuleOptions(
            SignInLogOptions.SectionName,
            configure,
            options => options
                .Validate(o => o.RetentionDays > 0, "SignInLogOptions.RetentionDays must be positive.")
                .Validate(o => o.CleanupInterval >= TimeSpan.FromSeconds(1), "SignInLogOptions.CleanupInterval must be at least one second."));
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ISignInLogRepository, EfSignInLogRepository>();
        services.TryAddScoped<SignInLogService>();
        services.AddHostedService<SignInLogCleanupService>();
        return builder;
    }
}
