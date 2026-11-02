namespace WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;

/// <summary>
/// Holds the rules an uploaded profile picture meets and the one picture the instance keeps from it: JPEG, PNG or WebP by
/// their leading bytes, never by name or declared type; within the upload and size bounds; re-encoded as one square WebP,
/// which drops every metadata block (EXIF, GPS) and whatever else the upload carried.
/// </summary>
/// <remarks>Set in code with <c>.AddProfilePictures(o => …)</c> or in the host section <c>Identity:ProfilePictures</c>, which is applied last and read live.</remarks>
public sealed record ProfilePictureOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:ProfilePictures";

    /// <summary>The media types a decoder exists for; <see cref="AcceptedTypes"/> names a subset.</summary>
    public static readonly IReadOnlyList<string> SupportedTypes = [ProfilePictureTypeConstants.Jpeg, ProfilePictureTypeConstants.Png, ProfilePictureTypeConstants.Webp];

    /// <summary>Gets the media types an upload may be, checked by its leading bytes. Default JPEG, PNG and WebP; GIF, SVG and the rest are never taken.</summary>
    public List<string> AcceptedTypes { get; } = [.. SupportedTypes];

    /// <summary>Gets or sets the largest upload, in bytes. Default 5 MiB.</summary>
    public int MaxUploadBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Gets or sets the shortest side a decoded upload may have, in pixels. Default 256.</summary>
    public int MinDimension { get; set; } = 256;

    /// <summary>Gets or sets the longest side a decoded upload may have, in pixels, read before decoding (a decompression-bomb guard). Default 4096.</summary>
    public int MaxDimension { get; set; } = 4096;

    /// <summary>Gets or sets the side of the square WebP the instance keeps, in pixels. Default 512.</summary>
    public int OutputSize { get; set; } = 512;

    /// <summary>Gets or sets the WebP quality of the kept picture, 1–100. Default 80.</summary>
    public int Quality { get; set; } = 80;

    /// <summary>
    /// Gets or sets the link the <c>picture</c> claim carries, with <c>{userId}</c> and <c>{version}</c> standing for the
    /// person and the picture's version, such as <c>https://id.example.com/api/account/pictures/{userId}?v={version}</c>;
    /// null issues no <c>picture</c> claim. The service sets it from <c>Identity:Server:Issuer</c>.
    /// </summary>
    public string? LinkTemplate { get; set; }

    /// <summary>The <see cref="LinkTemplate"/> filled for <paramref name="userId"/> at <paramref name="version"/>; null without a template.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="version">The picture's version.</param>
    public string? LinkOf(Guid userId, string version)
        => LinkTemplate?.Replace("{userId}", userId.ToString("D"), StringComparison.Ordinal).Replace("{version}", Uri.EscapeDataString(version), StringComparison.Ordinal);
}
