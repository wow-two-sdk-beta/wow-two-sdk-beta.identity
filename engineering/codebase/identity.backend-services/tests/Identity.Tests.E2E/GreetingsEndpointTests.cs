using System.Net;
using System.Text.Json;
using AwesomeAssertions;

namespace Identity.Tests.E2E;

/// <summary>The HTTP mirror of the contract on the wire: the <c>{ data }</c> envelope and a problem for a rejected name.</summary>
[Collection(IdentityApiCollection.Name)]
public sealed class GreetingsEndpointTests(IdentityApiFixture fixture)
{
    [Fact]
    public async Task Get_ShouldReturn200WithTheGreetingInTheEnvelope_WhenANameIsGiven()
    {
        using var client = fixture.Host.CreateClient();

        using var response = await client.GetAsync("/api/greetings?name=Identity");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var greeting = body.RootElement.GetProperty("data");
        greeting.GetProperty("message").GetString().Should().Be("Hello, Identity!");
        greeting.GetProperty("greetedAtUtc").GetDateTimeOffset().Should().Be(fixture.Host.Clock.GetUtcNow());
    }

    [Fact]
    public async Task Get_ShouldReturn400Problem_WhenTheNameIsTooLong()
    {
        using var client = fixture.Host.CreateClient();

        using var response = await client.GetAsync($"/api/greetings?name={new string('a', 81)}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }
}
