using System.Net;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Identity.Core;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests;

/// <summary>What the sessions and the sign-in log keep of a request: the network, a hint of the login, a bounded user agent.</summary>
public sealed class PersonalDataMaskTests
{
    [Theory]
    [InlineData("203.0.113.77", "203.0.113.0")]
    [InlineData("::ffff:198.51.100.9", "198.51.100.0")]
    [InlineData("2001:db8:85a3:8d3:1319:8a2e:370:7348", "2001:db8:85a3::")]
    public void Address_ShouldKeepOnlyItsNetwork(string address, string masked)
        => PersonalDataMasks.MaskAddress(IPAddress.Parse(address)).Should().Be(masked);

    [Theory]
    [InlineData("alice@example.test", "a***@example.test")]
    [InlineData("  Bob.Smith@Mail.Example  ", "B***@Mail.Example")]
    [InlineData("+998901234567", "+998*******67")]
    [InlineData("alice", "al***")]
    [InlineData("x", "x***")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Subject_ShouldKeepOnlyAHint(string? subject, string? masked)
        => PersonalDataMasks.MaskSubject(subject).Should().Be(masked);

    [Fact]
    public void UserAgent_ShouldBeCutTo256Characters()
    {
        PersonalDataMasks.TrimUserAgent(new string('a', 300)).Should().HaveLength(PersonalDataMasks.MaxUserAgentLength);
        PersonalDataMasks.TrimUserAgent("Mozilla/5.0").Should().Be("Mozilla/5.0");
        PersonalDataMasks.TrimUserAgent(string.Empty).Should().BeNull();
    }
}
