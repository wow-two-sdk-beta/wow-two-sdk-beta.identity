using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.ExternalAccounts;

/// <summary>Opts external accounts into an identity registration.</summary>
public static class ExternalAccountIdentityBuilderExtensions
{
    /// <summary>Register <see cref="ExternalAccountService"/> and its repository over <see cref="IdentityDbContext"/>.</summary>
    /// <param name="builder">The identity builder.</param>
    public static IdentityBuilder AddExternalAccounts(this IdentityBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddScoped<IExternalAccountRepository, EfExternalAccountRepository>();

        builder.Services.TryAddScoped<ExternalAccountService>();
        return builder;
    }
}
