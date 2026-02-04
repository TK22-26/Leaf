namespace Leaf.Controls.GitGraph.Services;

/// <summary>
/// Implementation of <see cref="IGitGraphLayoutService"/> for position calculations.
/// </summary>
public sealed class GitGraphLayoutService : IGitGraphLayoutService
{
    public double GetXForColumn(int column, double laneWidth, double labelAreaWidth)
    {
        // Shift graph right by label area width, then center of the lane
        return labelAreaWidth + (column + 0.5) * laneWidth;
    }

    public double GetYForRow(int row, double rowHeight)
    {
        // Simple calculation - expansion is rendered as overlay
        return row * rowHeight + rowHeight / 2;
    }

    public (int minIndex, int maxIndex) GetVisibleNodeRange(
        int nodeCount,
        double scrollOffset,
        double viewportHeight,
        double rowHeight,
        int rowOffset)
    {
        if (nodeCount == 0)
            return (0, -1);

        double viewportTop = scrollOffset;
        double viewportBottom = viewportTop + viewportHeight;

        // CRITICAL: Large padding ABOVE for children that draw to visible parents
        // A commit above viewport draws DOWN to its parent which may be visible
        // Merge commits can have parents many rows apart (long-running feature branches)
        double paddingAbove = rowHeight * 100;  // ~100 rows lookback for merge branches
        double paddingBelow = rowHeight * 5;    // Small padding below

        viewportTop = Math.Max(0, viewportTop - paddingAbove);
        viewportBottom += paddingBelow;

        // Convert viewport coordinates to node indices
        // Account for rowOffset (working changes row + stash rows)
        int minRow = Math.Max(0, (int)(viewportTop / rowHeight) - rowOffset);
        int maxRow = (int)(viewportBottom / rowHeight) - rowOffset;

        int minIndex = Math.Max(0, minRow);
        int maxIndex = Math.Min(nodeCount - 1, maxRow);

        return (minIndex, maxIndex);
    }
}
