using System.Globalization;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>A checked press of a notice's button, as <see cref="ITelegramCallbackHandler"/> receives it.</summary>
public sealed record TelegramCallback
{
    /// <summary>The button's action name.</summary>
    public required string Action { get; init; }

    /// <summary>The button's payload; null for none.</summary>
    public string? Payload { get; init; }

    /// <summary>The kind of the notice the button is on.</summary>
    public required string Kind { get; init; }

    /// <summary>The account the notice is about; null for a message recorded without one.</summary>
    public Guid? UserId { get; init; }

    /// <summary>The chat the notice is in.</summary>
    public required long ChatId { get; init; }

    /// <summary>Telegram's id of the notice in that chat.</summary>
    public required int MessageId { get; init; }

    /// <summary>The Telegram user who pressed it, the button's allowed one.</summary>
    public required long TelegramUserId { get; init; }

    /// <summary>The presser's culture, from their Telegram language.</summary>
    public required CultureInfo Culture { get; init; }
}
