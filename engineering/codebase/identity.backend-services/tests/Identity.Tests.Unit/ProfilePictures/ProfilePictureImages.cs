using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.ProfilePictures;

/// <summary>Builds the test uploads in code — encoded images, metadata segments, forged headers — and reads the WebP back.</summary>
internal static class ProfilePictureImages
{
    /// <summary>An image of <paramref name="width"/> × <paramref name="height"/> split into equal bands of <paramref name="bands"/>, left to right (or top to bottom when <paramref name="vertical"/>).</summary>
    public static byte[] Encode(SKEncodedImageFormat format, int width, int height, SKColor[]? bands = null, bool vertical = false)
    {
        bands ??= [SKColors.Orange, SKColors.Teal];
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint())
        {
            var length = vertical ? height : width;
            for (var i = 0; i < bands.Length; i++)
            {
                paint.Color = bands[i];
                var (from, to) = ((float)length * i / bands.Length, (float)length * (i + 1) / bands.Length);
                canvas.DrawRect(vertical ? new SKRect(0, from, width, to) : new SKRect(from, 0, to, height), paint);
            }
        }

        using var pixmap = bitmap.PeekPixels();
        using var data = pixmap.Encode(format, 90) ?? throw new InvalidOperationException($"SkiaSharp could not encode {format}.");
        return data.ToArray();
    }

    /// <summary>A JPEG with an EXIF segment (the orientation and a camera make) and an XMP segment, after its JFIF segment.</summary>
    public static byte[] WithExifAndXmp(byte[] jpeg, ushort orientation)
    {
        // TIFF, big-endian: header, IFD0 with Orientation (SHORT) and Make (ASCII, at offset 38), no next IFD.
        var make = "Acme Phone\0"u8.ToArray();
        var tiff = new List<byte>("MM"u8.ToArray()) { 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08, 0x00, 0x02 };
        tiff.AddRange([0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, (byte)(orientation >> 8), (byte)orientation, 0x00, 0x00]);
        tiff.AddRange([0x01, 0x0F, 0x00, 0x02, 0x00, 0x00, 0x00, (byte)make.Length, 0x00, 0x00, 0x00, 0x26]);
        tiff.AddRange([0x00, 0x00, 0x00, 0x00]);
        tiff.AddRange(make);

        var exif = Segment(0xE1, [.. "Exif\0\0"u8.ToArray(), .. tiff]);
        var xmp = Segment(0xE1, Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF/></x:xmpmeta>"));

        // After SOI and, when present, the JFIF APP0 segment.
        var at = jpeg[3] == 0xE0 ? 4 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(4)) : 2;
        return [.. jpeg[..at], .. exif, .. xmp, .. jpeg[at..]];
    }

    /// <summary>The JPEG with its frame header claiming <paramref name="width"/> × <paramref name="height"/>; the scan data stays as encoded.</summary>
    public static byte[] WithClaimedSize(byte[] jpeg, ushort width, ushort height)
    {
        var forged = jpeg.ToArray();
        for (var i = 2; i + 9 < forged.Length; i++)
        {
            if (forged[i] != 0xFF || forged[i + 1] is not (0xC0 or 0xC1 or 0xC2))
                continue;

            BinaryPrimitives.WriteUInt16BigEndian(forged.AsSpan(i + 5), height);
            BinaryPrimitives.WriteUInt16BigEndian(forged.AsSpan(i + 7), width);
            return forged;
        }

        throw new InvalidOperationException("No frame header found.");
    }

    /// <summary>The four-character codes of the WebP's RIFF chunks, in order.</summary>
    public static IReadOnlyList<string> WebpChunks(byte[] webp)
    {
        var chunks = new List<string>();
        for (var at = 12; at + 8 <= webp.Length;)
        {
            chunks.Add(Encoding.ASCII.GetString(webp, at, 4));
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(at + 4));
            at += 8 + size + (size & 1);
        }

        return chunks;
    }

    /// <summary>Whether <paramref name="color"/> is within 48 of <paramref name="expected"/> on each channel (lossy WebP).</summary>
    public static bool IsNear(SKColor color, SKColor expected) =>
        Math.Abs(color.Red - expected.Red) <= 48 && Math.Abs(color.Green - expected.Green) <= 48 && Math.Abs(color.Blue - expected.Blue) <= 48;

    private static byte[] Segment(byte marker, byte[] payload)
    {
        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(segment, 4);
        return segment;
    }
}
