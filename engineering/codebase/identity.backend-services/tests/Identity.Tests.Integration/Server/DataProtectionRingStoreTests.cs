using System.Security.Cryptography;
using AwesomeAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.DataProtection;
using WoW.Two.Sdk.Beta.Be.Identity.Keys;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Server;

/// <summary>
/// The Data Protection ring in <c>identity_data_protection_keys</c>: shared by every replica over one database, wrapped by
/// the ring certificate from the settings, unreadable without it; a local run may keep it unwrapped.
/// </summary>
public sealed class DataProtectionRingStoreTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private readonly string _ring = Bundle();

    public async Task InitializeAsync()
    {
        await using var provider = Build(certificates: [_ring], local: false);
        await IdentityTestDatabase.MigrateAsync(provider);
    }

    [Fact]
    public async Task Ring_ShouldBeSharedByReplicas_AndWrappedAtRest()
    {
        string sealedText;
        await using (var first = Build([_ring], local: false))
            sealedText = Protector(first).Protect("session-cookie-payload");

        await using var second = Build([_ring], local: false);
        Protector(second).Unprotect(sealedText).Should().Be("session-cookie-payload", "a second replica reads the same ring");

        var keys = await KeysAsync(second);
        keys.Should().ContainSingle();
        keys[0].FriendlyName.Should().StartWith("key-");
        keys[0].Xml.Should().Contain(nameof(CertificateRingXmlDecryptor)).And.Contain("EncryptedData").And.NotContain("<value>", "the key's secret is wrapped");
    }

    [Fact]
    public async Task Ring_ShouldStayUnreadable_WithoutItsCertificate()
    {
        string sealedText;
        await using (var owner = Build([_ring], local: false))
            sealedText = Protector(owner).Protect("payload");

        await using var stranger = Build([Bundle()], local: false);
        var unprotect = () => Protector(stranger).Unprotect(sealedText);

        unprotect.Should().Throw<CryptographicException>();
    }

    [Fact]
    public async Task LocalRun_ShouldKeepAnUnwrappedRing_WhenNoCertificateIsListed()
    {
        await using var local = Build(certificates: [], local: true);

        Protector(local).Protect("payload").Should().NotBeNullOrEmpty();

        (await KeysAsync(local)).Should().ContainSingle().Which.Xml.Should().Contain("<value>");
    }

    public Task DisposeAsync() => _database.DisposeAsync().AsTask();

    private static string Bundle()
    {
        using var certificate = SigningCertificateGenerator.CreateDataProtection(DateTimeOffset.UtcNow.AddDays(-1), TimeSpan.FromDays(730));
        return SigningCertificateGenerator.ExportPem(certificate);
    }

    private static IDataProtector Protector(IServiceProvider provider)
        => provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("tests");

    private static async Task<List<IdentityDataProtectionKey>> KeysAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().DataProtectionKeys.AsNoTracking().ToListAsync();
    }

    /// <summary>One replica: the Module and the ring over the shared test database.</summary>
    private ServiceProvider Build(List<string> certificates, bool local)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new RingHostEnvironment { EnvironmentName = local ? Environments.Development : Environments.Production });
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        _database.Register(services);
        services.AddUserAccounts().AddDataProtectionRing(options => options.Certificates = certificates);
        var provider = services.BuildServiceProvider();

        // The boot check a host runs at start.
        _ = provider.GetRequiredService<DataProtectionRingOptions>();
        return provider;
    }

    private sealed class RingHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "Identity.Tests";

        public string ContentRootPath { get; set; } = Path.GetTempPath();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
