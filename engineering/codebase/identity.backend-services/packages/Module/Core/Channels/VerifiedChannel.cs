namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Channels;

/// <summary>One way to reach a person that they have proven they control: an OTP channel and the address behind it.</summary>
public sealed record VerifiedChannel
{
    /// <summary>The OTP channel name that delivers to it, one of <c>OtpChannelNameConstants</c> or a custom handler's name.</summary>
    public required string Channel { get; init; }

    /// <summary>The channel-specific recipient: an email address, a phone number or a Telegram chat id.</summary>
    public required string Address { get; init; }
}
