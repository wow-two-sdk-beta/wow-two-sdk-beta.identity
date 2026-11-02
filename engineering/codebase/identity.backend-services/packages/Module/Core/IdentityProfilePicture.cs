namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>One person's profile picture (<c>identity_profile_pictures</c>): the processed square WebP, keyed by the person.</summary>
public sealed class IdentityProfilePicture
{
    /// <summary>The person, the primary key.</summary>
    public Guid UserId { get; set; }

    /// <summary>The picture's bytes.</summary>
    public byte[] Content { get; set; } = [];

    /// <summary>The picture's media type, <c>image/webp</c>.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>The hex digest prefix that changes with the content; links and ETags carry it.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>When the picture was kept (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
