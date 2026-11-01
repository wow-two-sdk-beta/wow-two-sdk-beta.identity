namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TestMode;

/// <summary>Represents one message the instance sent while test mode was on, kept in memory instead of leaving it.</summary>
public sealed record TestMessage
{
    /// <summary>The OTP channel name it went through, <c>email</c> or <c>telegram</c>.</summary>
    public required string Channel { get; init; }

    /// <summary>The recipient as the channel addressed it: an email address, or a Telegram chat id.</summary>
    public required string To { get; init; }

    /// <summary>The subject line, for email.</summary>
    public string? Subject { get; init; }

    /// <summary>The message text, carrying the code or the link.</summary>
    public required string Text { get; init; }

    /// <summary>When it was sent.</summary>
    public required DateTimeOffset SentAt { get; init; }
}
