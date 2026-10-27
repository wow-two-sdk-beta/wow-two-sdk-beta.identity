using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.DataProtection;
using WoW.Two.Sdk.Beta.Be.Identity.Keys;

namespace Identity.Tests.E2E.Oidc;

/// <summary>
/// The service on keys as a vault secret carries them: each setting holds the PEM bundle <c>keys generate</c> prints, the
/// ring certificate included. Tokens sign with the inline certificate and the ring lands in the database wrapped.
/// </summary>
/// <remarks>The certificates are throwaway, made per test in memory.</remarks>
public sealed class OidcVaultKeyTests
{
    private const string Password = "e2e-correct-horse-battery";

    [Fact]
    public async Task InlineBundles_ShouldSignTokens_AndWrapTheRing()
    {
        var now = DateTimeOffset.UtcNow;
        using var signing = SigningCertificateGenerator.Create(SigningKeyRole.Signing, now.AddDays(-1), TimeSpan.FromDays(30));
        using var encryption = SigningCertificateGenerator.Create(SigningKeyRole.Encryption, now.AddDays(-1), TimeSpan.FromDays(30));
        using var ring = SigningCertificateGenerator.CreateDataProtection(now.AddDays(-1), TimeSpan.FromDays(730));
        await using var server = new OidcHost(new Dictionary<string, string?>
        {
            ["Identity:Server:Keys:Ephemeral"] = "false",
            ["Identity:Server:Keys:Signing:0"] = SigningCertificateGenerator.ExportPem(signing),
            ["Identity:Server:Keys:Encryption:0"] = SigningCertificateGenerator.ExportPem(encryption),
            ["Identity:DataProtection:Certificates:0"] = SigningCertificateGenerator.ExportPem(ring),
        });
        using var client = server.CreateClient();

        await client.RegisterAndSignInAsync($"vault-{Guid.NewGuid():N}@example.test", Password);
        var tokens = await client.SignInToAppAsync();

        new JsonWebToken(tokens.IdentityToken).Kid.Should().Be(signing.Thumbprint);
        (await client.ValidateAsync(tokens.AccessToken, OidcHost.Audience)).Subject.Should().NotBeNullOrEmpty();
        await using var scope = server.Host.Services.CreateAsyncScope();
        var keys = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().DataProtectionKeys.AsNoTracking().ToListAsync();
        keys.Should().NotBeEmpty().And.OnlyContain(key => key.Xml.Contains(nameof(CertificateRingXmlDecryptor)) && !key.Xml.Contains("<value>"));
    }
}
