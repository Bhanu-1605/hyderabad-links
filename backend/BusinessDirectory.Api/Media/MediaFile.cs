using System.Buffers.Text;
using System.Security.Cryptography;

namespace BusinessDirectory.Api.Media;

/// <summary>What kind of media a file is, and therefore which allowance it counts against.</summary>
public enum MediaKind
{
    Logo,
    Photo,
    Video,
}

/// <summary>
/// The file types accepted, and the checks that decide whether an upload really is one of them.
///
/// The declared content type and the filename are both attacker-controlled, so neither is trusted:
/// the first bytes of the file decide. A .jpg that begins with "MZ" is a Windows executable, and
/// accepting it would mean serving an executable from the business's own gallery.
/// </summary>
public static class MediaFile
{
    /// <summary>Per-file ceilings. Generous for a phone photo, mean enough to stop an upload filling the disk.</summary>
    public const long MaxImageBytes = 8L * 1024 * 1024;

    /// <summary>Video has no transcoding yet (decision D12), so the original is what gets served.</summary>
    public const long MaxVideoBytes = 64L * 1024 * 1024;

    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string WebP = "image/webp";
    public const string Mp4 = "video/mp4";

    /// <summary>D12: MP4/H.264 only for video. Images are the three formats every phone and browser handles.</summary>
    public static readonly string[] ImageTypes = [Jpeg, Png, WebP];

    public static long MaxBytesFor(MediaKind kind) => kind == MediaKind.Video ? MaxVideoBytes : MaxImageBytes;

    public static string ExtensionFor(string contentType) => contentType switch
    {
        Jpeg => ".jpg",
        Png => ".png",
        WebP => ".webp",
        Mp4 => ".mp4",
        _ => ".bin",
    };

    /// <summary>
    /// Reads the real type from the file's first bytes, or null when it is not one we accept.
    /// Returns the content type to store, which is this and never what the caller claimed.
    /// </summary>
    public static string? SniffContentType(ReadOnlySpan<byte> head)
    {
        // JPEG: FF D8 FF
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
        {
            return Jpeg;
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (head.Length >= 8 &&
            head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47 &&
            head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A)
        {
            return Png;
        }

        // WebP: "RIFF" .... "WEBP"
        if (head.Length >= 12 &&
            head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F' &&
            head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P')
        {
            return WebP;
        }

        // MP4 and friends: a box length, then "ftyp" at offset 4.
        if (head.Length >= 12 && head[4] == 'f' && head[5] == 't' && head[6] == 'y' && head[7] == 'p')
        {
            return Mp4;
        }

        return null;
    }

    /// <summary>How many bytes <see cref="SniffContentType"/> needs to decide.</summary>
    public const int SniffBytes = 12;

    /// <summary>
    /// A storage name the caller cannot influence: the listing it belongs to, then random bytes.
    /// Nothing from the uploaded filename survives, so "../../etc/passwd.jpg" cannot become a path,
    /// and one upload can never overwrite another.
    /// </summary>
    public static string NewStorageKey(long listingId, MediaKind kind, string contentType)
    {
        var random = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        return $"listings/{listingId}/{kind.ToString().ToLowerInvariant()}/{random}{ExtensionFor(contentType)}";
    }

    /// <summary>
    /// Width and height from the file's own header, for the three image formats we accept. Null when
    /// they cannot be read, which is not an error: the dimensions are a nicety, not a gate.
    /// Written by hand rather than pulling in an imaging library for two integers.
    /// </summary>
    public static (int Width, int Height)? ReadImageSize(ReadOnlySpan<byte> bytes, string contentType)
    {
        try
        {
            return contentType switch
            {
                Png when bytes.Length >= 24 => (
                    System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4)),
                    System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4))),
                Jpeg => ReadJpegSize(bytes),
                WebP => ReadWebPSize(bytes),
                _ => null,
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;   // a truncated or malformed header: not fatal
        }
    }

    /// <summary>Walks the JPEG segments to the start-of-frame marker, which carries the dimensions.</summary>
    private static (int Width, int Height)? ReadJpegSize(ReadOnlySpan<byte> bytes)
    {
        var index = 2;   // skip SOI
        while (index + 9 < bytes.Length)
        {
            if (bytes[index] != 0xFF)
            {
                return null;
            }

            var marker = bytes[index + 1];
            var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index + 2, 2));

            // SOF0..SOF3 and SOF5..SOF15 carry height then width; SOF4 and SOF12 do not.
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                var height = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index + 5, 2));
                var width = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index + 7, 2));
                return (width, height);
            }

            index += 2 + length;
        }
        return null;
    }

    /// <summary>Only the simple lossy VP8 form is read; the lossless and extended forms return null.</summary>
    private static (int Width, int Height)? ReadWebPSize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 30 || bytes[12] != 'V' || bytes[13] != 'P' || bytes[14] != '8' || bytes[15] != ' ')
        {
            return null;
        }

        var width = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(26, 2)) & 0x3FFF;
        var height = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(28, 2)) & 0x3FFF;
        return (width, height);
    }
}
