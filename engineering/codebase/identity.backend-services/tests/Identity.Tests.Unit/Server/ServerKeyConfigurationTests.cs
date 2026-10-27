using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using WoW.Two.Sdk.Beta.Be.Identity.Keys;
using WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Server;

/// <summary>The boot-time hand-over of keys to OpenIddict: fail closed on a deployed boot, ephemeral keys only on a local run.</summary>
public sealed class ServerKeyConfigurationTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private readonly KeyFiles _files = new();

    [Fact]
    public void Ephemeral_ShouldMakeThrowawayRsaKeys_OnALocalRun()
    {
        var server = Configure(new ServerKeyOptions { Ephemeral = true }, deployEnvironment: null);

        server.SigningCredentials.Should().ContainSingle().Which.Key.Should().BeOfType<RsaSecurityKey>().Which.KeyId.Should().NotBeNullOrEmpty();
        server.EncryptionCredentials.Should().ContainSingle().Which.Key.Should().BeOfType<RsaSecurityKey>();
    }

    [Theory]
    [InlineData("Development", "dev")]
    [InlineData("Production", null)]
    public void Ephemeral_ShouldRefuseADeployedBoot(string environment, string? deployEnvironment)
    {
        var configure = () => Configure(new ServerKeyOptions { Ephemeral = true }, deployEnvironment, environment);

        configure.Should().Throw<InvalidOperationException>().WithMessage("*Ephemeral is allowed only on a local run*");
    }

    [Fact]
    public void Ephemeral_ShouldRefuseCertificateFilesBesideIt()
    {
        var configure = () => Configure(new ServerKeyOptions { Ephemeral = true, Signing = [_files.Write("s.pem", SigningKeyRole.Signing, Now)] }, deployEnvironment: null);

        configure.Should().Throw<InvalidOperationException>().WithMessage("*choose one*");
    }

    [Fact]
    public void Files_ShouldBecomeX509Credentials_OnADeployedBoot()
    {
        var keys = new ServerKeyOptions
        {
            Signing = [_files.Write("s1.pem", SigningKeyRole.Signing, Now.AddDays(-1)), _files.Write("s2.pem", SigningKeyRole.Signing, Now.AddDays(1))],
            Encryption = [_files.Write("e1.pem", SigningKeyRole.Encryption, Now.AddDays(-1))],
        };

        var server = Configure(keys, deployEnvironment: "prod", Environments.Production);

        server.SigningCredentials.Should().HaveCount(2).And.OnlyContain(credentials => credentials.Key is X509SecurityKey && credentials.Algorithm == SecurityAlgorithms.RsaSha256);
        server.EncryptionCredentials.Should().ContainSingle().Which.Enc.Should().Be(SecurityAlgorithms.Aes256CbcHmacSha512);
    }

    [Fact]
    public void InlineBundles_ShouldBecomeX509Credentials_OnADeployedBoot()
    {
        var keys = new ServerKeyOptions
        {
            Signing = [KeyFiles.Bundle(SigningKeyRole.Signing, Now.AddDays(-1))],
            Encryption = [KeyFiles.Bundle(SigningKeyRole.Encryption, Now.AddDays(-1))],
        };

        var server = Configure(keys, deployEnvironment: "prod", Environments.Production);

        server.SigningCredentials.Should().ContainSingle().Which.Key.Should().BeOfType<X509SecurityKey>();
        server.EncryptionCredentials.Should().ContainSingle().Which.Key.Should().BeOfType<X509SecurityKey>();
    }

    [Fact]
    public void Files_ShouldFailClosed_WhenNothingIsValidNow()
    {
        var keys = new ServerKeyOptions
        {
            Signing = [_files.Write("staged.pem", SigningKeyRole.Signing, Now.AddDays(1))],
            Encryption = [_files.Write("e1.pem", SigningKeyRole.Encryption, Now.AddDays(-1))],
        };

        var configure = () => Configure(keys, deployEnvironment: "prod", Environments.Production);

        configure.Should().Throw<InvalidOperationException>().WithMessage("*No signing certificate is valid*");
    }

    [Fact]
    public void Files_ShouldFailClosed_WhenNoneIsConfigured()
    {
        var configure = () => Configure(new ServerKeyOptions(), deployEnvironment: "prod", Environments.Production);

        configure.Should().Throw<InvalidOperationException>().WithMessage("*No signing certificate is configured*No encryption certificate is configured*");
    }

    public void Dispose() => _files.Dispose();

    private static OpenIddictServerOptions Configure(ServerKeyOptions keys, string? deployEnvironment, string environment = "Development")
    {
        var services = new ServiceCollection();
        services.AddSingleton(new OidcServerOptions { Keys = keys });
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment { EnvironmentName = environment });
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DEPLOY_ENVIRONMENT"] = deployEnvironment })
            .Build());
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddSingleton<ISigningKeySource, SettingsSigningKeySource>();
        using var provider = services.BuildServiceProvider();

        var server = new OpenIddictServerOptions();
        new ServerKeyConfiguration(provider).Configure(server);
        return server;
    }
}
