using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Identity.Keys;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Server;

/// <summary>The service's <c>keys generate</c> command: one PEM bundle on standard output, a summary or the usage on standard error.</summary>
public sealed class SigningKeyCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Generate_ShouldPrintOneLoadableBundle_StagedAtTheGivenDate()
    {
        var (exit, output, error) = Run("generate", "--role", "signing", "--days", "90", "--not-before", "2026-11-01");

        exit.Should().Be(0);
        using var certificate = X509Certificate2.CreateFromPem(output, output);
        certificate.HasPrivateKey.Should().BeTrue();
        new DateTimeOffset(certificate.NotBefore).Should().Be(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        new DateTimeOffset(certificate.NotAfter).Should().Be(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero).AddDays(90));
        certificate.Extensions.OfType<X509KeyUsageExtension>().Single().KeyUsages.Should().Be(X509KeyUsageFlags.DigitalSignature);
        output.Should().StartWith("-----BEGIN CERTIFICATE-----").And.Contain("-----BEGIN PRIVATE KEY-----");
        error.Should().Contain("Generated a signing certificate").And.Contain(certificate.Thumbprint);
    }

    [Fact]
    public void Generate_ShouldStartNow_WithTheRoleDefaultLifetime()
    {
        var (exit, output, _) = Run("generate", "--role", "encryption");

        exit.Should().Be(0);
        using var certificate = X509Certificate2.CreateFromPem(output, output);
        new DateTimeOffset(certificate.NotBefore).Should().Be(Now);
        new DateTimeOffset(certificate.NotAfter).Should().Be(Now + SigningCertificateGenerator.DefaultEncryptionLifetime);
        certificate.Extensions.OfType<X509KeyUsageExtension>().Single().KeyUsages.Should().Be(X509KeyUsageFlags.KeyEncipherment);
    }

    [Theory]
    [InlineData]
    [InlineData("generate")]
    [InlineData("generate", "--role", "admin")]
    [InlineData("generate", "--role", "signing", "--days", "0")]
    [InlineData("generate", "--role", "signing", "--days")]
    [InlineData("generate", "--role", "signing", "--not-before", "soon")]
    [InlineData("rotate", "--role", "signing")]
    public void Mistakes_ShouldPrintTheUsage_AndNoKey(params string[] arguments)
    {
        var (exit, output, error) = Run(arguments);

        exit.Should().Be(2);
        output.Should().BeEmpty();
        error.Should().Contain(SigningKeyCommand.Usage);
    }

    private static (int Exit, string Output, string Error) Run(params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = SigningKeyCommand.Run(arguments, output, error, new FakeTimeProvider(Now));
        return (exit, output.ToString(), error.ToString());
    }
}
