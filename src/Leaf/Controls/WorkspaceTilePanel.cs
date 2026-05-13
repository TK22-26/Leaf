using System.Windows;
using System.Windows.Controls;

namespace Leaf.Controls;

/// <summary>
/// Layout panel for the workspace tile grid. Picks a rectangle for each
/// child based on the total tile count so the parent (always child 0)
/// gets a sensible placement and the body of the grid never has a
/// stranded empty cell where one of the cardinal layouts would land
/// off-balance.
/// </summary>
/// <remarks>
/// Cell shapes per count:
/// <list type="bullet">
///   <item><description>1 — fills the panel.</description></item>
///   <item><description>2 — equal left / right halves.</description></item>
///   <item><description>3 — parent fills the LEFT HALF full-height; the two
///     submodules stack on the RIGHT HALF. This avoids the dead-quarter
///     a 2×2 layout would leave when the third slot is unused.</description></item>
///   <item><description>4 — 2×2 grid, parent top-left.</description></item>
///   <item><description>5 - 6 — 2 rows × 3 columns.</description></item>
///   <item><description>7 - 8 — 2 rows × 4 columns.</description></item>
///   <item><description>9 + — 3 columns, rows grow as needed.</description></item>
/// </list>
/// The panel deliberately does NOT virtualise. Tile counts are bounded
/// by submodule count (rarely more than a couple of dozen) and the
/// tiles' internal canvases already handle the heavy lifting.
/// </remarks>
public class WorkspaceTilePanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        // Measure each child against the cell size it would receive at
        // the panel's full available bounds. We compute cell sizes the
        // same way arrange does so the children's measured DesiredSize
        // matches the rectangle they'll eventually occupy — avoids a
        // layout-thrash that some controls trigger when measure and
        // arrange disagree.
        var count = InternalChildren.Count;
        if (count == 0) return new Size(0, 0);

        for (var i = 0; i < count; i++)
        {
            var rect = CellRect(count, i, availableSize.Width, availableSize.Height);
            InternalChildren[i].Measure(new Size(rect.Width, rect.Height));
        }
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = InternalChildren.Count;
        for (var i = 0; i < count; i++)
        {
            var rect = CellRect(count, i, finalSize.Width, finalSize.Height);
            InternalChildren[i].Arrange(rect);
        }
        return finalSize;
    }

    /// <summary>
    /// Compute the rectangle for tile <paramref name="index"/> given
    /// <paramref name="count"/> total tiles. Pulled out so
    /// <see cref="MeasureOverride"/> and <see cref="ArrangeOverride"/>
    /// agree on geometry without duplicating the layout table.
    /// </summary>
    private static Rect CellRect(int count, int index, double width, double height)
    {
        if (count <= 0) return new Rect(0, 0, 0, 0);
        if (count == 1) return new Rect(0, 0, width, height);

        if (count == 2)
        {
            var w = width / 2;
            return index == 0 ? new Rect(0, 0, w, height) : new Rect(w, 0, w, height);
        }

        if (count == 3)
        {
            // Parent (index 0) takes the entire left half, full height.
            // The two submodule tiles share the right half: top-right
            // and bottom-right.
            var halfW = width / 2;
            return index switch
            {
                0 => new Rect(0, 0, halfW, height),
                1 => new Rect(halfW, 0, halfW, height / 2),
                _ => new Rect(halfW, height / 2, halfW, height / 2),
            };
        }

        // Regular row-major grids past 3. Pick a shape that minimises
        // wasted area for the count and prefers wider over taller.
        var (rows, cols) = PickGrid(count);
        var cellW = width / cols;
        var cellH = height / rows;
        var row = index / cols;
        var col = index % cols;
        return new Rect(col * cellW, row * cellH, cellW, cellH);
    }

    private static (int Rows, int Cols) PickGrid(int count)
    {
        if (count == 4) return (2, 2);
        if (count <= 6) return (2, 3);
        if (count <= 8) return (2, 4);
        // 9+ — 3 columns, rows grow. Past 12 tiles the user is on a
        // big monitor or scrolling vertically; either way we keep the
        // tile width legible by capping at 3 cols.
        return ((count + 2) / 3, 3);
    }
}
