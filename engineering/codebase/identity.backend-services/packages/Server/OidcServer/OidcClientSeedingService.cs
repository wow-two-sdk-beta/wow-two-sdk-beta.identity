using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>Runs <see cref="OidcClientSeeder"/> once as the host starts, after the schema is in place and the keys passed their check.</summary>
/// <param name="services">The root provider.</param>
internal sealed class OidcClientSeedingService(IServiceProvider services) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OidcClientSeeder>().SeedAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
