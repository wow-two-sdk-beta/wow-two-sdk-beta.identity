using System.Globalization;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Represents a start link a matching shared contact just completed: the number is now linked to the chat.</summary>
public sealed record TelegramLinkCompletion
{
    /// <summary>What the link was made for, one of <see cref="TelegramLinkPurposeConstants"/>.</summary>
    public required string Purpose { get; init; }

    /// <summary>The linked number in E.164.</summary>
    public required string PhoneNumber { get; init; }

    /// <summary>The private chat the bot now sends that number's codes to.</summary>
    public required long ChatId { get; init; }

    /// <summary>The person's Telegram language, as a culture, for the wording of what follows.</summary>
    public required CultureInfo Culture { get; init; }
}
