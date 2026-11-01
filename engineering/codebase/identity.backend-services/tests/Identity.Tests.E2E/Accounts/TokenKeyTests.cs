using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Web;
using AwesomeAssertions;

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

    [Fact]
    public void Boot_ShouldRefuseAMissingKey_WhenNoThrowawayKeyIsAskedFor()
    {
        var act = () => new AccountHost(new Dictionary<string, string?> { ["Identity:Tokens:IsKeyEphemeral"] = "false" });

        act.Should().Throw<Exception>().Which.ToString().Should().Contain("Identity:Tokens:SigningKey is required");
    }
}
