using Leaf.Controls.GitGraph;

namespace Leaf.Controls.GitGraph.Services;

/// <summary>
/// Implementation of <see cref="IGitGraphStateService"/> for managing UI state.
/// This service is UI-thread only - all operations should occur on the dispatcher thread.
/// </summary>
public sealed class GitGraphStateService : IGitGraphStateService
{
    private readonly Dictionary<int, int> _expandedNodes = new();
    private readonly HashSet<int> _expandedTagNodes = new();
    private readonly Dictionary<int, double> _expansionProgress = new();

    public (int NodeIndex, int BranchIndex) HoveredExpandedItem { get; set; } = (-1, -1);
    public int HoveredOverflowRow { get; set; } = -1;

    public event EventHandler<RowExpansionChangedEventArgs>? RowExpansionChanged;

    public bool IsNodeExpanded(int nodeIndex) => _expandedNodes.ContainsKey(nodeIndex);

    public bool IsTagNodeExpanded(int nodeIndex) => _expandedTagNodes.Contains(nodeIndex);

    public bool ToggleNodeExpansion(int nodeIndex, int labelCount)
    {
        bool wasExpanded = _expandedNodes.ContainsKey(nodeIndex);
        bool isNowExpanded;

        if (wasExpanded)
        {
            _expandedNodes.Remove(nodeIndex);
            isNowExpanded = false;
        }
        else
        {
            _expandedNodes[nodeIndex] = labelCount;
            isNowExpanded = true;
        }

        // Initialize animation progress
        if (isNowExpanded)
        {
            _expansionProgress[nodeIndex] = 0.0;
        }
        // For collapsing, progress will decrease from current value during animation

        return isNowExpanded;
    }

    public bool ToggleTagNodeExpansion(int nodeIndex)
    {
        bool wasExpanded = _expandedTagNodes.Contains(nodeIndex);

        if (wasExpanded)
        {
            _expandedTagNodes.Remove(nodeIndex);
            return false;
        }
        else
        {
            _expandedTagNodes.Add(nodeIndex);
            return true;
        }
    }

    public void CollapseAllNodes()
    {
        if (_expandedNodes.Count == 0)
            return;

        var nodesToCollapse = _expandedNodes.Keys.ToList();
        foreach (var nodeIndex in nodesToCollapse)
        {
            _expandedNodes.Remove(nodeIndex);
            // Leave progress in dictionary for collapse animation
        }

        // Notify listeners
        RowExpansionChanged?.Invoke(this, new RowExpansionChangedEventArgs(
            -1, false, 0, 0));
    }

    public void CollapseAllTagNodes()
    {
        _expandedTagNodes.Clear();
    }

    public IReadOnlyDictionary<int, int> GetExpandedNodes() => _expandedNodes;

    public IReadOnlySet<int> GetExpandedTagNodes() => _expandedTagNodes;

    public double GetExpansionProgress(int nodeIndex)
    {
        return _expansionProgress.TryGetValue(nodeIndex, out var progress) ? progress : 0.0;
    }

    public void SetExpansionProgress(int nodeIndex, double progress)
    {
        _expansionProgress[nodeIndex] = progress;
    }

    public void UpdateAnimationProgress(double stepMs, double durationMs)
    {
        double step = stepMs / durationMs;

        var nodesToUpdate = _expansionProgress.Keys.ToList();
        foreach (var nodeIndex in nodesToUpdate)
        {
            bool isExpanded = _expandedNodes.ContainsKey(nodeIndex);
            double current = _expansionProgress[nodeIndex];

            if (isExpanded)
            {
                // Expanding - increase progress
                current = Math.Min(1.0, current + step);
                _expansionProgress[nodeIndex] = current;
            }
            else
            {
                // Collapsing - decrease progress
                current = Math.Max(0.0, current - step);
                if (current > 0.0)
                {
                    _expansionProgress[nodeIndex] = current;
                }
                else
                {
                    _expansionProgress.Remove(nodeIndex);
                }
            }
        }
    }

    public bool HasActiveAnimations()
    {
        foreach (var kvp in _expansionProgress)
        {
            bool isExpanded = _expandedNodes.ContainsKey(kvp.Key);
            if (isExpanded && kvp.Value < 1.0)
                return true;
            if (!isExpanded && kvp.Value > 0.0)
                return true;
        }
        return false;
    }

    public double GetTotalExpansionHeight(double rowHeight)
    {
        return _expandedNodes.Values.Sum() * rowHeight;
    }

    public bool HasExpandedNodes() => _expandedNodes.Count > 0 || _expandedTagNodes.Count > 0;

    /// <summary>
    /// Raises the RowExpansionChanged event. Called by the canvas when expansion changes.
    /// </summary>
    internal void RaiseRowExpansionChanged(int nodeIndex, bool isExpanded, int extraRows, double totalHeight)
    {
        RowExpansionChanged?.Invoke(this, new RowExpansionChangedEventArgs(
            nodeIndex, isExpanded, extraRows, totalHeight));
    }
}
