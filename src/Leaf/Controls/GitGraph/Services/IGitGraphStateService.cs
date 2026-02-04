using Leaf.Controls.GitGraph;

namespace Leaf.Controls.GitGraph.Services;

/// <summary>
/// Service for managing expansion state, animation progress, and hover state.
/// This service is UI-thread only.
/// </summary>
public interface IGitGraphStateService
{
    /// <summary>
    /// Checks if a node is expanded.
    /// </summary>
    bool IsNodeExpanded(int nodeIndex);

    /// <summary>
    /// Checks if a tag node is expanded.
    /// </summary>
    bool IsTagNodeExpanded(int nodeIndex);

    /// <summary>
    /// Toggles the expansion state of a node.
    /// </summary>
    /// <param name="nodeIndex">The node index to toggle</param>
    /// <param name="labelCount">The number of labels (for tracking extra rows)</param>
    /// <returns>True if the node is now expanded, false if collapsed</returns>
    bool ToggleNodeExpansion(int nodeIndex, int labelCount);

    /// <summary>
    /// Toggles the expansion state of a tag node.
    /// </summary>
    /// <param name="nodeIndex">The node index to toggle</param>
    /// <returns>True if the node is now expanded, false if collapsed</returns>
    bool ToggleTagNodeExpansion(int nodeIndex);

    /// <summary>
    /// Collapses all expanded nodes.
    /// </summary>
    void CollapseAllNodes();

    /// <summary>
    /// Collapses all expanded tag nodes.
    /// </summary>
    void CollapseAllTagNodes();

    /// <summary>
    /// Gets all expanded nodes with their extra row counts.
    /// </summary>
    IReadOnlyDictionary<int, int> GetExpandedNodes();

    /// <summary>
    /// Gets all expanded tag nodes.
    /// </summary>
    IReadOnlySet<int> GetExpandedTagNodes();

    /// <summary>
    /// Gets the current expansion animation progress for a node (0.0 to 1.0).
    /// Returns 0.0 if no animation is in progress.
    /// </summary>
    double GetExpansionProgress(int nodeIndex);

    /// <summary>
    /// Sets the initial animation progress for a node.
    /// </summary>
    void SetExpansionProgress(int nodeIndex, double progress);

    /// <summary>
    /// Updates animation progress for all animating nodes.
    /// </summary>
    /// <param name="stepMs">Animation step in milliseconds</param>
    /// <param name="durationMs">Total animation duration in milliseconds</param>
    void UpdateAnimationProgress(double stepMs, double durationMs);

    /// <summary>
    /// Checks if there are any active animations.
    /// </summary>
    bool HasActiveAnimations();

    /// <summary>
    /// Gets or sets the currently hovered expanded item.
    /// </summary>
    (int NodeIndex, int BranchIndex) HoveredExpandedItem { get; set; }

    /// <summary>
    /// Gets or sets the currently hovered overflow row.
    /// </summary>
    int HoveredOverflowRow { get; set; }

    /// <summary>
    /// Gets the total extra height from all expanded rows.
    /// </summary>
    double GetTotalExpansionHeight(double rowHeight);

    /// <summary>
    /// Checks if there are any expanded nodes (branch or tag).
    /// </summary>
    bool HasExpandedNodes();

    /// <summary>
    /// Event raised when a row expansion state changes.
    /// </summary>
    event EventHandler<RowExpansionChangedEventArgs>? RowExpansionChanged;
}
