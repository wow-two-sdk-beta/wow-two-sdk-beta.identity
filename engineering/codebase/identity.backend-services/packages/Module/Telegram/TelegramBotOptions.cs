namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Holds the instance's own Telegram bot: whether it runs, how it is reached, and its link and message wording.</summary>
/// <remarks>
/// Set in code with <c>.AddTelegramLinks(o => …)</c> or in the host section <c>Identity:Telegram</c>, which is applied last
/// and read live, except <see cref="UpdateMode"/>, which is read at start. On, the token and the user name are required, the
/// webhook secret too while <see cref="UpdateMode"/> is <see cref="TelegramUpdateMode.Webhook"/>, and a deployed boot also
/// needs <c>Identity:Otp:HashKey</c> and refuses <see cref="TelegramUpdateMode.Polling"/>; a missing or refused one stops the start.
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
    /// Gets or sets how the instance receives updates. <see cref="TelegramUpdateMode.Webhook"/> (default) serves the webhook,
    /// and a deployed boot registers it with Telegram; <see cref="TelegramUpdateMode.Polling"/> long-polls <c>getUpdates</c>
    /// and is allowed only on a local run. Read at start.
    /// </summary>
    public TelegramUpdateMode UpdateMode { get; set; } = TelegramUpdateMode.Webhook;

    /// <summary>
    /// Gets or sets the secret Telegram sends in <c>X-Telegram-Bot-Api-Secret-Token</c> with every webhook call, as set by
    /// <c>setWebhook</c>'s <c>secret_token</c>: 1–256 of <c>A-Z a-z 0-9 _ -</c>; a secret. Required in <see cref="TelegramUpdateMode.Webhook"/> mode.
    /// </summary>
    public string? WebhookSecret { get; set; }

    /// <summary>
    /// Gets or sets the public HTTPS address of the webhook that a deployed boot registers with Telegram. The service host
    /// fills it with <c>{origin of Identity:Server:Issuer}/api/telegram/webhook</c> unless a setting names it; with none, nothing is registered.
    /// </summary>
    public string? WebhookUrl { get; set; }

    /// <summary>Gets or sets how long a start link stays usable. Default 15 minutes.</summary>
    public TimeSpan LinkLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets or sets how long a notice (<see cref="ITelegramNoticeSender"/>) stays in its chat before the sweep deletes it.
    /// Default 1 day; positive and below 48 hours, the window in which Telegram lets the bot delete its own messages.
    /// </summary>
    public TimeSpan NoticeLifetime { get; set; } = TimeSpan.FromDays(1);

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
