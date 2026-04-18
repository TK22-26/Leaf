#nullable enable
using FluentAssertions;
using Leaf.Controls.Merge;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Unit tests for <see cref="ConflictMinimap.PointerYToLine"/>. The pixel→line
/// math is the load-bearing part of the minimap — a click on a marker must
/// land on the line that marker represents, exactly, so that "click the red
/// bar at the top to jump to conflict #1" works.
/// </summary>
public class ConflictMinimapTests
{
    [Fact]
    public void PointerYToLine_ClickAtTop_ReturnsFirstLine()
    {
        // A tall minimap with 100 lines → each row is 4 px. Y=0 is line 1.
        ConflictMinimap.PointerYToLine(0, actualHeight: 400, lineCount: 100).Should().Be(1);
    }

    [Fact]
    public void PointerYToLine_ClickAtMiddle_ReturnsMiddleLine()
    {
        // 400 px / 100 lines = 4 px per line. Y=200 falls on line 51
        // (floor(200/4)+1 = 51).
        ConflictMinimap.PointerYToLine(200, actualHeight: 400, lineCount: 100).Should().Be(51);
    }

    [Fact]
    public void PointerYToLine_ClickPastBottom_ClampsToLastLine()
    {
        // Out-of-bounds clicks (possible during drag-scroll near the edge)
        // must not return a line > lineCount.
        ConflictMinimap.PointerYToLine(99999, actualHeight: 400, lineCount: 100).Should().Be(100);
    }

    [Fact]
    public void PointerYToLine_NegativeY_ClampsToFirstLine()
    {
        ConflictMinimap.PointerYToLine(-50, actualHeight: 400, lineCount: 100).Should().Be(1);
    }

    [Fact]
    public void PointerYToLine_DenseCase_RowHeightPinsTo1Px()
    {
        // 50 px / 100 lines → rowHeight would be 0.5, which would collapse
        // multiple lines into a single pixel. The pin to 1 px per row keeps
        // the jump math usable in the dense case — Y=10 addresses line 11.
        ConflictMinimap.PointerYToLine(10, actualHeight: 50, lineCount: 100).Should().Be(11);
    }

    [Fact]
    public void PointerYToLine_ZeroLineCount_ReturnsOne()
    {
        // Degenerate input: empty document. Don't throw; return 1.
        ConflictMinimap.PointerYToLine(50, actualHeight: 400, lineCount: 0).Should().Be(1);
    }

    [Fact]
    public void PointerYToLine_ZeroHeight_ReturnsOne()
    {
        // Control not laid out yet. Don't throw.
        ConflictMinimap.PointerYToLine(50, actualHeight: 0, lineCount: 100).Should().Be(1);
    }

    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(10, 10, 2)]
    [InlineData(20, 10, 3)]
    [InlineData(90, 10, 10)]
    public void PointerYToLine_SparseCase_HitsExactRow(double y, int lineCount, int expected)
    {
        // 10 lines mapped over 100 px → 10 px per row, each row addressable.
        ConflictMinimap.PointerYToLine(y, actualHeight: 100, lineCount: lineCount)
            .Should().Be(expected);
    }

    [Fact]
    public void PointerYToLine_BoundaryOnRowEdge_RoundsDown()
    {
        // floor(40/4) + 1 = 11 — the click at exactly 40 px in a 4px-row
        // layout is the top of the 11th row. Matches "click near the line
        // boundary addresses the line that starts there".
        ConflictMinimap.PointerYToLine(40, actualHeight: 400, lineCount: 100)
            .Should().Be(11);
    }
}
