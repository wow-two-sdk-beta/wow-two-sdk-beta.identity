namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>An <c>add-phone</c> start link, or why none was made.</summary>
public sealed record OwnPhoneLinkResult
{
    /// <summary>How it ended.</summary>
    public required OwnPhoneLinkStatus Status { get; init; }

    /// <summary>The <c>t.me</c> link to open (<see cref="OwnPhoneLinkStatus.Created"/>).</summary>
    public string? TelegramLinkUrl { get; init; }
}
