using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Web;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Tokens;

namespace Identity.Tests.E2E.Accounts;

/// <summary>
/// The key that signs the confirmation, reset and forced-change tokens: a setting (inline or a file path) that outlives a
/// restart; a throwaway key only when a local run asks for it, and no boot without one.
/// </summary>
public sealed class TokenKeyTests
{
    [Fact]
    public async Task Key_ShouldComeFromAFile_SoATokenSurvivesARestart()
    {
        var directory = Directory.CreateTempSubdirectory("token-key-");
        try
        {
            var path = Path.Combine(directory.FullName, "token.key");
            await File.WriteAllTextAsync(path, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + "\n");
            var settings = new Dictionary<string, string?> { ["Identity:Tokens:SigningKey"] = path };
            await using var first = new AccountHost(settings);
            using var client = first.CreateClient();
            var registered = await AccountHost.DataAsync(await client.PostAsJsonAsync("/api/account/register", new { email = "kit@example.test", password = AccountHost.StrongPassword }));
            var link = first.Mail.LinkOf("Confirm your email", "kit@example.test");
            var query = HttpUtility.ParseQueryString(link.Query);
            registered.GetProperty("userId").GetString().Should().Be(query["userId"]);

            // A second instance of the same service, sharing the database and the key file, accepts the first one's token.
            await using var second = new AccountHost(settings, first.ConnectionString);
            using var other = second.CreateClient();
            (await other.GetAsync("/api/account/confirm-email" + link.Query)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Key_ShouldTakeTheInlineBase64Form()
    {
        await using var host = new AccountHost(new Dictionary<string, string?> { ["Identity:Tokens:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });
        using var client = host.CreateClient();

        (await client.PostAsJsonAsync("/api/account/register", new { email = "ivy@example.test", password = AccountHost.StrongPassword })).StatusCode.Should().Be(HttpStatusCode.OK);

        host.Mail.Sent.Should().ContainSingle();
    }

    [Theory]
    [InlineData("base64url")]
    [InlineData("base64url-padded")]
    [InlineData("base64-unpadded")]
    public async Task Key_ShouldTakeBase64Url_AndUnpaddedBase64_AsADeployToolWritesThem(string encoding)
    {
        // 0xFB 0xFF bytes make the base64 alphabets differ (+ and / against - and _).
        var bytes = Enumerable.Repeat((byte)0xFB, 16).Concat(Enumerable.Repeat((byte)0xFF, 16)).ToArray();
        var standard = Convert.ToBase64String(bytes);
        var key = encoding switch
        {
            "base64url" => standard.TrimEnd('=').Replace('+', '-').Replace('/', '_'),
            "base64url-padded" => standard.Replace('+', '-').Replace('/', '_'),
            _ => standard.TrimEnd('='),
        };
        await using var first = new AccountHost(new Dictionary<string, string?> { ["Identity:Tokens:SigningKey"] = key });
        using var client = first.CreateClient();
        (await client.PostAsJsonAsync("/api/account/register", new { email = "ana@example.test", password = AccountHost.StrongPassword })).StatusCode.Should().Be(HttpStatusCode.OK);
        var link = first.Mail.LinkOf("Confirm your email", "ana@example.test");

        // A second instance holding the same key in standard base64 accepts the token the first one signed.
        await using var second = new AccountHost(new Dictionary<string, string?> { ["Identity:Tokens:SigningKey"] = standard }, first.ConnectionString);
        using var other = second.CreateClient();
        (await other.GetAsync("/api/account/confirm-email" + link.Query)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Lifetimes_ShouldComeFromTheSection_BesideTheKey()
    {
        await using var host = new AccountHost(new Dictionary<string, string?>
        {
            ["Identity:Tokens:DefaultLifetime"] = "2.00:00:00",
            ["Identity:Tokens:Lifetimes:PasswordReset"] = "00:30:00",
        });

        var options = host.Host.Services.GetRequiredService<UserTokenOptions>();

        options.DefaultLifetime.Should().Be(TimeSpan.FromDays(2));
        options.Lifetimes[UserTokenPurposeConstants.PasswordReset].Should().Be(TimeSpan.FromMinutes(30));
        options.Lifetimes[UserTokenPurposeConstants.EmailConfirmation].Should().Be(TimeSpan.FromDays(3), "a purpose the section does not name keeps its default");
    }

    [Fact]
    public void Boot_ShouldRefuseAMissingKey_WhenNoThrowawayKeyIsAskedFor()
    {
        // The settings record and the token options each refuse the start, so more than one exception is thrown.
        var thrown = Record.Exception(() => new AccountHost(new Dictionary<string, string?> { ["Identity:Tokens:IsKeyEphemeral"] = "false" }));

        thrown.Should().NotBeNull();
        thrown!.ToString().Should().Contain("Identity:Tokens:SigningKey is required");
    }
}
