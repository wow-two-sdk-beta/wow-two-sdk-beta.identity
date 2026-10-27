using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Lockout;

/// <summary>Opts the lockout slice into an identity registration.</summary>
public static class LockoutIdentityBuilderExtensions
{
    /// <summary>Register <see cref="UserLockoutService"/> over <see cref="LockoutOptions"/>.</summary>
    /// <param name="builder">The identity builder.</param>
    public static IdentityBuilder AddLockout(this IdentityBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddScoped<UserLockoutService>();
        return builder;
    }
}
