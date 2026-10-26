using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Identity.Tests.E2E;

/// <summary>Host filtering as a deployed target sets it (<c>AllowedHosts</c> = the public host plus <c>localhost</c>): the dev check in <c>deploy.yml</c> reaches the service by its platform-network name, so each of its requests must carry an allowed Host.</summary>
[Collection(IdentityApiCollection.Name)]
public sealed partial class HostFilteringTests(IdentityApiFixture fixture)
{
    /// <summary>Holds the platform-network name a dev check addresses the service by, which no target allows.</summary>
    private const string ServiceHost = "identity-dev-identity";

    [Theory]
    [InlineData("localhost", "/health")]
    [InlineData("localhost", "/api/system/status")]
    [InlineData(IdentityApiFixture.PublicHost, "/health")]
    public async Task Get_ShouldReturn200_WhenTheHostIsAllowed(string host, string path)
    {
        using var response = await SendAsync(host, path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(ServiceHost + ":8080", "/health")]
    [InlineData(ServiceHost + ":8080", "/api/system/status")]
    public async Task Get_ShouldReturn400_WhenTheHostIsNotAllowed(string host, string path)
    {
        using var response = await SendAsync(host, path);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DevCheck_ShouldPass_WhenEachRequestCarriesTheHostItDeclares()
    {
        var requests = DeclaredDevCheckRequests();

        requests.Should().NotBeEmpty("the descriptor declares a dev check");
        foreach (var (host, path) in requests)
        {
            host.Should().NotBeNull($"the dev check's request to {path} must name an allowed Host, since its URL carries the service's platform-network name");
            using var response = await SendAsync(host!, path);
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"the dev check's request to {path} sends Host: {host}");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string host, string path)
    {
        using var client = fixture.Host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = host;

        return await client.SendAsync(request);
    }

    /// <summary>Reads each <c>curl</c> of the descriptor's <c>verify</c> command: the Host header it sends, if any, and the path it requests.</summary>
    private static List<(string? Host, string Path)> DeclaredDevCheckRequests()
    {
        var verify = string.Join(' ', File.ReadLines(DescriptorPath())
            .SkipWhile(line => !line.StartsWith("verify:", StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => line.Length == 0 || char.IsWhiteSpace(line[0]) || line[0] == '#')
            .Where(line => !line.TrimStart().StartsWith('#')));

        return [.. CurlPattern().Matches(verify).Select(curl =>
        {
            var host = HostHeaderPattern().Match(curl.Value);
            return (host.Success ? host.Groups["host"].Value : null, UrlPathPattern().Match(curl.Value).Groups["path"].Value);
        })];
    }

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

    [GeneratedRegex("""\bcurl\b[^&]*""")]
    private static partial Regex CurlPattern();

    [GeneratedRegex("""--header\s+"Host:\s*(?<host>[^"\s]+)""")]
    private static partial Regex HostHeaderPattern();

    [GeneratedRegex("""\$VERIFY_SERVICE_[A-Z0-9_]+(?<path>/[^"\s]*)""")]
    private static partial Regex UrlPathPattern();
}
