using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Client.Greetings;
using WoW.Two.Sdk.Beta.Be.Identity.Greetings;

namespace WoW.Two.Sdk.Beta.Be.Identity.Client;

/// <summary>Registers the Identity HTTP client (Forms 2 and 3).</summary>
public static class IdentityClientServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IGreetingsBroker"/> over the running Identity service.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Sets the service's root URL.</param>
    /// <returns>The HTTP client builder, for resilience handlers or a test server's handler.</returns>
    public static IHttpClientBuilder AddIdentityClient(this IServiceCollection services, Action<IdentityClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<IdentityClientOptions>()
            .Configure(configure)
            .Validate(options => options.BaseAddress is { IsAbsoluteUri: true }, "IdentityClientOptions.BaseAddress must be an absolute URL.");

        return services.AddHttpClient<IGreetingsBroker, ApiGreetingsBroker>((provider, http) =>
        {
            var root = provider.GetRequiredService<IOptions<IdentityClientOptions>>().Value.BaseAddress!.ToString();
            http.BaseAddress = new Uri(root.EndsWith('/') ? root : root + "/");
        });
    }
}
