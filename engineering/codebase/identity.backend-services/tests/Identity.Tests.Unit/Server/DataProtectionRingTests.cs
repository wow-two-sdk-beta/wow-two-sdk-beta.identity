using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Identity.DataProtection;
using WoW.Two.Sdk.Beta.Be.Identity.Keys;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Server;

/// <summary>The ring's boot check and its certificate wrapping, without a database: fail closed when deployed, unwrap only with a listed certificate.</summary>
public sealed class DataProtectionRingTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly XElement Secret = new("key", new XAttribute("id", "1"), new XElement("masterKey", new XElement("value", "c2VjcmV0LXJpbmcta2V5")));
    private readonly KeyFiles _files = new();

    [Theory]
    [InlineData("Development", "dev")]
    [InlineData("Production", null)]
    public void Validator_ShouldRefuseAMissingCertificate_OnADeployedBoot(string environment, string? deployEnvironment)
    {
        var result = Validate(new DataProtectionRingOptions(), environment, deployEnvironment);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Identity:DataProtection:Certificates lists no certificate").And.Contain("keys generate --role data-protection");
    }

    [Fact]
    public void Validator_ShouldAllowAnUnwrappedRing_OnALocalRun()
        => Validate(new DataProtectionRingOptions(), Environments.Development, deployEnvironment: null).Succeeded.Should().BeTrue();

    [Fact]
    public void Validator_ShouldPassAnInlineRingCertificate_OnADeployedBoot()
        => Validate(new DataProtectionRingOptions { Certificates = [RingBundle(Now.AddDays(-1))] }, Environments.Production, "prod").Succeeded.Should().BeTrue();

    [Fact]
    public void Validator_ShouldRefuseUnfitCertificates_WithoutPrintingThem()
    {
        using var small = KeyFiles.Custom(1024, X509KeyUsageFlags.KeyEncipherment, Now.AddDays(-1));
        using var signingOnly = KeyFiles.Custom(2048, X509KeyUsageFlags.DigitalSignature, Now.AddDays(-1));
        using var expired = SigningCertificateGenerator.CreateDataProtection(Now.AddDays(-40), TimeSpan.FromDays(30));
        var options = new DataProtectionRingOptions { Certificates = [Pem(small), Pem(signingOnly)] };
        var expiredOnly = new DataProtectionRingOptions { Certificates = [_files.WriteText("expired.pem", SigningCertificateGenerator.ExportPem(expired))] };

        var unfit = Validate(options, Environments.Production, "prod");
        var stale = Validate(expiredOnly, Environments.Production, "prod");

        unfit.FailureMessage.Should().Contain("1024-bit key").And.Contain("lacks the KeyEncipherment key usage").And.NotContain("PRIVATE KEY");
        stale.FailureMessage.Should().Contain("No Data Protection ring certificate is valid now");
    }

    [Fact]
    public void Wrapping_ShouldRoundTrip_ThroughAnyListedCertificate()
    {
        using var old = SigningCertificateGenerator.CreateDataProtection(Now.AddDays(-100), TimeSpan.FromDays(130));
        using var next = SigningCertificateGenerator.CreateDataProtection(Now.AddDays(-1), TimeSpan.FromDays(730));
        var wrapped = new CertificateRingXmlEncryptor(old, NullLoggerFactory.Instance).Encrypt(Secret);

        wrapped.DecryptorType.Should().Be<CertificateRingXmlDecryptor>();
        wrapped.EncryptedElement.ToString().Should().NotContain("c2VjcmV0LXJpbmcta2V5", "the key's secret never lands in clear");

        using var rotated = Ring([Pem(next), Pem(old)]);
        rotated.GetRequiredService<DataProtectionRingCertificates>().Active!.Thumbprint.Should().Be(next.Thumbprint, "the certificate expiring last wraps new keys");
        new CertificateRingXmlDecryptor(rotated).Decrypt(wrapped.EncryptedElement).ToString().Should().Be(Secret.ToString(), "a retiring certificate still unwraps what it wrapped");
    }

    [Fact]
    public void Unwrapping_ShouldFail_WithoutTheWrappingCertificate()
    {
        using var wrapping = SigningCertificateGenerator.CreateDataProtection(Now.AddDays(-1), TimeSpan.FromDays(730));
        using var other = SigningCertificateGenerator.CreateDataProtection(Now.AddDays(-1), TimeSpan.FromDays(730));
        var wrapped = new CertificateRingXmlEncryptor(wrapping, NullLoggerFactory.Instance).Encrypt(Secret);
        using var ring = Ring([Pem(other)]);

        var decrypt = () => new CertificateRingXmlDecryptor(ring).Decrypt(wrapped.EncryptedElement);

        decrypt.Should().Throw<CryptographicException>();
    }

    public void Dispose() => _files.Dispose();

    private static string RingBundle(DateTimeOffset notBefore)
    {
        using var certificate = SigningCertificateGenerator.CreateDataProtection(notBefore, TimeSpan.FromDays(730));
        return SigningCertificateGenerator.ExportPem(certificate);
    }

    private static string Pem(X509Certificate2 certificate)
    {
        using var rsa = certificate.GetRSAPrivateKey()!;
        return certificate.ExportCertificatePem() + "\n" + rsa.ExportPkcs8PrivateKeyPem();
    }

    private static ServiceProvider Ring(List<string> certificates)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DataProtectionRingCertificates(new DataProtectionRingOptions { Certificates = certificates }, new FakeTimeProvider(Now)));
        return services.BuildServiceProvider();
    }

    private static ValidateOptionsResult Validate(DataProtectionRingOptions options, string environment, string? deployEnvironment)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment { EnvironmentName = environment });
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DEPLOY_ENVIRONMENT"] = deployEnvironment })
            .Build());
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        using var provider = services.BuildServiceProvider();
        return new DataProtectionRingOptionsValidator(provider).Validate(null, options);
    }
}
