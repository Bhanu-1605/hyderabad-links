using BusinessDirectory.Api.Media;

namespace BusinessDirectory.UnitTests;

// What a file really is decides whether it is accepted, so these tests are the ones standing between
// the gallery and someone uploading an executable named photo.jpg.
public class MediaFileTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];
    private static readonly byte[] Mp4 = [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m'];

    private static byte[] WebP()
    {
        var bytes = new byte[12];
        "RIFF"u8.CopyTo(bytes);
        "WEBP"u8.CopyTo(bytes.AsSpan(8));
        return bytes;
    }

    [Fact]
    public void The_four_accepted_formats_are_recognised()
    {
        Assert.Equal(MediaFile.Jpeg, MediaFile.SniffContentType(Jpeg));
        Assert.Equal(MediaFile.Png, MediaFile.SniffContentType(Png));
        Assert.Equal(MediaFile.WebP, MediaFile.SniffContentType(WebP()));
        Assert.Equal(MediaFile.Mp4, MediaFile.SniffContentType(Mp4));
    }

    [Theory]
    // A Windows executable, an ELF binary, a PDF, an SVG (which can carry script) and a zip.
    [InlineData("MZ\u0090\0\u0003\0\0\0\u0004\0\0\0")]
    [InlineData("\u007fELF\u0002\u0001\u0001\0\0\0\0\0")]
    [InlineData("%PDF-1.7\n%âãÏ")]
    [InlineData("<svg xmlns=\"htt")]
    [InlineData("PK\u0003\u0004\u0014\0\0\0\u0008\0\0\0")]
    public void Anything_else_is_refused_however_it_is_named(string head)
    {
        var bytes = head.Select(c => (byte)c).ToArray();

        Assert.Null(MediaFile.SniffContentType(bytes));
    }

    [Fact]
    public void A_truncated_file_is_refused_rather_than_guessed_at()
    {
        Assert.Null(MediaFile.SniffContentType([0xFF, 0xD8]));   // the start of a JPEG, but not enough of it
        Assert.Null(MediaFile.SniffContentType([]));
    }

    [Fact]
    public void A_jpeg_that_merely_starts_like_a_riff_file_is_not_called_webp()
    {
        var bytes = new byte[12];
        "RIFF"u8.CopyTo(bytes);
        "AVI "u8.CopyTo(bytes.AsSpan(8));   // a RIFF container, but not WebP

        Assert.Null(MediaFile.SniffContentType(bytes));
    }

    [Fact]
    public void Png_dimensions_come_from_the_file_header()
    {
        // A 4x7 PNG: the IHDR chunk carries the width then the height, big-endian, at offsets 16 and 20.
        var bytes = new byte[24];
        Png.AsSpan(0, 8).CopyTo(bytes);
        bytes[19] = 4;
        bytes[23] = 7;

        Assert.Equal((4, 7), MediaFile.ReadImageSize(bytes, MediaFile.Png));
    }

    [Fact]
    public void Jpeg_dimensions_are_found_by_walking_to_the_frame_marker()
    {
        // SOI, then an APP0 segment to skip over, then SOF0 with height 3 and width 2.
        byte[] bytes =
        [
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,
            0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x03, 0x00, 0x02, 0x03, 0x01, 0x11, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
            0xFF, 0xD9,
        ];

        Assert.Equal((2, 3), MediaFile.ReadImageSize(bytes, MediaFile.Jpeg));
    }

    [Fact]
    public void Unreadable_dimensions_are_not_an_error()
    {
        // Dimensions are a nicety: a file we cannot measure is still a valid upload.
        Assert.Null(MediaFile.ReadImageSize([0xFF, 0xD8, 0xFF], MediaFile.Jpeg));
        Assert.Null(MediaFile.ReadImageSize(Png.AsSpan(0, 8), MediaFile.Png));
        Assert.Null(MediaFile.ReadImageSize(Mp4, MediaFile.Mp4));
    }

    [Fact]
    public void A_storage_key_keeps_nothing_from_the_uploaded_filename()
    {
        var key = MediaFile.NewStorageKey(42, MediaKind.Photo, MediaFile.Png);

        Assert.StartsWith("listings/42/photo/", key);
        Assert.EndsWith(".png", key);
        // No path traversal is possible, because no part of the caller's input reaches the key.
        Assert.DoesNotContain("..", key);
        Assert.DoesNotContain("//", key);
    }

    [Fact]
    public void Two_uploads_never_get_the_same_storage_key()
    {
        var keys = Enumerable.Range(0, 500)
            .Select(_ => MediaFile.NewStorageKey(1, MediaKind.Photo, MediaFile.Jpeg))
            .ToHashSet();

        Assert.Equal(500, keys.Count);
    }

    [Fact]
    public void Video_is_allowed_to_be_larger_than_an_image()
    {
        Assert.Equal(MediaFile.MaxImageBytes, MediaFile.MaxBytesFor(MediaKind.Photo));
        Assert.Equal(MediaFile.MaxImageBytes, MediaFile.MaxBytesFor(MediaKind.Logo));
        Assert.True(MediaFile.MaxBytesFor(MediaKind.Video) > MediaFile.MaxBytesFor(MediaKind.Photo));
    }

    [Fact]
    public void The_extension_follows_the_verified_type_not_the_upload()
    {
        Assert.Equal(".jpg", MediaFile.ExtensionFor(MediaFile.Jpeg));
        Assert.Equal(".png", MediaFile.ExtensionFor(MediaFile.Png));
        Assert.Equal(".webp", MediaFile.ExtensionFor(MediaFile.WebP));
        Assert.Equal(".mp4", MediaFile.ExtensionFor(MediaFile.Mp4));
    }
}
