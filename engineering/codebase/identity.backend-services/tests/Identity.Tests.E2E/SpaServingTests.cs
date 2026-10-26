using System.Net;
using AwesomeAssertions;

namespace Identity.Tests.E2E;

/// <summary>The single-host contract: the built Vue app (console and hosted pages) is served at <c>/</c>, its hosted <c>/greet</c> page and any unknown route falls back to it, and an unmatched <c>/api</c> path answers JSON, never the app shell.</summary>
[Collection(IdentityApiCollection.Name)]
public sealed class SpaServingTests(IdentityApiFixture fixture)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/greet")]
    [InlineData("/some/client/route")]
    public async Task Get_ShouldServeTheAppShell_WhenTheRouteIsNotAnApiRoute(string path)
    {
        using var client = fixture.Host.CreateClient();

        using var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync()).Should().Contain("<div id=\"root\">");
    }

    [Fact]
    public async Task Get_ShouldReturn404AsJson_WhenAnApiRouteDoesNotExist()
    {
        using var client = fixture.Host.CreateClient();

        using var response = await client.GetAsync("/api/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().NotBe("text/html");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("<div id=\"root\">");
    }
}
