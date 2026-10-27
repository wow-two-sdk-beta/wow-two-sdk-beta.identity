using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.RefreshTokens;

/// <summary>Opts rotating refresh tokens into an identity registration.</summary>
public static class RefreshTokenIdentityBuilderExtensions
{
    /// <summary>Register <see cref="RefreshTokenService"/> and its repository over <see cref="IdentityDbContext"/>.</summary>
    /// <param name="builder">The identity builder.</param>
    /// <param name="configure">Optional lifetime.</param>
    public static IdentityBuilder AddRefreshTokens(this IdentityBuilder builder, Action<RefreshTokenOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddValidatedOptions(
            configure,
            options => options.Validate(o => o.Lifetime > TimeSpan.Zero, "RefreshTokenOptions.Lifetime must be positive."));
        builder.Services.TryAddScoped<IRefreshTokenRepository, EfRefreshTokenRepository>();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddScoped<RefreshTokenService>();
        return builder;
    }
}
