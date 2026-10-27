using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Identity.Keys;
using WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Server;

/// <summary>The settings key source (inline PEM bundles and file paths), the certificate check and the date-based selection.</summary>
public sealed class SigningKeyTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private readonly KeyFiles _files = new();

    [Fact]
    public void SettingsSource_ShouldLoadEachFileWithItsPrivateKey()
    {
        var options = Options(
            signing: [_files.Write("signing.pem", SigningKeyRole.Signing, Now.AddDays(-1)), _files.Write("staged.pem", SigningKeyRole.Signing, Now.AddDays(1))],
            encryption: [_files.Write("encryption.pem", SigningKeyRole.Encryption, Now.AddDays(-1))]);

        var keys = new SettingsSigningKeySource(options).Load();

        keys.Signing.Should().HaveCount(2).And.OnlyContain(certificate => certificate.HasPrivateKey);
        keys.Encryption.Should().ContainSingle().Which.HasPrivateKey.Should().BeTrue();
        SigningKeyValidator.Validate(keys, Now).Should().BeEmpty();
    }

    [Fact]
    public void SettingsSource_ShouldNameEveryMissingOrKeylessFile()
    {
        using var certificate = SigningCertificateGenerator.Create(SigningKeyRole.Signing, Now, TimeSpan.FromDays(30));
        var keyless = _files.WriteText("certificate-only.pem", certificate.ExportCertificatePem());
        var options = Options(signing: [keyless], encryption: [Path.Combine(_files.Directory, "absent.pem")]);

        var load = () => new SettingsSigningKeySource(options).Load();

        load.Should().Throw<InvalidOperationException>()
            .WithMessage("*Identity:Server:Keys:Signing:0 file*certificate-only.pem*no readable certificate with its private key*")
            .WithMessage("*Identity:Server:Keys:Encryption:0 file*absent.pem*does not exist*");
    }

    [Fact]
    public void SettingsSource_ShouldLoadInlineBundles_AsKeysGeneratePrintsThem()
    {
        var signing = KeyFiles.Bundle(SigningKeyRole.Signing, Now.AddDays(-1));
        var options = Options(
            signing: [signing, _files.Write("staged.pem", SigningKeyRole.Signing, Now.AddDays(1))],
            encryption: ["\n" + KeyFiles.Bundle(SigningKeyRole.Encryption, Now.AddDays(-1)).Replace("\n", "\r\n", StringComparison.Ordinal) + "\n"]);

        var keys = new SettingsSigningKeySource(options).Load();

        keys.Signing.Should().HaveCount(2).And.OnlyContain(certificate => certificate.HasPrivateKey);
        keys.Encryption.Should().ContainSingle().Which.HasPrivateKey.Should().BeTrue("CRLF line ends and surrounding blank lines still parse");
        SigningKeyValidator.Validate(keys, Now).Should().BeEmpty();
    }

    [Fact]
    public void SettingsSource_ShouldNeverPrintInlineKeyMaterial()
    {
        var bundle = KeyFiles.Bundle(SigningKeyRole.Signing, Now);
        var privateKey = bundle[bundle.IndexOf("-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal)..];
        var broken = bundle.Replace("-----BEGIN CERTIFICATE-----", "-----BEGIN CERTIFICATE-----\nAAAA", StringComparison.Ordinal);
        var bare = privateKey.Replace("-----BEGIN PRIVATE KEY-----", string.Empty, StringComparison.Ordinal).Replace("-----END PRIVATE KEY-----", string.Empty, StringComparison.Ordinal);
        var options = Options(signing: [broken, privateKey], encryption: [bare]);

        var load = () => new SettingsSigningKeySource(options).Load();

        var message = load.Should().Throw<InvalidOperationException>().Which.Message;
        message.Should().Contain("Identity:Server:Keys:Signing:0 (inline PEM) holds no readable certificate with its private key")
            .And.Contain("Identity:Server:Keys:Signing:1 (inline PEM) holds no readable certificate with its private key")
            .And.Contain("Identity:Server:Keys:Encryption:0 holds neither a PEM bundle nor a file path");
        foreach (var line in bundle.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(line => !line.StartsWith("-----", StringComparison.Ordinal)))
            message.Should().NotContain(line, "no line of key material reaches the message");
    }

    [Fact]
    public void SettingsSource_ShouldResolveRelativePathsAgainstTheContentRoot()
    {
        _files.Write("signing.pem", SigningKeyRole.Signing, Now);
        _files.Write("encryption.pem", SigningKeyRole.Encryption, Now);

        var keys = new SettingsSigningKeySource(Options(["signing.pem"], ["encryption.pem"]), new TestHostEnvironment { ContentRootPath = _files.Directory }).Load();

        keys.Signing.Should().ContainSingle();
        keys.Encryption.Should().ContainSingle();
    }

    [Fact]
    public void Validator_ShouldRefuseEmptyRolesAndCertificatesNotValidNow()
    {
        using var staged = SigningCertificateGenerator.Create(SigningKeyRole.Signing, Now.AddDays(1), TimeSpan.FromDays(30));
        using var expired = SigningCertificateGenerator.Create(SigningKeyRole.Signing, Now.AddDays(-40), TimeSpan.FromDays(30));

        var problems = SigningKeyValidator.Validate(new SigningKeySet([staged, expired], []), Now);

        problems.Should().HaveCount(2);
        problems.Should().Contain(problem => problem.StartsWith("No signing certificate is valid at", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("No encryption certificate is configured", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_ShouldRefuseSmallKeysWrongUsagesAndMissingPrivateKeys()
    {
        using var small = KeyFiles.Custom(1024, X509KeyUsageFlags.DigitalSignature, Now.AddDays(-1));
        using var wrongUsage = KeyFiles.Custom(2048, X509KeyUsageFlags.DigitalSignature, Now.AddDays(-1));
        using var withKey = SigningCertificateGenerator.Create(SigningKeyRole.Signing, Now.AddDays(-1), TimeSpan.FromDays(30));
        using var publicOnly = X509CertificateLoader.LoadCertificate(withKey.RawData);

        var problems = SigningKeyValidator.Validate(new SigningKeySet([small, publicOnly], [wrongUsage]), Now);

        problems.Should().Contain(problem => problem.Contains("1024-bit key", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.Contains("has no private key", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.Contains("lacks the KeyEncipherment key usage", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectActive_ShouldPickTheValidCertificateExpiringLast_AndNeverAStagedOne()
    {
        using var current = SigningCertificateGenerator.Create(SigningKeyRole.Signing, Now.AddDays(-10), TimeSpan.FromDays(30));
        using var older = SigningCertificateGenerator.Create(SigningKeyRole.Signing, Now.AddDays(-20), TimeSpan.FromDays(25));
        using var staged = SigningCertificateGenerator.Create(SigningKeyRole.Signing, Now.AddDays(1), TimeSpan.FromDays(120));
        X509Certificate2[] all = [older, staged, current];

        SigningKeySet.SelectActive(all, Now).Should().BeSameAs(current);
        SigningKeySet.SelectActive(all, Now.AddDays(2)).Should().BeSameAs(staged, "the staged certificate takes over once its date passes");
        SigningKeySet.SelectActive([staged], Now).Should().BeNull();
    }

    [Fact]
    public void Generator_ShouldMakeARingCertificate_ForKeyEncipherment()
    {
        using var ring = SigningCertificateGenerator.CreateDataProtection(Now, SigningCertificateGenerator.DefaultDataProtectionLifetime);

        Usage(ring).Should().Be(X509KeyUsageFlags.KeyEncipherment);
        ring.Subject.Should().Be("CN=Identity data protection");
        new DateTimeOffset(ring.NotAfter).Should().Be(Now.AddDays(730));
    }

    [Fact]
    public void Generator_ShouldGiveEachRoleItsKeyUsage_AndTheRequestedDates()
    {
        using var signing = SigningCertificateGenerator.Create(SigningKeyRole.Signing, Now, TimeSpan.FromDays(120));
        using var encryption = SigningCertificateGenerator.Create(SigningKeyRole.Encryption, Now, TimeSpan.FromDays(210));

        Usage(signing).Should().Be(X509KeyUsageFlags.DigitalSignature);
        Usage(encryption).Should().Be(X509KeyUsageFlags.KeyEncipherment);
        new DateTimeOffset(signing.NotBefore).Should().Be(Now);
        new DateTimeOffset(signing.NotAfter).Should().Be(Now.AddDays(120));
        signing.GetRSAPublicKey()!.KeySize.Should().Be(SigningCertificateGenerator.KeySize);
    }

    public void Dispose() => _files.Dispose();

    private static X509KeyUsageFlags Usage(X509Certificate2 certificate)
        => certificate.Extensions.OfType<X509KeyUsageExtension>().Single().KeyUsages;

    private static OidcServerOptions Options(List<string> signing, List<string> encryption)
        => new() { Keys = new ServerKeyOptions { Signing = signing, Encryption = encryption } };
}
