using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Identity.Api.Configurations;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration.ConfigCheck;
using WoW.Two.Sdk.Beta.Be.Identity.Keys;
using WoW.Two.Sdk.Beta.Be.Meta;

namespace Identity.Tests.E2E;

/// <summary>
/// The image's <c>config check</c> verb (<c>dotnet Identity.Api.dll config check</c>) over the services its <c>Program</c>
/// builds: one <c>config-result</c> line, exit <c>0</c> for the settings a target sets, <c>1</c> for a retired key or a bad
/// value, and every key <c>deploy.yml</c> declares bound to a settings record. The host never serves, and no database is read.
/// </summary>
public sealed partial class ConfigCheckVerbTests
{
    [Fact]
    public async Task Check_ShouldBeConsistent_ForTheSettingsADescriptorKeyListFills()
    {
        var run = await RunAsync(DeclaredSettings());

        run.Exit.Should().Be(0, run.Error);
        run.Result.Invalid.Should().BeEmpty();
        run.Result.Unknown.Should().BeEmpty();
        run.Result.Stranded.Should().BeEmpty();
        run.Result.Service.Should().Be("identity");
        run.Output.Trim().Split('\n').Should().ContainSingle().Which.Should().StartWith(ConfigCheckResult.ResultLinePrefix);
    }

    [Fact]
    public async Task Check_ShouldBeConsistent_ForADeployedTargetWithOnlyTheRequiredAndGeneratedKeys()
    {
        var all = DeclaredSettings();
        string[] kept =
        [
            "DatabaseSettings:ConnectionString", "Identity:Server:Issuer", "Identity:Server:Keys:IsEphemeral",
            "Identity:Server:Keys:Signing:0", "Identity:Server:Keys:Encryption:0", "Identity:DataProtection:Certificates:0",
            "Identity:Tokens:SigningKey", "Identity:Otp:HashKey", "Identity:Telegram:WebhookSecret",
        ];
        var settings = kept.ToDictionary(key => key, key => all[key]);
        settings["DEPLOY_ENVIRONMENT"] = "dev";

        var run = await RunAsync(settings);

        run.Exit.Should().Be(0, run.Output + run.Error);
        run.Result.Invalid.Should().BeEmpty();
        run.Result.Unknown.Should().BeEmpty();
    }

    [Fact]
    public void Samples_ShouldCoverEveryKeyTheDescriptorDeclares_SoANewKeyIsCheckedHere()
    {
        var declared = DeclaredKeys();

        declared.Should().NotBeEmpty();
        DeclaredSettings().Keys.Should().BeEquivalentTo(declared, "each key deploy.yml declares needs a sample value here, and each sample a declared key");
    }

    [Fact]
    public async Task Check_ShouldKnowEverySettingTheDeploymentDocOffers()
    {
        var settings = DeclaredSettings();
        foreach (var (key, value) in OfferedSettings())
            settings[key] = value;

        var run = await RunAsync(settings);

        run.Exit.Should().Be(0, run.Output + run.Error);
        run.Result.Unknown.Should().BeEmpty();
    }

    [Fact]
    public async Task Check_ShouldRefuseARetiredKey_AndNameItWithoutItsValue()
    {
        var settings = DeclaredSettings();
        settings["Identity:SignUp:Enabled"] = "true";

        var run = await RunAsync(settings);

        run.Exit.Should().Be(1);
        run.Result.Unknown.Should().Equal("Identity:SignUp:Enabled");
    }

    [Fact]
    public async Task Check_ShouldRefuseAShortTokenKey_InEitherEncoding()
    {
        foreach (var short31 in new[] { Convert.ToBase64String(RandomNumberGenerator.GetBytes(31)), Base64Url(RandomNumberGenerator.GetBytes(31)) })
        {
            var settings = DeclaredSettings();
            settings["Identity:Tokens:SigningKey"] = short31;

            var run = await RunAsync(settings);

            run.Exit.Should().Be(1);
            run.Result.Invalid.Should().ContainSingle().Which.Message.Should().Contain("SigningKey");
            run.Output.Should().NotContain(short31, "a finding never carries the key");
        }
    }

    [Fact]
    public async Task Check_ShouldRefuseAMissingTokenKey_NamingTheSetting()
    {
        var settings = DeclaredSettings();
        settings.Remove("Identity:Tokens:SigningKey");

        var run = await RunAsync(settings);

        run.Exit.Should().Be(1);
        run.Result.Invalid.Should().Contain(finding => finding.Setting == "Identity:Tokens:SigningKey" && finding.Code == "required", run.Output + run.Error);
    }

    [Fact]
    public async Task Check_ShouldRefuseAThrowawayTokenKey_OnADeployedRun()
    {
        var settings = DeclaredSettings();
        settings.Remove("Identity:Tokens:SigningKey");
        settings["Identity:Tokens:IsKeyEphemeral"] = "true";
        settings["DEPLOY_ENVIRONMENT"] = "dev";

        var run = await RunAsync(settings);

        run.Exit.Should().Be(1);
        run.Result.Invalid.Should().ContainSingle(finding => finding.Setting == "Identity:Tokens:IsKeyEphemeral" && finding.Code == "local-only", run.Output + run.Error);
    }

    [Fact]
    public async Task Check_ShouldRefuseAnUnknownKeyUnderAnyIdentitySection()
    {
        var settings = DeclaredSettings();
        settings["Identity:Telegram:BotTokn"] = "x";
        settings["Identity:Email:Smtp:Hots"] = "x";

        var run = await RunAsync(settings);

        run.Exit.Should().Be(1);
        run.Result.Unknown.Should().BeEquivalentTo("Identity:Telegram:BotTokn", "Identity:Email:Smtp:Hots");
    }

    [Fact]
    public async Task Check_ShouldRefuseTheRetiredKeys_OfTestModeClientLooksAndOldNames()
    {
        var settings = DeclaredSettings();
        foreach (var (key, value) in OfferedSettings())
            settings[key] = value;
        settings["Identity:TestMode:IsEnabled"] = "false";
        settings["Identity:Server:Clients:0:Theme"] = "platform";
        settings["Identity:Server:Clients:0:BackgroundUrl"] = "https://img.example.test/desk.jpg";
        settings["Identity:Server:Clients:0:PostLogoutRedirectUris:0"] = "https://desk.example.test/";
        settings["Identity:SignIn:IsEmailLoginAllowed"] = "true";

        var run = await RunAsync(settings);

        run.Exit.Should().Be(1);
        run.Result.Unknown.Should().BeEquivalentTo(
            "Identity:TestMode:IsEnabled",
            "Identity:Server:Clients:0:Theme",
            "Identity:Server:Clients:0:BackgroundUrl",
            "Identity:Server:Clients:0:PostLogoutRedirectUris:0",
            "Identity:SignIn:IsEmailLoginAllowed");
    }

    private static async Task<Run> RunAsync(Dictionary<string, string?> settings)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        // Program.cs's own path to the verb: the configured builder, built, never started. The run is local (Development
        // without DEPLOY_ENVIRONMENT), as a developer's and the compose check's are.
        var exit = await ServiceHost.RunAsync(
            ["config", "check"],
            builder =>
            {
                builder.Environment.EnvironmentName = Environments.Development;
                // Pinned beside the sample: the test host's Development file turns on the throwaway token key.
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Identity:Tokens:IsKeyEphemeral"] = "false" });
                builder.Configuration.AddInMemoryCollection(settings);
                builder.Configure();
            },
            _ => Task.CompletedTask,
            addVerbs: null,
            output,
            error,
            CancellationToken.None);

        var line = output.ToString().Trim().Split('\n').Last();
        var result = ConfigCheckResult.FromOutput(output.ToString()) ?? throw new InvalidOperationException($"No config-result line: {line}");
        return new Run(exit, result, output.ToString(), error.ToString());
    }

    /// <summary>One sample value for every key <c>deploy.yml</c> declares for the service; the secrets are throwaway.</summary>
    private static Dictionary<string, string?> DeclaredSettings()
    {
        var now = DateTimeOffset.UtcNow.AddDays(-1);
        using var signing = SigningCertificateGenerator.Create(SigningKeyRole.Signing, now, TimeSpan.FromDays(30));
        using var encryption = SigningCertificateGenerator.Create(SigningKeyRole.Encryption, now, TimeSpan.FromDays(30));
        using var ring = SigningCertificateGenerator.CreateDataProtection(now, TimeSpan.FromDays(730));
        return new Dictionary<string, string?>
        {
            ["DatabaseSettings:ConnectionString"] = "Host=database.invalid;Database=identity;Username=identity;Password=unused",
            ["Identity:Server:Issuer"] = "https://id.example.test/",
            ["Identity:Server:Keys:IsEphemeral"] = "false",
            ["Identity:SignUp:Mode"] = "Invite",
            ["Identity:Server:Keys:Signing:0"] = SigningCertificateGenerator.ExportPem(signing),
            ["Identity:Server:Keys:Encryption:0"] = SigningCertificateGenerator.ExportPem(encryption),
            ["Identity:DataProtection:Certificates:0"] = SigningCertificateGenerator.ExportPem(ring),
            // What Wheelhouse's generate: { random: 32 } writes: base64url, no padding.
            ["Identity:Tokens:SigningKey"] = Base64Url(RandomNumberGenerator.GetBytes(32)),
            ["Identity:Otp:HashKey"] = Base64Url(RandomNumberGenerator.GetBytes(32)),
            ["Identity:Email:From"] = "identity@example.test",
            ["Identity:Email:FromName"] = "Identity",
            ["Identity:Email:Smtp:Host"] = "smtp.example.test",
            ["Identity:Email:Smtp:Port"] = "587",
            ["Identity:Email:Smtp:SecureSocket"] = "StartTls",
            ["Identity:Email:Smtp:Username"] = "mailer",
            ["Identity:Email:Smtp:Password"] = "unused",
            ["Identity:Password:Change:Confirmation"] = "Link",
            ["Identity:Sessions:Expiration"] = "Sliding",
            ["Identity:Sessions:Lifetime"] = "12:00:00",
            ["Identity:Sessions:ExtendedLifetime"] = "30.00:00:00",
            ["Identity:Sessions:SlidingLifetime"] = "5.00:00:00",
            ["Identity:Invitations:Lifetime"] = "7.00:00:00",
            ["Identity:Telegram:IsEnabled"] = "true",
            ["Identity:Telegram:BotToken"] = "123456:unused",
            ["Identity:Telegram:BotUserName"] = "identity_example_bot",
            ["Identity:Telegram:WebhookSecret"] = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),
            ["Identity:Telegram:UpdateMode"] = "Webhook",
            ["Identity:Phone:AllowedCountries:0"] = "UZ",
            ["Identity:Pages:BrandName"] = "Example",
            ["Identity:Pages:Theme"] = "platform",
            ["Identity:Pages:BackgroundUrl"] = "https://img.example.test/background.jpg",
        };
    }

    /// <summary>One sample value for every other setting <c>deployment.md</c> lists, so a key that binds to no record is found here.</summary>
    private static Dictionary<string, string?> OfferedSettings() => new()
    {
        ["Identity:Server:Clients:0:ClientId"] = "desk",
        ["Identity:Server:Clients:0:ClientSecret"] = "unused-client-secret-0123456789",
        ["Identity:Server:Clients:0:RedirectUris:0"] = "https://desk.example.test/callback",
        ["Identity:Server:Clients:0:PostSignOutRedirectUris:0"] = "https://desk.example.test/",
        ["Identity:Server:Clients:0:Audience"] = "desk-api",
        ["Identity:Server:Clients:0:PermissionNamespace"] = "desk",
        ["Identity:Tokens:DefaultLifetime"] = "1.00:00:00",
        ["Identity:Tokens:Lifetimes:PasswordReset"] = "02:00:00",
        ["Identity:Server:SignInPath"] = "/sign-in",
        ["Identity:Server:AuthorizationCodeLifetime"] = "00:01:00",
        ["Identity:Server:AccessTokenLifetime"] = "00:10:00",
        ["Identity:Server:IdentityTokenLifetime"] = "00:05:00",
        ["Identity:Server:RefreshTokenLifetime"] = "12:00:00",
        ["Identity:Sessions:Lifetime"] = "12:00:00",
        ["Identity:Sessions:ActivityInterval"] = "00:05:00",
        ["Identity:SignInLog:RetentionDays"] = "180",
        ["Identity:SignInLog:CleanupInterval"] = "01:00:00",
        ["Identity:Password:MinLength"] = "12",
        ["Identity:Password:MaxLength"] = "100",
        ["Identity:Password:IsDigitRequired"] = "true",
        ["Identity:Password:IsLowercaseRequired"] = "true",
        ["Identity:Password:IsUppercaseRequired"] = "true",
        ["Identity:Password:IsNonAlphanumericRequired"] = "true",
        ["Identity:Password:RequiredUniqueChars"] = "4",
        ["Identity:Password:IsAccountIdentifierRejected"] = "true",
        ["Identity:Password:Change:IsSignInAgainRequired"] = "true",
        ["Identity:Password:Change:Lifetime"] = "00:15:00",
        ["Identity:Password:Change:LinkTemplate"] = "https://id.example.test/change-password#token={token}",
        ["Identity:Password:Change:LinkSubject"] = "Change your password",
        ["Identity:Password:Change:LinkBody"] = "Open {link} within {minutes} minutes.",
        ["Identity:SignIn:IsConfirmedEmailRequired"] = "true",
        ["Identity:SignIn:IsConfirmedPhoneNumberRequired"] = "false",
        ["Identity:SignIn:IsEmailIdentifierAllowed"] = "true",
        ["Identity:Lockout:MaxFailedAttempts"] = "5",
        ["Identity:Lockout:DefaultLockout"] = "00:05:00",
        ["Identity:Lockout:IsEnabledForNewUsers"] = "true",
        ["Identity:Telegram:WebhookUrl"] = "https://id.example.test/api/telegram/webhook",
    };

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Reads the keys of <c>services.identity.settings.keys</c> in <c>deploy.yml</c>.</summary>
    private static List<string> DeclaredKeys() =>
    [
        .. File.ReadLines(DescriptorPath())
            .SkipWhile(line => !line.StartsWith("      keys:", StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => line.StartsWith("        ", StringComparison.Ordinal) || line.TrimStart().StartsWith('#'))
            .Select(line => KeyPattern().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["key"].Value),
    ];

    private static string DescriptorPath()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var path = Path.Combine(folder.FullName, "engineering", "deployment", "deploy.yml");
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException("engineering/deployment/deploy.yml was not found above the test output.");
    }

    [GeneratedRegex(@"^ {8}(?<key>[A-Za-z][A-Za-z0-9]*(?::[A-Za-z0-9]+)+):")]
    private static partial Regex KeyPattern();

    /// <summary>One verb run: the exit, the parsed result line, and the two streams.</summary>
    private sealed record Run(int Exit, ConfigCheckResult Result, string Output, string Error);
}
