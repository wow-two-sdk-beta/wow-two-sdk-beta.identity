namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Holds the instance's own Telegram bot: whether it runs, how it is reached, and its link and message wording.</summary>
/// <remarks>
/// Set in code with <c>.AddTelegramLinks(o => …)</c> or in the host section <c>Identity:Telegram</c>, which is applied last
/// and read live. On, the token, the user name and the webhook secret are required, and a deployed boot also needs
/// <c>Identity:Otp:HashKey</c>; a missing one stops the start.
/// </remarks>
public sealed record TelegramBotOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:Telegram";

    /// <summary>Gets or sets whether the bot runs: its webhook answers and phone numbers sign in through it. Default <c>false</c>.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Gets or sets the bot's API token, <c>{bot id}:{secret}</c>; a secret.</summary>
    public string? BotToken { get; set; }

    /// <summary>Gets or sets the bot's user name without <c>@</c>; start links read <c>https://t.me/{BotUserName}?start={token}</c>.</summary>
    public string? BotUserName { get; set; }

    /// <summary>
    /// Gets or sets the secret Telegram sends in <c>X-Telegram-Bot-Api-Secret-Token</c> with every webhook call, as set by
    /// <c>setWebhook</c>'s <c>secret_token</c>: 1–256 of <c>A-Z a-z 0-9 _ -</c>; a secret.
    /// </summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Gets or sets how long a start link stays usable. Default 15 minutes.</summary>
    public TimeSpan LinkLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets the bot's own wording by culture, then by key (<see cref="TelegramBotMessageKeyConstants"/>); configured texts
    /// win over the built-in <c>en</c>, <c>ru</c> and <c>uz</c> ones.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> Messages { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the culture used when the person's Telegram language has no text. Default <c>en</c>.</summary>
    public string DefaultCulture { get; set; } = "en";

    /// <summary>The bot's user name as links and rows store it: lower case, without <c>@</c>.</summary>
    internal string NormalizedBotUserName => (BotUserName ?? string.Empty).Trim().TrimStart('@').ToLowerInvariant();
}
