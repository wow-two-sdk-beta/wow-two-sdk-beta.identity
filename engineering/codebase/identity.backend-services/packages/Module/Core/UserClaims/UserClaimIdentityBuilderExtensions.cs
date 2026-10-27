using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.UserClaims;

/// <summary>Opts per-user claims into an identity registration.</summary>
public static class UserClaimIdentityBuilderExtensions
{
    /// <summary>
    /// Register <see cref="UserClaimService"/> and its repository over <see cref="IdentityDbContext"/>.
    /// Principals then carry the user's claims.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    public static IdentityBuilder AddUserClaims(this IdentityBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddScoped<IUserClaimRepository, EfUserClaimRepository>();

        builder.Services.TryAddScoped<UserClaimService>();
        return builder;
    }
}
