using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenIddict.Server;
using WoW.Two.Sdk.Beta.Be.Identity.Keys;

namespace Identity.Tests.E2E.Oidc;

/// <summary>
/// The two T6 rotation spikes, against the real service on host-file certificates: a staged signing certificate is in
/// the JWKS before its date, and signs from its date on without a restart. A control run without
/// <see cref="ActiveKeySelectionHandler"/> shows what OpenIddict alone does: it keeps its boot-time pick until a restart.
/// </summary>
/// <remarks>The certificates are throwaway, made per test class in a temp folder.</remarks>
public sealed class OidcKeyRotationTests : IDisposable
{
    private const string Password = "e2e-correct-horse-battery";
    private static readonly TimeSpan StagedIn = TimeSpan.FromDays(1);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"identity-oidc-keys-{Guid.NewGuid():N}");
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
    private readonly string _current;
    private readonly string _staged;
    private readonly Dictionary<string, string?> _settings;

    public OidcKeyRotationTests()
    {
        Directory.CreateDirectory(_directory);
        _current = Write("signing-current.pem", SigningKeyRole.Signing, _now.AddDays(-1), TimeSpan.FromDays(30), out var currentPath);
        _staged = Write("signing-staged.pem", SigningKeyRole.Signing, _now + StagedIn, TimeSpan.FromDays(120), out var stagedPath);
        Write("encryption.pem", SigningKeyRole.Encryption, _now.AddDays(-1), TimeSpan.FromDays(60), out var encryptionPath);
        _settings = new Dictionary<string, string?>
        {
            ["Identity:Server:Keys:Ephemeral"] = "false",
            ["Identity:Server:Keys:Signing:0"] = currentPath,
            ["Identity:Server:Keys:Signing:1"] = stagedPath,
            ["Identity:Server:Keys:Encryption:0"] = encryptionPath,
        };
    }

    [Fact]
    public async Task Jwks_ShouldPublishAStagedCertificate_BeforeItsDate()
    {
        await using var server = new OidcHost(_settings);
        using var client = server.CreateClient();

        var keys = await client.GetKeysAsync();
        await client.RegisterAndSignInAsync($"jwks-{Guid.NewGuid():N}@example.test", Password);
        var tokens = await client.SignInToAppAsync();

        keys.Keys.Select(key => key.Kid).Should().BeEquivalentTo([_current, _staged], "relying parties cache the next key before it signs");
        KeyIdOf(tokens.IdentityToken).Should().Be(_current, "a staged certificate never signs early");
        KeyIdOf(tokens.AccessToken).Should().Be(_current);
    }

    [Fact]
    public async Task StagedCertificate_ShouldTakeOverSigningOnItsDate_WithoutARestart()
    {
        await using var server = new OidcHost(_settings);
        using var client = server.CreateClient();
        var email = $"rotate-{Guid.NewGuid():N}@example.test";
        await client.RegisterAndSignInAsync(email, Password);
        KeyIdOf((await client.SignInToAppAsync()).IdentityToken).Should().Be(_current);

        server.Host.Clock.Advance(StagedIn + TimeSpan.FromHours(1));
        await client.SignInAsync(email, Password);
        var tokens = await client.SignInToAppAsync();

        KeyIdOf(tokens.IdentityToken).Should().Be(_staged);
        KeyIdOf(tokens.AccessToken).Should().Be(_staged);
        (await client.ValidateAsync(tokens.AccessToken, OidcHost.Audience)).Issuer.Should().NotBeNullOrEmpty();
        (await client.GetKeysAsync()).Keys.Select(key => key.Kid).Should().Contain(_current, "the old key still verifies tokens it signed");
    }

    [Fact]
    public async Task OpenIddictAlone_ShouldKeepItsBootPick_UntilARestart()
    {
        await using (var server = new OidcHost(_settings, WithoutDateSelection))
        {
            using var client = server.CreateClient();
            var email = $"control-{Guid.NewGuid():N}@example.test";
            await client.RegisterAndSignInAsync(email, Password);

            server.Host.Clock.Advance(StagedIn + TimeSpan.FromHours(1));
            await client.SignInAsync(email, Password);

            KeyIdOf((await client.SignInToAppAsync()).IdentityToken).Should().Be(_current, "OpenIddict orders its keys once, at boot");
        }

        // A boot after the date orders the staged certificate first.
        await using var restarted = new OidcHost(_settings, WithoutDateSelection);
        restarted.Host.Clock.SetUtcNow(_now + StagedIn + TimeSpan.FromHours(1));
        using var after = restarted.CreateClient();
        await after.RegisterAndSignInAsync($"restart-{Guid.NewGuid():N}@example.test", Password);

        KeyIdOf((await after.SignInToAppAsync()).IdentityToken).Should().Be(_staged);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static void WithoutDateSelection(IServiceCollection services)
        => services.Configure<OpenIddictServerOptions>(options => options.Handlers.Remove(ActiveKeySelectionHandler.Descriptor));

    /// <summary>The <c>kid</c> a token was signed with: the signing certificate's thumbprint.</summary>
    private static string KeyIdOf(string token) => new JsonWebToken(token).Kid;

    /// <summary>Writes a throwaway certificate bundle; returns its thumbprint, the <c>kid</c> its tokens carry.</summary>
    private string Write(string name, SigningKeyRole role, DateTimeOffset notBefore, TimeSpan lifetime, out string path)
    {
        using var certificate = SigningCertificateGenerator.Create(role, notBefore, lifetime);
        path = Path.Combine(_directory, name);
        File.WriteAllText(path, SigningCertificateGenerator.ExportPem(certificate));
        return certificate.Thumbprint;
    }
}
