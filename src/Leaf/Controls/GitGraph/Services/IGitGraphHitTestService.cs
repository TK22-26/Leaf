using System.Windows;
using Leaf.Models;

namespace Leaf.Controls.GitGraph.Services;

/// <summary>
/// Service for hit testing logic for labels, overflow indicators, and expanded dropdowns.
/// Hit areas are mutable state with a defined lifecycle:
/// 1. ClearHitAreas() - called at start of OnRender
/// 2. RegisterOverflowHitArea() / RegisterExpandedItemHitArea() - called during render
/// 3. GetExpandedItemAt() / GetOverflowRowAt() - called from mouse handlers
/// </summary>
public interface IGitGraphHitTestService
{
    /// <summary>
    /// Registers an overflow hit area for a display row.
    /// </summary>
    void RegisterOverflowHitArea(int displayRow, List<BranchLabel> labels, Rect hitArea);

    /// <summary>
    /// Registers a tag overflow hit area for a display row.
    /// </summary>
    void RegisterTagOverflowHitArea(int displayRow, List<string> tags, Rect hitArea, double startX);

    /// <summary>
    /// Registers expanded item hit areas for a node.
    /// </summary>
    void RegisterExpandedItemHitArea(int nodeIndex, List<(BranchLabel Label, Rect HitArea)> items);

    /// <summary>
    /// Registers expanded tag hit areas for a node.
    /// </summary>
    void RegisterExpandedTagHitArea(int nodeIndex, List<Rect> hitAreas);

    /// <summary>
    /// Gets the expanded item at the given position, if any.
    /// </summary>
    (int NodeIndex, int BranchIndex)? GetExpandedItemAt(Point position);

    /// <summary>
    /// Gets the overflow row at the given position, if any.
    /// </summary>
    int? GetOverflowRowAt(Point position);

    /// <summary>
    /// Gets the tag overflow row at the given position, if any.
    /// </summary>
    int? GetTagOverflowRowAt(Point position);

    /// <summary>
    /// Checks if the position is inside any expanded area (branch or tag).
    /// </summary>
    bool IsInsideExpandedArea(Point position);

    /// <summary>
    /// Gets the overflow data for a display row.
    /// </summary>
    (List<BranchLabel> Labels, Rect HitArea)? GetOverflowByRow(int displayRow);

    /// <summary>
    /// Gets the tag overflow data for a display row.
    /// </summary>
    (List<string> Tags, Rect HitArea, double StartX)? GetTagOverflowByRow(int displayRow);

    /// <summary>
    /// Gets all overflow areas for iteration.
    /// </summary>
    IReadOnlyDictionary<int, (List<BranchLabel> Labels, Rect HitArea)> GetAllOverflowAreas();

    /// <summary>
    /// Gets all tag overflow areas for iteration.
    /// </summary>
    IReadOnlyDictionary<int, (List<string> Tags, Rect HitArea, double StartX)> GetAllTagOverflowAreas();

    /// <summary>
    /// Gets all expanded item hit areas for a node.
    /// </summary>
    List<(BranchLabel Label, Rect HitArea)>? GetExpandedItemHitAreas(int nodeIndex);

    /// <summary>
    /// Gets all expanded tag hit areas for a node.
    /// </summary>
    List<Rect>? GetExpandedTagHitAreas(int nodeIndex);

    /// <summary>
    /// Gets all expanded item hit areas for iteration.
    /// </summary>
    IReadOnlyDictionary<int, List<(BranchLabel Label, Rect HitArea)>> GetAllExpandedItemHitAreas();

    /// <summary>
    /// Gets all expanded tag hit areas for iteration.
    /// </summary>
    IReadOnlyDictionary<int, List<Rect>> GetAllExpandedTagHitAreas();

    /// <summary>
    /// Clears all hit areas. Should be called at the start of OnRender.
    /// </summary>
    void ClearHitAreas();

    /// <summary>
    /// Clears expanded item hit areas for a specific node.
    /// </summary>
    void ClearExpandedItemHitAreas(int nodeIndex);

    /// <summary>
    /// Clears expanded tag hit areas for a specific node.
    /// </summary>
    void ClearExpandedTagHitAreas(int nodeIndex);
}
