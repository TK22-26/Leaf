#nullable enable
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Leaf.Controls.Merge;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Tests for <see cref="ImageConflictPane.ExtractBgra"/> — the pixel-adjust
/// helper that underpins the Difference-mode pixel-math kernel. Full-pane
/// rendering tests would need a visual tree; extracting the kernel lets the
/// size-matching math be verified in isolation.
/// </summary>
public class ImageConflictPaneTests
{
    private static BitmapSource MakeSolidBitmap(int w, int h, Color color)
    {
        var stride = w * 4;
        var buf = new byte[stride * h];
        for (int i = 0; i < buf.Length; i += 4)
        {
            buf[i] = color.B;
            buf[i + 1] = color.G;
            buf[i + 2] = color.R;
            buf[i + 3] = color.A;
        }
        var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, w, h), buf, stride, 0);
        wb.Freeze();
        return wb;
    }

    [Fact]
    public void ExtractBgra_SameSize_CopiesPixelsVerbatim()
    {
        var src = MakeSolidBitmap(4, 4, Color.FromArgb(0xFF, 0xAA, 0xBB, 0xCC));
        var bytes = ImageConflictPane.ExtractBgra(src, 4, 4);
        bytes.Should().HaveCount(64); // 4x4x4
        // Check a few pixels are BGRA-encoded as expected.
        for (int i = 0; i < bytes.Length; i += 4)
        {
            bytes[i].Should().Be(0xCC);     // B
            bytes[i + 1].Should().Be(0xBB); // G
            bytes[i + 2].Should().Be(0xAA); // R
            bytes[i + 3].Should().Be(0xFF); // A
        }
    }

    [Fact]
    public void ExtractBgra_SmallerSource_IsCentredInDestination()
    {
        // 2×2 source centred in a 4×4 destination lands at (1,1)..(2,2).
        // Corners should be zero (transparent padding), centre filled.
        var src = MakeSolidBitmap(2, 2, Color.FromArgb(0xFF, 0x00, 0x80, 0xFF));
        var bytes = ImageConflictPane.ExtractBgra(src, 4, 4);
        bytes.Should().HaveCount(64);

        // Corner (0,0) — padding
        bytes[0].Should().Be(0);
        bytes[1].Should().Be(0);
        bytes[2].Should().Be(0);
        bytes[3].Should().Be(0);

        // Centre (1,1)
        int center = (1 * 4 + 1) * 4;
        bytes[center].Should().Be(0xFF);     // B
        bytes[center + 1].Should().Be(0x80); // G
        bytes[center + 2].Should().Be(0x00); // R
        bytes[center + 3].Should().Be(0xFF); // A
    }

    [Fact]
    public void ExtractBgra_LargerSource_IsClippedToDestinationBounds()
    {
        // A 6×6 source into a 4×4 destination — negative offsets, src clips in.
        var src = MakeSolidBitmap(6, 6, Color.FromArgb(0xFF, 0x10, 0x20, 0x30));
        var bytes = ImageConflictPane.ExtractBgra(src, 4, 4);
        bytes.Should().HaveCount(64);
        // Every pixel in the 4×4 output should be the source colour (no padding
        // because the source covers the whole destination after centring).
        for (int i = 0; i < bytes.Length; i += 4)
        {
            bytes[i].Should().Be(0x30);     // B
            bytes[i + 1].Should().Be(0x20); // G
            bytes[i + 2].Should().Be(0x10); // R
            bytes[i + 3].Should().Be(0xFF); // A
        }
    }
}
