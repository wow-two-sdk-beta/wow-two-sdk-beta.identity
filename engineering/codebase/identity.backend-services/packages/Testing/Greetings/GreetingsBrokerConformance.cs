using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Identity.Greetings;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Identity.Testing.Greetings;

/// <summary>The conformance suite of <see cref="IGreetingsBroker"/>: one set of rules every implementation passes.</summary>
/// <remarks>Derive a test class per implementation — the module, the client against a host, a fake — and supply the
/// broker and the clock it stamps greetings with.</remarks>
public abstract class GreetingsBrokerConformance
{
    /// <summary>Gets the implementation under test.</summary>
    protected abstract IGreetingsBroker Broker { get; }

    /// <summary>Gets the clock the implementation stamps greetings with.</summary>
    protected abstract TimeProvider Clock { get; }

    /// <summary>A name is greeted by name.</summary>
    [Fact]
    public async Task GreetAsync_ShouldGreetTheName_WhenANameIsGiven()
    {
        var greeting = Value(await Broker.GreetAsync("Identity"));

        greeting.Message.Should().Be("Hello, Identity!");
    }

    /// <summary>Whitespace around a name is ignored.</summary>
    [Fact]
    public async Task GreetAsync_ShouldTrimTheName_WhenItHasSurroundingWhitespace()
    {
        var greeting = Value(await Broker.GreetAsync("  Identity  "));

        greeting.Message.Should().Be("Hello, Identity!");
    }

    /// <summary>A blank name greets the world.</summary>
    /// <param name="name">A blank name.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GreetAsync_ShouldGreetTheWorld_WhenTheNameIsBlank(string? name)
    {
        var greeting = Value(await Broker.GreetAsync(name));

        greeting.Message.Should().Be("Hello, world!");
    }

    /// <summary>A greeting carries the time it was served.</summary>
    [Fact]
    public async Task GreetAsync_ShouldStampTheGreeting_WhenItIsServed()
    {
        var greeting = Value(await Broker.GreetAsync("Identity"));

        greeting.GreetedAtUtc.Should().BeCloseTo(Clock.GetUtcNow(), TimeSpan.FromMinutes(1));
    }

    /// <summary>A name over the limit fails validation.</summary>
    [Fact]
    public async Task GreetAsync_ShouldFailValidation_WhenTheNameIsTooLong()
    {
        var result = await Broker.GreetAsync(new string('a', GreetingConstants.MaxNameLength + 1));

        result.Should().BeOfType<Result<GreetingModel>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.Validation);
    }

    /// <summary>A name at the limit is accepted.</summary>
    [Fact]
    public async Task GreetAsync_ShouldAcceptTheName_WhenItIsExactlyAtTheLimit()
    {
        var result = await Broker.GreetAsync(new string('a', GreetingConstants.MaxNameLength));

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>Returns the success value, failing the test with the broker's own error otherwise.</summary>
    private static GreetingModel Value(Result<GreetingModel> result) =>
        result is Result<GreetingModel>.Success success
            ? success.Value
            : throw new Xunit.Sdk.XunitException(((Result<GreetingModel>.Failure)result).Error.Message);
}
