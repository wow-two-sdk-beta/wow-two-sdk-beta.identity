namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>
/// One app's request under one idempotency key (<c>identity_app_notices</c>): <see cref="PendingStatus"/> while it sends,
/// then <see cref="AppNoticeStatusConstants.Sent"/> (naming the ledger row) or <see cref="AppNoticeStatusConstants.Muted"/>.
/// The key's first outcome answers every repeat; a repeat with another body is refused. Only the body's digest is kept.
/// </summary>
public sealed class IdentityAppNotice
{
    /// <summary>The status of a request being sent now.</summary>
    public const string PendingStatus = "pending";

    /// <summary>Primary key, a Guid v7 made in code.</summary>
    public Guid Id { get; set; }

    /// <summary>The app that sent it: the client id of its token, or the in-process host's <see cref="AppNoticeOptions.LocalClientId"/>.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The app's key for the notice, unique per app.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>The hex SHA-256 of the notice's body as first received.</summary>
    public string RequestHash { get; set; } = string.Empty;

    /// <summary>The person it was for.</summary>
    public Guid UserId { get; set; }

    /// <summary><see cref="PendingStatus"/>, <see cref="AppNoticeStatusConstants.Sent"/> or <see cref="AppNoticeStatusConstants.Muted"/>.</summary>
    public string Status { get; set; } = PendingStatus;

    /// <summary>The sent notice's row in the bot's message ledger (<c>identity_telegram_messages</c>); null unless sent.</summary>
    public Guid? MessageRecordId { get; set; }

    /// <summary>When the request was claimed (UTC); the idempotency window counts from it.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
