using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>Registers the token deny list.</summary>
public static class TokenDenyListServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ITokenDenyList"/> over <c>IDistributedCache</c> (in memory unless a shared one is registered, which
    /// replicas need), its signal handler, and the refusal on the default <c>JwtBearer</c> scheme. A host's own
    /// <c>OnTokenValidated</c> keeps running. Each bearer request reads up to two more cache keys.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Sets the default entry lifetime; null keeps 1 hour.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddTokenDenyList(this IServiceCollection services, Action<DenyListOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddValidatedOptions(
            configure,
            static builder => builder.Validate(static o => o.DefaultEntryLifetime > TimeSpan.Zero, "Token deny list: DefaultEntryLifetime must be positive."));
        services.AddDistributedMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITokenDenyList, DistributedCacheTokenDenyList>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<JwtBearerOptions>, TokenDenyListBearerEvents>());
        return services.AddSignalHandlers();
    }
}
