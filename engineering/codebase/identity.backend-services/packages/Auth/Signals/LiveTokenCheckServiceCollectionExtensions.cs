using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>Registers what <c>RequireLiveToken</c> calls.</summary>
public static class LiveTokenCheckServiceCollectionExtensions
{
    /// <summary>Registers the introspection client behind <c>policy.RequireLiveToken()</c>; settings are checked at boot.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">The endpoint and the client's credentials.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddLiveTokenCheck(this IServiceCollection services, Action<LiveTokenCheckOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddValidatedOptions(configure, static builder => builder
            .Validate(static o => o.IntrospectionEndpoint is { IsAbsoluteUri: true }, "Live token check: IntrospectionEndpoint must be an absolute URL.")
            .Validate(static o => o.IntrospectionEndpoint is not { IsAbsoluteUri: true } endpoint || endpoint.Scheme == Uri.UriSchemeHttps || endpoint.IsLoopback, "Live token check: IntrospectionEndpoint must use HTTPS unless it is loopback.")
            .Validate(static o => !string.IsNullOrWhiteSpace(o.ClientId), "Live token check: ClientId is required.")
            .Validate(static o => !string.IsNullOrWhiteSpace(o.ClientSecret) || !string.IsNullOrWhiteSpace(o.ClientPrivateKey), "Live token check: ClientSecret or ClientPrivateKey is required."));
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(LiveTokenClient.HttpClientName);
        services.TryAddSingleton<LiveTokenClient>();
        return services;
    }
}
