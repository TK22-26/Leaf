using System.Windows.Media;
using FluentAssertions;
using Leaf.Models;
using Xunit;

namespace Leaf.Tests.Models;

public class BranchColorPaletteTests
{
    [Theory]
    [InlineData("#FF0000", 0xFF, 0xFF, 0x00, 0x00)]
    [InlineData("#00FF00", 0xFF, 0x00, 0xFF, 0x00)]
    [InlineData("#0000FF", 0xFF, 0x00, 0x00, 0xFF)]
    [InlineData("#80FFFFFF", 0x80, 0xFF, 0xFF, 0xFF)]
    [InlineData("3478F6", 0xFF, 0x34, 0x78, 0xF6)]
    [InlineData("#3478f6", 0xFF, 0x34, 0x78, 0xF6)]
    public void TryParseColor_AcceptsValidHexes(string hex, byte a, byte r, byte g, byte b)
    {
        BranchColorPalette.TryParseColor(hex, out var color).Should().BeTrue();
        color.Should().Be(Color.FromArgb(a, r, g, b));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-hex")]
    [InlineData("#ZZZZZZ")]
    [InlineData("#12345")]    // wrong length
    [InlineData("#1234567")]  // wrong length
    [InlineData(null)]
    public void TryParseColor_RejectsInvalidInput(string? hex)
    {
        BranchColorPalette.TryParseColor(hex, out _).Should().BeFalse();
    }

    [Fact]
    public void FormatColor_OpaqueDropsAlpha()
    {
        var hex = BranchColorPalette.FormatColor(Color.FromArgb(0xFF, 0x12, 0x34, 0x56));
        hex.Should().Be("#123456");
    }

    [Fact]
    public void FormatColor_NonOpaqueKeepsAlpha()
    {
        var hex = BranchColorPalette.FormatColor(Color.FromArgb(0x80, 0x12, 0x34, 0x56));
        hex.Should().Be("#80123456");
    }

    [Fact]
    public void ParsedColors_SkipsInvalidEntriesButKeepsValidOnes()
    {
        var palette = new BranchColorPalette
        {
            Colors = ["#FF0000", "garbage", "#00FF00"],
        };
        var parsed = palette.ParsedColors();
        parsed.Should().HaveCount(2);
        parsed[0].Should().Be(Color.FromRgb(0xFF, 0, 0));
        parsed[1].Should().Be(Color.FromRgb(0, 0xFF, 0));
    }
}
