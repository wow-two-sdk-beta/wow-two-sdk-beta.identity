using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Identity.Module.Greetings;

namespace Identity.Tests.Unit.Greetings;

public sealed class GreetingComposerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddHours(1);

    [Fact]
    public void Compose_ShouldGreetTheTrimmedName_WhenANameIsGiven()
    {
        var greeting = GreetingComposer.Compose("  Identity  ", Now);

        greeting.Message.Should().Be("Hello, Identity!");
        greeting.GreetedAtUtc.Should().Be(Now);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Compose_ShouldGreetTheWorld_WhenTheNameIsBlank(string? name)
    {
        GreetingComposer.Compose(name, Now).Message.Should().Be("Hello, world!");
    }
}
