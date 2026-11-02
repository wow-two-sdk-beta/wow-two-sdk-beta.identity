using System.Text;
using AwesomeAssertions;
using SkiaSharp;
using WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;
using static WoW.Two.Sdk.Beta.Be.Identity.Tests.ProfilePictures.ProfilePictureImages;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.ProfilePictures;

/// <summary>The SkiaSharp processor: the type by leading bytes, the size and side checks, the square WebP it keeps, and no metadata.</summary>
public sealed class SkiaProfilePictureProcessorTests
{
    private static readonly SkiaProfilePictureProcessor Processor = new();

    public static TheoryData<string, byte[]> UnsupportedUploads => new()
    {
        { "gif", [.. "GIF89a"u8.ToArray(), 0x00, 0x04, 0x00, 0x04, 0x80, 0x00, 0x00, .. new byte[64]] },
        { "svg", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"512\" height=\"512\"><script>alert(1)</script></svg>") },
        { "xml svg", Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\" width=\"512\" height=\"512\"/>") },
        { "heic", [0x00, 0x00, 0x00, 0x18, .. "ftypheic"u8.ToArray(), 0x00, 0x00, 0x00, 0x00, .. "mif1heic"u8.ToArray(), .. new byte[64]] },
        { "bmp", [.. "BM"u8.ToArray(), .. new byte[64]] },
        { "text", "just a profile picture, honest"u8.ToArray() },
        { "riff wave", [.. "RIFF"u8.ToArray(), 0x24, 0x00, 0x00, 0x00, .. "WAVEfmt "u8.ToArray(), .. new byte[32]] },
        { "short jpeg", [0xFF, 0xD8] },
    };

    public static TheoryData<string, byte[], string> UnreadableUploads => new()
    {
        { "jpeg signature + garbage", [0xFF, 0xD8, 0xFF, .. Garbage(4096)], ProfilePictureTypeConstants.Jpeg },
        { "png signature + garbage", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Garbage(4096)], ProfilePictureTypeConstants.Png },
        { "webp header + garbage", [.. "RIFF"u8.ToArray(), 0x00, 0x10, 0x00, 0x00, .. "WEBP"u8.ToArray(), .. Garbage(4096)], ProfilePictureTypeConstants.Webp },
        { "truncated jpeg", Truncated(Encode(SKEncodedImageFormat.Jpeg, 640, 480, Rainbow)), ProfilePictureTypeConstants.Jpeg },
    };

    private static SKColor[] Rainbow => [SKColors.Red, SKColors.Orange, SKColors.Yellow, SKColors.Green, SKColors.Cyan, SKColors.Blue, SKColors.Purple, SKColors.Magenta];

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg, 640, 480, 512, ProfilePictureTypeConstants.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png, 480, 640, 300, ProfilePictureTypeConstants.Png)]
    [InlineData(SKEncodedImageFormat.Webp, 640, 640, 128, ProfilePictureTypeConstants.Webp)]
    [InlineData(SKEncodedImageFormat.Jpeg, 300, 300, 512, ProfilePictureTypeConstants.Jpeg)]
    public void Process_ShouldKeepOneSquareWebpOfTheOutputSize(SKEncodedImageFormat format, int width, int height, int outputSize, string sourceType)
    {
        var result = Processor.Process(Encode(format, width, height), new ProfilePictureOptions { OutputSize = outputSize });

        result.Status.Should().Be(ProfilePictureStatus.Accepted);
        result.IsAccepted.Should().BeTrue();
        result.SourceType.Should().Be(sourceType);
        result.Content.Should().NotBeNull();
        result.Content![..4].Should().Equal("RIFF"u8.ToArray());
        result.Content[8..12].Should().Equal("WEBP"u8.ToArray());

        using var kept = SKBitmap.Decode(result.Content);
        kept.Width.Should().Be(outputSize);
        kept.Height.Should().Be(outputSize);
    }

    [Theory]
    [InlineData(900, 300, false)]
    [InlineData(300, 900, true)]
    public void Process_ShouldCropTheCentreSquare(int width, int height, bool vertical)
    {
        var upload = Encode(SKEncodedImageFormat.Png, width, height, [SKColors.Red, SKColors.Lime, SKColors.Blue], vertical);

        var result = Processor.Process(upload, new ProfilePictureOptions());

        result.Status.Should().Be(ProfilePictureStatus.Accepted);
        using var kept = SKBitmap.Decode(result.Content);
        kept.Width.Should().Be(512);
        kept.Height.Should().Be(512);
        new[] { (2, 2), (509, 2), (2, 509), (509, 509), (256, 256) }
            .Should().AllSatisfy(p => IsNear(kept.GetPixel(p.Item1, p.Item2), SKColors.Lime).Should().BeTrue($"({p.Item1}, {p.Item2}) lies in the centre band"));
    }

    [Theory]
    [MemberData(nameof(UnsupportedUploads))]
    public void Process_ShouldRefuseBytesThatNameNoAcceptedType(string kind, byte[] upload)
    {
        var result = Processor.Process(upload, new ProfilePictureOptions());

        result.Status.Should().Be(ProfilePictureStatus.UnsupportedType, kind);
        result.SourceType.Should().BeNull();
        result.Content.Should().BeNull();
    }

    [Fact]
    public void Process_ShouldRefuseASniffedTypeOutsideTheAcceptedTypes()
    {
        var rules = new ProfilePictureOptions();
        rules.AcceptedTypes.Remove(ProfilePictureTypeConstants.Png);

        var result = Processor.Process(Encode(SKEncodedImageFormat.Png, 600, 600), rules);

        result.Status.Should().Be(ProfilePictureStatus.UnsupportedType);
        result.SourceType.Should().Be(ProfilePictureTypeConstants.Png);
        Processor.Process(Encode(SKEncodedImageFormat.Jpeg, 600, 600), rules).Status.Should().Be(ProfilePictureStatus.Accepted);
    }

    [Fact]
    public void Process_ShouldRefuseEmptyAndOversizedUploads()
    {
        var jpeg = Encode(SKEncodedImageFormat.Jpeg, 600, 600);

        Processor.Process([], new ProfilePictureOptions()).Status.Should().Be(ProfilePictureStatus.TooLarge);
        Processor.Process(jpeg, new ProfilePictureOptions { MaxUploadBytes = jpeg.Length - 1 }).Status.Should().Be(ProfilePictureStatus.TooLarge);
        Processor.Process(jpeg, new ProfilePictureOptions { MaxUploadBytes = jpeg.Length }).Status.Should().Be(ProfilePictureStatus.Accepted);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(600, 200)]
    public void Process_ShouldRefuseASideBelowTheMinimum(int width, int height)
    {
        var result = Processor.Process(Encode(SKEncodedImageFormat.Png, width, height), new ProfilePictureOptions());

        result.Status.Should().Be(ProfilePictureStatus.TooSmall);
        result.SourceType.Should().Be(ProfilePictureTypeConstants.Png);
    }

    [Fact]
    public void Process_ShouldRefuseAHeaderClaimingSidesAboveTheMaximum_BeforeDecoding()
    {
        // A 300 × 300 JPEG whose frame header claims 5000 × 5000: only reading the header first sees TooBig, decoding fails.
        var forged = WithClaimedSize(Encode(SKEncodedImageFormat.Jpeg, 300, 300), 5000, 5000);

        var result = Processor.Process(forged, new ProfilePictureOptions());

        result.Status.Should().Be(ProfilePictureStatus.TooBig);
        result.SourceType.Should().Be(ProfilePictureTypeConstants.Jpeg);
    }

    [Fact]
    public void Process_ShouldRefuseARealSideAboveTheMaximum()
    {
        var result = Processor.Process(Encode(SKEncodedImageFormat.Png, 5000, 300), new ProfilePictureOptions());

        result.Status.Should().Be(ProfilePictureStatus.TooBig);
    }

    [Theory]
    [MemberData(nameof(UnreadableUploads))]
    public void Process_ShouldRefuseBytesThatStartLikeAnAcceptedTypeButDoNotDecode(string kind, byte[] upload, string sourceType)
    {
        var result = Processor.Process(upload, new ProfilePictureOptions());

        result.Status.Should().Be(ProfilePictureStatus.Unreadable, kind);
        result.SourceType.Should().Be(sourceType);
        result.Content.Should().BeNull();
    }

    // Stored 400 × 300, left half red and right half blue; its centre square is red | blue before the EXIF turn.
    // Quadrants read top-left, top-right, bottom-left, bottom-right.
    [Theory]
    [InlineData(1, "RBRB")]
    [InlineData(2, "BRBR")]
    [InlineData(3, "BRBR")]
    [InlineData(4, "RBRB")]
    [InlineData(5, "RRBB")]
    [InlineData(6, "RRBB")]
    [InlineData(7, "BBRR")]
    [InlineData(8, "BBRR")]
    public void Process_ShouldTurnThePictureUprightByItsExifOrientation(ushort orientation, string quadrants)
    {
        var upload = WithExifAndXmp(Encode(SKEncodedImageFormat.Jpeg, 400, 300, [SKColors.Red, SKColors.Blue]), orientation);

        var result = Processor.Process(upload, new ProfilePictureOptions());

        result.Status.Should().Be(ProfilePictureStatus.Accepted);
        using var kept = SKBitmap.Decode(result.Content);
        var seen = new string([.. new[] { (128, 128), (384, 128), (128, 384), (384, 384) }.Select(p => Quadrant(kept.GetPixel(p.Item1, p.Item2)))]);
        seen.Should().Be(quadrants);
    }

    [Fact]
    public void Process_ShouldKeepNoMetadata()
    {
        var upload = WithExifAndXmp(Encode(SKEncodedImageFormat.Jpeg, 640, 480), orientation: 6);
        using (var data = SKData.CreateCopy(upload))
        using (var codec = SKCodec.Create(data))
            codec.EncodedOrigin.Should().Be(SKEncodedOrigin.RightTop, "the upload carries the EXIF segment");

        var result = Processor.Process(upload, new ProfilePictureOptions());

        result.Status.Should().Be(ProfilePictureStatus.Accepted);
        var chunks = WebpChunks(result.Content!);
        chunks.Should().NotBeEmpty();
        chunks.Should().NotContain(["EXIF", "XMP ", "ICCP"]);
        chunks.Should().BeSubsetOf(["VP8 ", "VP8L", "VP8X", "ALPH"]);
        Encoding.ASCII.GetString(result.Content!).Should().NotContain("Acme Phone").And.NotContain("adobe:ns:meta");
    }

    private static char Quadrant(SKColor color) => IsNear(color, SKColors.Red) ? 'R' : IsNear(color, SKColors.Blue) ? 'B' : '?';

    private static byte[] Garbage(int length)
    {
        var bytes = new byte[length];
        new Random(42).NextBytes(bytes);
        return bytes;
    }

    private static byte[] Truncated(byte[] encoded) => encoded[..(encoded.Length * 2 / 3)];
}
