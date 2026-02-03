namespace Leaf.Controls.GitGraph.Services;

/// <summary>
/// Service for pure math calculations for positions and measurements.
/// </summary>
public interface IGitGraphLayoutService
{
    /// <summary>
    /// Gets the X coordinate for a given column in the graph.
    /// </summary>
    double GetXForColumn(int column, double laneWidth, double labelAreaWidth);

    /// <summary>
    /// Gets the Y coordinate (center) for a given row.
    /// </summary>
    double GetYForRow(int row, double rowHeight);

    /// <summary>
    /// Calculates the visible node index range based on scroll position.
    /// </summary>
    /// <param name="nodeCount">Total number of nodes</param>
    /// <param name="scrollOffset">Current vertical scroll offset</param>
    /// <param name="viewportHeight">Height of the visible viewport</param>
    /// <param name="rowHeight">Height of each row</param>
    /// <param name="rowOffset">Offset for working changes and stash rows</param>
    /// <returns>Tuple of (minIndex, maxIndex) for visible nodes</returns>
    (int minIndex, int maxIndex) GetVisibleNodeRange(
        int nodeCount,
        double scrollOffset,
        double viewportHeight,
        double rowHeight,
        int rowOffset);
}
