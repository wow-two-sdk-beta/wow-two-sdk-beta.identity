namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>
/// A person's mute of an app's notices (<c>identity_notice_mutes</c>), from the notice's mute button, or of every app's
/// (<see cref="AllClients"/>) from the bot's <c>/stop</c>. A bare <c>/start</c> lifts the person's mutes. Codes and the
/// bot's own security notices never read it.
/// </summary>
public sealed class IdentityNoticeMute
{
    /// <summary>The <see cref="ClientId"/> that mutes every app.</summary>
    public const string AllClients = "*";

    /// <summary>The person.</summary>
    public Guid UserId { get; set; }

    /// <summary>The muted app's client id, or <see cref="AllClients"/>.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>When the person muted it (UTC).</summary>
    public DateTimeOffset MutedAt { get; set; }
}
