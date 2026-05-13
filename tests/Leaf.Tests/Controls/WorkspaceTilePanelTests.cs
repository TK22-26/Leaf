#nullable enable
using System.Reflection;
using System.Windows;
using FluentAssertions;
using Leaf.Controls;
using Xunit;

namespace Leaf.Tests.Controls;

/// <summary>
/// Geometry tests for <see cref="WorkspaceTilePanel"/>. We exercise the
/// private <c>CellRect</c> table via reflection so the assertions read
/// like a layout spec — adding a new tile count (or changing one) only
/// needs the rectangle inputs updated here.
/// </summary>
public class WorkspaceTilePanelTests
{
    private static Rect Cell(int count, int index, double width = 1000, double height = 600)
    {
        var method = typeof(WorkspaceTilePanel)
            .GetMethod("CellRect", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Rect)method.Invoke(null, new object[] { count, index, width, height })!;
    }

    [Fact]
    public void Single_FillsPanel()
    {
        var r = Cell(1, 0);
        r.Should().Be(new Rect(0, 0, 1000, 600));
    }

    [Fact]
    public void Two_SplitsHorizontally()
    {
        Cell(2, 0).Should().Be(new Rect(0, 0, 500, 600));
        Cell(2, 1).Should().Be(new Rect(500, 0, 500, 600));
    }

    [Fact]
    public void Three_ParentLeftFullHeight_TwoStackedRight()
    {
        // The special case — the parent occupies the entire left half,
        // and the two submodules stack on the right. Avoids the
        // dead-quarter a naive 2x2 layout would leave.
        Cell(3, 0).Should().Be(new Rect(0, 0, 500, 600));
        Cell(3, 1).Should().Be(new Rect(500, 0, 500, 300));
        Cell(3, 2).Should().Be(new Rect(500, 300, 500, 300));
    }

    [Fact]
    public void Four_TwoByTwo()
    {
        Cell(4, 0).Should().Be(new Rect(0, 0, 500, 300));
        Cell(4, 1).Should().Be(new Rect(500, 0, 500, 300));
        Cell(4, 2).Should().Be(new Rect(0, 300, 500, 300));
        Cell(4, 3).Should().Be(new Rect(500, 300, 500, 300));
    }

    [Fact]
    public void Six_TwoByThree_RowMajor()
    {
        Cell(6, 0).Should().Be(new Rect(0, 0, 1000.0 / 3, 300));
        Cell(6, 3).Should().Be(new Rect(0, 300, 1000.0 / 3, 300));
        Cell(6, 5).Should().Be(new Rect(2000.0 / 3, 300, 1000.0 / 3, 300));
    }

    [Fact]
    public void Nine_ThreeColumns_RowsGrow()
    {
        // 9 tiles → 3 rows × 3 columns. Cell 6 is row 2, col 0.
        var r = Cell(9, 6);
        r.X.Should().Be(0);
        r.Y.Should().BeApproximately(400, 0.01);
        r.Width.Should().BeApproximately(1000.0 / 3, 0.01);
        r.Height.Should().BeApproximately(200, 0.01);
    }
}
