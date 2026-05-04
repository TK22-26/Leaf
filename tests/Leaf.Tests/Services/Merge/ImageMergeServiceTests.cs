#nullable enable
using System.Text;
using FluentAssertions;
using Leaf.Services.Merge;
using Xunit;

namespace Leaf.Tests.Services.Merge;

/// <summary>
/// Tests for <see cref="ImageMergeService.Classify"/>. The outer <see cref="ImageMergeService.Load"/>
/// shells out to <c>git show</c> so its tests belong in an integration suite with
/// a fixture repo; Classify is the pure-function core and covers format detection
/// and LFS-pointer detection against fabricated byte arrays.
/// </summary>
public class ImageMergeServiceTests
{
    [Fact]
    public void NullBytes_ProducesNone()
    {
        var result = ImageMergeService.Classify(null);
        result.Format.Should().Be(ImageFormat.None);
        result.Bytes.Should().BeNull();
        result.IsLfsPointer.Should().BeFalse();
    }

    [Fact]
    public void EmptyBytes_ProducesNone()
    {
        var result = ImageMergeService.Classify(Array.Empty<byte>());
        result.Format.Should().Be(ImageFormat.None);
    }

    [Fact]
    public void PngMagicBytes_ProducesPng()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };
        var result = ImageMergeService.Classify(bytes);
        result.Format.Should().Be(ImageFormat.Png);
        result.IsLfsPointer.Should().BeFalse();
    }

    [Fact]
    public void JpegMagicBytes_ProducesJpeg()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        var result = ImageMergeService.Classify(bytes);
        result.Format.Should().Be(ImageFormat.Jpeg);
    }

    [Fact]
    public void GifMagicBytes_Gif89_ProducesGif()
    {
        var bytes = Encoding.ASCII.GetBytes("GIF89a.....");
        var result = ImageMergeService.Classify(bytes);
        result.Format.Should().Be(ImageFormat.Gif);
    }

    [Fact]
    public void GifMagicBytes_Gif87_ProducesGif()
    {
        var bytes = Encoding.ASCII.GetBytes("GIF87a.....");
        var result = ImageMergeService.Classify(bytes);
        result.Format.Should().Be(ImageFormat.Gif);
    }

    [Fact]
    public void BmpMagicBytes_ProducesBmp()
    {
        var bytes = new byte[] { 0x42, 0x4D, 0x00, 0x00, 0x00, 0x00 };
        var result = ImageMergeService.Classify(bytes);
        result.Format.Should().Be(ImageFormat.Bmp);
    }

    [Fact]
    public void WebpMagicBytes_ProducesWebp()
    {
        // RIFF....WEBP
        var bytes = new byte[] {
            0x52, 0x49, 0x46, 0x46, // RIFF
            0x20, 0x00, 0x00, 0x00, // size
            0x57, 0x45, 0x42, 0x50, // WEBP
        };
        var result = ImageMergeService.Classify(bytes);
        result.Format.Should().Be(ImageFormat.Webp);
    }

    [Fact]
    public void UnknownBinary_ProducesUnknown()
    {
        // Plausible binary that doesn't match any known image header.
        var bytes = new byte[] { 0x7F, 0x45, 0x4C, 0x46 }; // ELF header, for instance
        var result = ImageMergeService.Classify(bytes);
        result.Format.Should().Be(ImageFormat.Unknown);
        result.IsLfsPointer.Should().BeFalse();
    }

    [Fact]
    public void LfsPointer_IsDetected()
    {
        // A realistic pointer file looks like:
        //   version https://git-lfs.github.com/spec/v1
        //   oid sha256:abcdef...
        //   size 12345
        var pointerText =
            "version https://git-lfs.github.com/spec/v1\n" +
            "oid sha256:1111111111111111111111111111111111111111111111111111111111111111\n" +
            "size 42\n";
        var bytes = Encoding.ASCII.GetBytes(pointerText);
        var result = ImageMergeService.Classify(bytes);
        result.IsLfsPointer.Should().BeTrue();
        result.Bytes.Should().BeSameAs(bytes);
    }

    [Fact]
    public void PngPayloadStartingWithLfsPrefix_WouldBeLfs()
    {
        // Edge case: if someone committed a weird file that happened to start
        // with the LFS prefix, we treat it as a pointer (the prefix is
        // distinctive enough that this is intentionally aggressive).
        var bytes = Encoding.ASCII.GetBytes(
            "version https://git-lfs.github.com/spec/v1\n" +
            "other content here");
        var result = ImageMergeService.Classify(bytes);
        result.IsLfsPointer.Should().BeTrue();
    }

    [Fact]
    public void ShortBytesThatLookLikePrefix_AreNotLfs()
    {
        // Short-byte guard: must have enough bytes for the whole prefix, not
        // just a partial match.
        var bytes = Encoding.ASCII.GetBytes("version ht");
        var result = ImageMergeService.Classify(bytes);
        result.IsLfsPointer.Should().BeFalse();
        result.Format.Should().Be(ImageFormat.Unknown);
    }
}
