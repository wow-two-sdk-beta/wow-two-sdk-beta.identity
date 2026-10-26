using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Sqlite;
using WoW.Two.Sdk.Beta.Be.Observability.HealthChecks;
using WoW.Two.Sdk.Beta.Be.Identity.Greetings;
using WoW.Two.Sdk.Beta.Be.Identity.Module.Greetings;
using WoW.Two.Sdk.Beta.Be.Identity.Module.Persistence;

namespace WoW.Two.Sdk.Beta.Be.Identity.Module;

/// <summary>Registers the in-process Identity module (Form 1).</summary>
public static class IdentityModuleServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IGreetingsBroker"/> over the module's store, and the store's readiness check.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The options builder, through which the host sets the connection string; it is read on first use.</returns>
    public static OptionsBuilder<IdentityModuleOptions> AddIdentity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<IdentityDbContext>((provider, options) =>
            options.UseSqliteConventional(provider.GetRequiredService<IOptions<IdentityModuleOptions>>().Value.ConnectionString));
        services.AddScoped<IGreetingsBroker, ModuleGreetingsBroker>();
        services.AddHealthChecks().AddDatabaseReadinessCheck<IdentityDbContext>();

        return services.AddOptions<IdentityModuleOptions>()
            .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "IdentityModuleOptions.ConnectionString must be set.");
    }
}
