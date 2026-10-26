using System.Net;
using System.Text;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Identity.Client.Greetings;
using WoW.Two.Sdk.Beta.Be.Identity.Greetings;

namespace Identity.Tests.Unit.Greetings;

/// <summary>The client's mapping of the service's answers onto the contract's results; the happy path is conformance's.</summary>
public sealed class ApiGreetingsBrokerTests
{
    [Fact]
    public async Task GreetAsync_ShouldEscapeTheName_WhenItHasReservedCharacters()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"data":{"message":"Hello, a&b!","greetedAtUtc":"2026-01-01T00:00:00+00:00"}}""");

        await Broker(handler).GreetAsync("a&b");

        handler.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/api/greetings?name=a%26b");
    }

    [Fact]
    public async Task GreetAsync_ShouldFailValidation_WhenTheServiceAnswers400()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, """{"title":"Validation","detail":"The name is too long."}""");

        var result = await Broker(handler).GreetAsync("x");

        Error(result).Should().Match<AppError>(error => error.Type == AppErrorType.Validation && error.Message == "The name is too long.");
    }

    [Fact]
    public async Task GreetAsync_ShouldFailAsUnavailable_WhenTheServiceAnswers503()
    {
        var result = await Broker(new StubHandler(HttpStatusCode.ServiceUnavailable, "Unavailable")).GreetAsync("x");

        Error(result).Type.Should().Be(AppErrorType.ExternalUnavailable);
    }

    [Fact]
    public async Task GreetAsync_ShouldFailSerialization_WhenTheBodyIsMalformed()
    {
        var result = await Broker(new StubHandler(HttpStatusCode.OK, "{not json")).GreetAsync("x");

        Error(result).Type.Should().Be(AppErrorType.SerializationFailed);
    }

    private static ApiGreetingsBroker Broker(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://identity.test/") });

    private static AppError Error(Result<GreetingModel> result) =>
        result.Should().BeOfType<Result<GreetingModel>.Failure>().Which.Error;

    /// <summary>Answers every request with one canned response and remembers the request.</summary>
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
