namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>Answers <c>POST manage/phone/telegram-link</c>: the bot's <c>add-phone</c> start link.</summary>
public sealed record OwnPhoneLinkDto
{
    /// <summary>The <c>t.me</c> link; the person's own shared contact in the bot sets the phone.</summary>
    public required string TelegramLinkUrl { get; init; }
}
