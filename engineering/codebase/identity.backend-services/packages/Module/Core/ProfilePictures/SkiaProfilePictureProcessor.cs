using SkiaSharp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;

/// <summary>
/// Turns an uploaded profile picture into the one square WebP the instance keeps, with SkiaSharp: the type by leading bytes,
/// the sides read from the codec before any pixel is decoded, then decode to sRGB, upright by the EXIF orientation, the
/// centre square scaled to <see cref="ProfilePictureOptions.OutputSize"/> and encoded as lossy WebP from pixels alone.
/// </summary>
/// <remarks>
/// Stateless and safe to share. The output carries no metadata block: it is encoded from a pixel buffer with no colour space,
/// so even no ICC profile is written. Bad input never throws; rules that cannot make a picture (an output side below 1) do.
/// </remarks>
public sealed class SkiaProfilePictureProcessor : IProfilePictureProcessor
{
    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> RiffSignature => "RIFF"u8;
    private static ReadOnlySpan<byte> WebpSignature => "WEBP"u8;

    /// <inheritdoc />
    public ProfilePictureResult Process(ReadOnlySpan<byte> upload, ProfilePictureOptions rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentOutOfRangeException.ThrowIfLessThan(rules.OutputSize, 1);

        if (upload.IsEmpty || upload.Length > rules.MaxUploadBytes)
            return ProfilePictureResult.Refused(ProfilePictureStatus.TooLarge);

        var sourceType = Sniff(upload);
        if (sourceType is null)
            return ProfilePictureResult.Refused(ProfilePictureStatus.UnsupportedType);
        if (!rules.AcceptedTypes.Contains(sourceType, StringComparer.OrdinalIgnoreCase))
            return ProfilePictureResult.Refused(ProfilePictureStatus.UnsupportedType, sourceType);

        using var data = SKData.CreateCopy(upload);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat != EncodedFormatOf(sourceType))
            return ProfilePictureResult.Refused(ProfilePictureStatus.Unreadable, sourceType);

        // The header's sides, before a single pixel is decoded: the decompression-bomb guard.
        var (width, height) = (codec.Info.Width, codec.Info.Height);
        if (width > rules.MaxDimension || height > rules.MaxDimension)
            return ProfilePictureResult.Refused(ProfilePictureStatus.TooBig, sourceType);
        if (width < rules.MinDimension || height < rules.MinDimension)
            return ProfilePictureResult.Refused(ProfilePictureStatus.TooSmall, sourceType);

        using var source = Decode(codec);
        var content = source is null ? null : Render(source, codec.EncodedOrigin, rules);
        return content is null
            ? ProfilePictureResult.Refused(ProfilePictureStatus.Unreadable, sourceType)
            : new ProfilePictureResult { Status = ProfilePictureStatus.Accepted, Content = content, SourceType = sourceType };
    }

    private static string? Sniff(ReadOnlySpan<byte> upload)
    {
        if (upload.StartsWith(JpegSignature))
            return ProfilePictureTypeConstants.Jpeg;
        if (upload.StartsWith(PngSignature))
            return ProfilePictureTypeConstants.Png;
        if (upload.Length >= 12 && upload.StartsWith(RiffSignature) && upload[8..12].SequenceEqual(WebpSignature))
            return ProfilePictureTypeConstants.Webp;
        return null;
    }

    private static SKEncodedImageFormat EncodedFormatOf(string sourceType) => sourceType switch
    {
        ProfilePictureTypeConstants.Jpeg => SKEncodedImageFormat.Jpeg,
        ProfilePictureTypeConstants.Png => SKEncodedImageFormat.Png,
        _ => SKEncodedImageFormat.Webp,
    };

    /// <summary>Decodes the stored (not yet oriented) pixels into sRGB; null when they do not decode in full.</summary>
    private static SKBitmap? Decode(SKCodec codec)
    {
        var alphaType = codec.Info.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Premul;

        // CreateSrgb returns Skia's shared instance, which ignores Dispose.
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKImageInfo.PlatformColorType, alphaType, SKColorSpace.CreateSrgb());
        var bitmap = new SKBitmap();
        if (bitmap.TryAllocPixels(info) && codec.GetPixels(info, bitmap.GetPixels()) == SKCodecResult.Success)
        {
            bitmap.SetImmutable();
            return bitmap;
        }

        bitmap.Dispose();
        return null;
    }

    /// <summary>Crops the centre square, turns it upright, scales it to the output side and encodes it as WebP.</summary>
    private static byte[]? Render(SKBitmap source, SKEncodedOrigin origin, ProfilePictureOptions rules)
    {
        // The centre square of the stored pixels is the centre square of the upright picture: orientation only turns or flips it.
        var side = Math.Min(source.Width, source.Height);
        var crop = SKRectI.Create((source.Width - side) / 2, (source.Height - side) / 2, side, side);

        using var pixels = source.PeekPixels();
        using var square = pixels?.ExtractSubset(crop);
        if (square is null)
            return null;

        // Shares the decoded pixels; drawing only this square keeps its neighbours out of the edge samples.
        using var image = SKImage.FromPixels(square);
        if (image is null)
            return null;

        // No colour space: the pixels are already sRGB, and the encoder then writes no ICC profile.
        var size = rules.OutputSize;
        using var output = new SKBitmap();
        if (!output.TryAllocPixels(new SKImageInfo(size, size, SKImageInfo.PlatformColorType, source.AlphaType)))
            return null;

        using (var canvas = new SKCanvas(output))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale((float)size / side);
            canvas.Concat(Upright(origin, side));
            canvas.DrawImage(image, 0, 0, Sampling(side, size));
        }

        using var outputPixels = output.PeekPixels();
        using var encoded = outputPixels?.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, Math.Clamp(rules.Quality, 1, 100)));
        return encoded?.ToArray();
    }

    /// <summary>Trilinear (mipmapped) when shrinking, so a large photo does not alias; Mitchell cubic when growing.</summary>
    private static SKSamplingOptions Sampling(int side, int size) => side > size
        ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
        : new SKSamplingOptions(SKCubicResampler.Mitchell);

    /// <summary>Maps a stored square of <paramref name="side"/> onto the upright square the EXIF orientation describes.</summary>
    private static SKMatrix Upright(SKEncodedOrigin origin, float side) => origin switch
    {
        SKEncodedOrigin.TopRight => Matrix(-1, 0, side, 0, 1, 0),        // mirrored
        SKEncodedOrigin.BottomRight => Matrix(-1, 0, side, 0, -1, side), // turned 180°
        SKEncodedOrigin.BottomLeft => Matrix(1, 0, 0, 0, -1, side),      // flipped
        SKEncodedOrigin.LeftTop => Matrix(0, 1, 0, 1, 0, 0),             // transposed
        SKEncodedOrigin.RightTop => Matrix(0, -1, side, 1, 0, 0),        // turned 90° clockwise
        SKEncodedOrigin.RightBottom => Matrix(0, -1, side, -1, 0, side), // transversed
        SKEncodedOrigin.LeftBottom => Matrix(0, 1, 0, -1, 0, side),      // turned 90° counter-clockwise
        _ => SKMatrix.Identity,
    };

    private static SKMatrix Matrix(float scaleX, float skewX, float transX, float skewY, float scaleY, float transY) =>
        new(scaleX, skewX, transX, skewY, scaleY, transY, 0, 0, 1);
}
