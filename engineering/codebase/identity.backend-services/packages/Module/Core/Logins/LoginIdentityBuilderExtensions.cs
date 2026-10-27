using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Logins;

/// <summary>Opts external logins into an identity registration.</summary>
public static class LoginIdentityBuilderExtensions
{
    /// <summary>Register <see cref="UserLoginService"/> and its repository over <see cref="IdentityDbContext"/>.</summary>
    /// <param name="builder">The identity builder.</param>
    public static IdentityBuilder AddExternalLogins(this IdentityBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddScoped<IUserLoginRepository, EfUserLoginRepository>();

        builder.Services.TryAddScoped<UserLoginService>();
        return builder;
    }
}
