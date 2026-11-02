namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>What a handled press leads to: the notice loses its buttons either way.</summary>
public sealed record TelegramCallbackResult
{
    /// <summary>Changes nothing but the buttons, which drop, and shows no toast.</summary>
    public static readonly TelegramCallbackResult Done = new();

    /// <summary>The notice's new text, such as "Trust revoked"; null keeps its text.</summary>
    public string? MessageText { get; init; }

    /// <summary>The short toast Telegram shows the presser (<c>answerCallbackQuery</c>); null shows none.</summary>
    public string? AnswerText { get; init; }
}
