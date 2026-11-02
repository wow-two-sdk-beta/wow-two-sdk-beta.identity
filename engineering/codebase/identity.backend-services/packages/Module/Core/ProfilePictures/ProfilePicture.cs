namespace WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;

/// <summary>Represents a person's kept profile picture.</summary>
public sealed record ProfilePicture
{
    /// <summary>Gets the picture's bytes.</summary>
    public required byte[] Content { get; init; }

    /// <summary>Gets the picture's media type, <c>image/webp</c>.</summary>
    public required string ContentType { get; init; }

    /// <summary>Gets the version, which changes with the content; links and ETags carry it.</summary>
    public required string Version { get; init; }

    /// <summary>Gets when the picture was kept (UTC).</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
