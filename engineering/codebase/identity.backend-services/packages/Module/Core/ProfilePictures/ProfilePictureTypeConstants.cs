namespace WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;

/// <summary>Names the media types a profile picture upload may be, and the one the instance keeps.</summary>
public static class ProfilePictureTypeConstants
{
    /// <summary>JPEG: starts <c>FF D8 FF</c>; what iPhone browsers send for a HEIC photo when the input accepts no HEIC.</summary>
    public const string Jpeg = "image/jpeg";

    /// <summary>PNG: starts <c>89 50 4E 47 0D 0A 1A 0A</c>.</summary>
    public const string Png = "image/png";

    /// <summary>WebP: <c>RIFF</c>, four size bytes, then <c>WEBP</c>; also the kept picture's type.</summary>
    public const string Webp = "image/webp";
}
