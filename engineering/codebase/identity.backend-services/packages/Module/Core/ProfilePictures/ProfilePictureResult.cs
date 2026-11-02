namespace WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;

/// <summary>Represents a processed profile picture upload: the kept square WebP, or why the upload was refused.</summary>
public sealed record ProfilePictureResult
{
    /// <summary>Gets the outcome.</summary>
    public required ProfilePictureStatus Status { get; init; }

    /// <summary>Gets the kept picture, a square WebP of <see cref="ProfilePictureOptions.OutputSize"/>; null when refused.</summary>
    public byte[]? Content { get; init; }

    /// <summary>Gets the type the upload's leading bytes named; null when none did.</summary>
    public string? SourceType { get; init; }

    /// <summary>Gets whether the upload was kept.</summary>
    public bool IsAccepted => Status == ProfilePictureStatus.Accepted;

    /// <summary>A refusal with <paramref name="status"/>.</summary>
    /// <param name="status">Why the upload was refused.</param>
    /// <param name="sourceType">The type the leading bytes named, when any.</param>
    public static ProfilePictureResult Refused(ProfilePictureStatus status, string? sourceType = null) => new() { Status = status, SourceType = sourceType };
}
