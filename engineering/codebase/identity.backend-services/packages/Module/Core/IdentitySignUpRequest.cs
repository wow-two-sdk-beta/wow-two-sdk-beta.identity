namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// One sign-up a browser started from an invitation (<c>identity_sign_up_requests</c>): the name and processed picture it
/// gave, waiting for the instance's bot to prove a phone through a shared contact. Only the browser holding the secret,
/// whose SHA-256 alone is stored, reads it, and it signs that browser in once.
/// </summary>
public sealed class IdentitySignUpRequest
{
    /// <summary>Primary key, a Guid v7 made in code.</summary>
    public Guid Id { get; set; }

    /// <summary>The invitation the sign-up redeems.</summary>
    public Guid InvitationId { get; set; }

    /// <summary>The name the person gave; null when none was asked or given.</summary>
    public string? DisplayName { get; set; }

    /// <summary>The processed square WebP the person gave; null when none was asked or given.</summary>
    public byte[]? Picture { get; set; }

    /// <summary>The hex SHA-256 of the secret the starting browser holds.</summary>
    public string BrowserSecretHash { get; set; } = string.Empty;

    /// <summary>The app the sign-up serves, for the sign-in log; null when unknown.</summary>
    public string? ClientId { get; set; }

    /// <summary>When the browser started it (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When it stops waiting for the bot (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>When the bot finished it, joined or refused; null while waiting.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>The person who joined; null while waiting or when refused.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Why the join was refused, an identity error code; null while waiting or when joined.</summary>
    public string? FailureCode { get; set; }

    /// <summary>When the starting browser was signed in from it; null until then.</summary>
    public DateTimeOffset? SignedInAt { get; set; }
}
