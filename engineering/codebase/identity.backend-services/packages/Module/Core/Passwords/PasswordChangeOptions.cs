namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;

/// <summary>Holds how a password change is confirmed and what it does to the person's sessions.</summary>
/// <remarks>
/// Part of <see cref="PasswordOptions"/>, so it binds from <c>Identity:Password:Change</c>. The same setting governs the
/// change from the account page and the change a sign-in forces when a stored password breaks the current rules. A code
/// takes its characters and length from <c>Identity:Otp</c>.
/// </remarks>
public sealed record PasswordChangeOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = PasswordOptions.SectionName + ":Change";

    /// <summary>The placeholder a link template carries the one-time token in.</summary>
    public const string TokenPlaceholder = "{token}";

    /// <summary>Gets or sets how a change is confirmed. Default <see cref="PasswordChangeConfirmationType.None"/>.</summary>
    public PasswordChangeConfirmationType Confirmation { get; set; } = PasswordChangeConfirmationType.None;

    /// <summary>Gets or sets whether a change also ends the session it was made in; every other session ends either way. Default false.</summary>
    public bool IsSignInAgainRequired { get; set; }

    /// <summary>Gets or sets how long a code or link works, counted from sending. Default 15 minutes.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Gets or sets the address of the page that spends a link, with <c>{token}</c> where the token goes, such as <c>https://id.example/change-password#token={token}</c>.</summary>
    /// <remarks>Required for <see cref="PasswordChangeConfirmationType.Link"/>. The token belongs in the URL fragment, which no server or mail scanner sees; a host with a public origin fills a missing value with its own page.</remarks>
    public string? LinkTemplate { get; set; }

    /// <summary>Gets or sets the subject of the message that carries the link. Default <c>Change your password</c>.</summary>
    public string LinkSubject { get; set; } = "Change your password";

    /// <summary>Gets or sets the text of the message that carries the link; <c>{link}</c> is the link and <c>{minutes}</c> its lifetime in minutes.</summary>
    public string LinkBody { get; set; } = "Open this link to change your password: {link}\nIt works once and expires in {minutes} minutes. If you did not ask for it, ignore this message.";

    /// <summary>Whether a template can carry a token: it holds the placeholder.</summary>
    internal bool IsLinkTemplateValid => !string.IsNullOrWhiteSpace(LinkTemplate)
        && LinkTemplate.Contains(TokenPlaceholder, StringComparison.Ordinal);
}
