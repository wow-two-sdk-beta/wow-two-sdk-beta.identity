using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

/// <summary>
/// Warns at boot on a deployed host (<c>DEPLOY_ENVIRONMENT</c> set) when the BFF's sessions would not outlive the process:
/// the sessions sit in the in-memory distributed cache, or the Data Protection key ring that encrypts them and the cookie
/// is not persisted. Either signs every person out on each restart or deploy. It only warns, so a host still boots.
/// </summary>
/// <param name="services">The application's services.</param>
/// <param name="configuration">The host configuration, read for <c>DEPLOY_ENVIRONMENT</c>.</param>
/// <param name="logger">Receives the warnings.</param>
internal sealed partial class BffStorageCheck(IServiceProvider services, IConfiguration configuration, ILogger<BffStorageCheck> logger) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.IsDeployed())
            return Task.CompletedTask;

        if (IsSessionStoreInMemory(services))
            LogInMemorySessions(logger);
        if (!IsKeyRingPersisted(services))
            LogUnpersistedKeyRing(logger);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Whether the sessions sit in process memory: the default store over the in-memory distributed cache.</summary>
    /// <param name="services">The application's services.</param>
    internal static bool IsSessionStoreInMemory(IServiceProvider services)
        => services.GetService<IBffSessionRepository>() is DistributedCacheBffSessionRepository
            && services.GetService<IDistributedCache>() is MemoryDistributedCache;

    /// <summary>Whether the Data Protection key ring names a store of its own (<c>PersistKeysTo…</c>), not the default folder of the process.</summary>
    /// <param name="services">The application's services.</param>
    internal static bool IsKeyRingPersisted(IServiceProvider services)
        => services.GetService<IOptions<KeyManagementOptions>>()?.Value.XmlRepository is not null;

    [LoggerMessage(EventId = 7431, Level = LogLevel.Warning,
        Message = "BFF sessions are kept in the in-memory distributed cache, so every restart or deploy signs every person out; register a shared IDistributedCache (Redis, PostgreSQL, SQL Server) before AddIdentityBff")]
    private static partial void LogInMemorySessions(ILogger logger);

    [LoggerMessage(EventId = 7432, Level = LogLevel.Warning,
        Message = "The Data Protection key ring is not persisted, so the BFF's cookies and stored sessions stop decrypting after a restart or deploy; persist it with AddDataProtection().PersistKeysTo… and share it across instances")]
    private static partial void LogUnpersistedKeyRing(ILogger logger);
}
