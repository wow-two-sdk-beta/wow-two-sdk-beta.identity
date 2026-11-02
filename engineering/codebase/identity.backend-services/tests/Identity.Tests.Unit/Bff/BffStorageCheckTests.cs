using AwesomeAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Identity.Bff;
using WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Bff;

/// <summary>A deployed boot warns, without refusing, while the BFF keeps its sessions or its key ring where a restart loses them.</summary>
public sealed class BffStorageCheckTests
{
    [Fact]
    public async Task DeployedBoot_ShouldWarnOfTheInMemoryCache_AndTheUnpersistedKeyRing()
    {
        var logger = new CapturingLogger();
        await using var provider = Provider(isDeployed: true);

        await new BffStorageCheck(provider, provider.GetRequiredService<IConfiguration>(), logger).StartAsync(CancellationToken.None);

        logger.Warnings.Should().HaveCount(2);
        logger.Warnings.Should().Contain(message => message.Contains("in-memory distributed cache", StringComparison.Ordinal));
        logger.Warnings.Should().Contain(message => message.Contains("key ring is not persisted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeployedBoot_ShouldStayQuiet_WithASharedCacheAndAPersistedRing()
    {
        var logger = new CapturingLogger();
        var keys = Directory.CreateTempSubdirectory("bff-keys-");
        try
        {
            await using var provider = Provider(isDeployed: true, services =>
            {
                services.AddDataProtection().PersistKeysToFileSystem(keys);
                services.AddSingleton<IDistributedCache, SharedCache>();
            });

            await new BffStorageCheck(provider, provider.GetRequiredService<IConfiguration>(), logger).StartAsync(CancellationToken.None);

            logger.Warnings.Should().BeEmpty();
        }
        finally
        {
            keys.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LocalRun_ShouldStayQuiet_OnTheDefaults()
    {
        if (Environment.GetEnvironmentVariable("DEPLOY_ENVIRONMENT") is { Length: > 0 })
            return;

        var logger = new CapturingLogger();
        await using var provider = Provider(isDeployed: false);

        await new BffStorageCheck(provider, provider.GetRequiredService<IConfiguration>(), logger).StartAsync(CancellationToken.None);

        logger.Warnings.Should().BeEmpty();
    }

    private static ServiceProvider Provider(bool isDeployed, Action<IServiceCollection>? configure = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Identity:Client:Authority"] = "https://id.example.test",
            ["Identity:Client:ClientId"] = "desk",
        };
        if (isDeployed)
            settings["DEPLOY_ENVIRONMENT"] = "dev";

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddLogging();
        configure?.Invoke(services);
        services.AddIdentityBff();
        return services.BuildServiceProvider();
    }

    private sealed class CapturingLogger : ILogger<BffStorageCheck>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    /// <summary>A stand-in for a shared cache such as Redis.</summary>
    private sealed class SharedCache : IDistributedCache
    {
        public byte[]? Get(string key) => null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key)
        {
        }

        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
    }
}
