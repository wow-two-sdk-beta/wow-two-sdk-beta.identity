namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Holds the Bot API limits the message ledger, the sweep and the buttons keep to.</summary>
internal static class TelegramBotLimits
{
    /// <summary>A bot deletes its own private-chat messages only while they are younger than this; editing has no limit.</summary>
    public static readonly TimeSpan DeleteWindow = TimeSpan.FromHours(48);

    /// <summary>The most message ids one <c>deleteMessages</c> call takes, all from one chat.</summary>
    public const int MaxIdsPerDelete = 100;

    /// <summary>The most bytes a button's <c>callback_data</c> holds.</summary>
    public const int MaxCallbackDataBytes = 64;
}
