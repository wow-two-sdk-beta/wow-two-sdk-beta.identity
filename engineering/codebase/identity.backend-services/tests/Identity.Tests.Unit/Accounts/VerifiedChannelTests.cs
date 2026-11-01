using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Emails;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

/// <summary>A confirmed email is a verified channel; an unconfirmed or missing one is not.</summary>
public sealed class VerifiedChannelTests
{
    [Fact]
    public async Task EmailSource_ShouldListAConfirmedEmailAsTheEmailChannel()
    {
        var channels = await new EmailVerifiedChannelSource().ListAsync(new IdentityUser { Email = "a@example.test", IsEmailConfirmed = true });

        channels.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Channel = OtpChannelNameConstants.Email, Address = "a@example.test" });
    }

    [Theory]
    [InlineData("a@example.test", false)]
    [InlineData(null, true)]
    [InlineData("  ", true)]
    public async Task EmailSource_ShouldListNothing_WhenTheEmailIsUnconfirmedOrMissing(string? email, bool isConfirmed)
    {
        var channels = await new EmailVerifiedChannelSource().ListAsync(new IdentityUser { Email = email, IsEmailConfirmed = isConfirmed });

        channels.Should().BeEmpty();
    }
}
