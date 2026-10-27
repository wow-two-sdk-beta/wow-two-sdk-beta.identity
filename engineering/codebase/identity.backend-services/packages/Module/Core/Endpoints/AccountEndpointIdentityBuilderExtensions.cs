using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Opts the account HTTP API's email support into an identity registration.</summary>
public static class AccountEndpointIdentityBuilderExtensions
{
    /// <summary>
    /// Register <see cref="UserAccountMailService"/> with <see cref="UserAccountEmailSettings"/> bound live from
    /// the container's <c>UserAccounts:Emails</c> section; map the API with <c>MapUserAccountEndpoints</c>.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    public static IdentityBuilder AddAccountEndpoints(this IdentityBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IOptionsChangeTokenSource<UserAccountEmailSettings>)))
        {
            services.AddOptions<UserAccountEmailSettings>()
                .Configure<IServiceProvider>((settings, provider) =>
                    provider.GetService<IConfiguration>()?.GetSection(UserAccountEmailSettings.SectionName).Bind(settings));
            services.AddSingleton<IOptionsChangeTokenSource<UserAccountEmailSettings>>(provider =>
                new ConfigurationChangeTokenSource<UserAccountEmailSettings>(
                    (IConfiguration?)provider.GetService<IConfiguration>()?.GetSection(UserAccountEmailSettings.SectionName)
                        ?? new ConfigurationBuilder().Build()));
        }

        services.TryAddScoped<UserAccountMailService>();
        return builder;
    }
}
