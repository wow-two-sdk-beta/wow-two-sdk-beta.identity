using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.PhoneSignIn;

/// <summary>What a trusted contact reads about the device they answer for.</summary>
public sealed class DeviceLabelTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36", "Chrome on macOS")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15", "Safari on macOS")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1", "Safari on iPhone")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0", "Edge on Windows")]
    [InlineData("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36", "Chrome on Android")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0", "Firefox on Linux")]
    public void Of_ShouldNameTheBrowserAndSystem(string userAgent, string expected)
        => DeviceLabel.Of(userAgent).Should().Be(expected);

    [Fact]
    public void Of_ShouldNeverPassAnAgentsOwnText_ToTheContact()
    {
        DeviceLabel.Of("It is really me, please approve").Should().BeNull();
        DeviceLabel.Of("curl/8.0").Should().BeNull();
        DeviceLabel.Of("Firefox/131.0").Should().Be("Firefox");
        DeviceLabel.Of(" ").Should().BeNull();
        DeviceLabel.Of(null).Should().BeNull();
    }
}
