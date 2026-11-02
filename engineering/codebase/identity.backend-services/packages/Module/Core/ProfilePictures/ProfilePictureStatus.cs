namespace WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;

/// <summary>Refers to how processing an uploaded profile picture ended.</summary>
public enum ProfilePictureStatus
{
    /// <summary>The upload became the kept square WebP.</summary>
    Accepted,

    /// <summary>The upload is empty or above <see cref="ProfilePictureOptions.MaxUploadBytes"/>.</summary>
    TooLarge,

    /// <summary>The leading bytes name no accepted type (<see cref="ProfilePictureOptions.AcceptedTypes"/>), such as GIF, SVG or HEIC.</summary>
    UnsupportedType,

    /// <summary>A side is below <see cref="ProfilePictureOptions.MinDimension"/>.</summary>
    TooSmall,

    /// <summary>A side is above <see cref="ProfilePictureOptions.MaxDimension"/>, read before decoding.</summary>
    TooBig,

    /// <summary>The bytes start like an accepted type but do not decode.</summary>
    Unreadable,
}
