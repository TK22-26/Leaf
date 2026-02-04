using System.Windows;
using Leaf.Models;

namespace Leaf.Controls.GitGraph.Services;

/// <summary>
/// Implementation of <see cref="IGitGraphHitTestService"/> for hit testing.
/// </summary>
public sealed class GitGraphHitTestService : IGitGraphHitTestService
{
    private readonly Dictionary<int, (List<BranchLabel> Labels, Rect HitArea)> _overflowByRow = new();
    private readonly Dictionary<int, (List<string> Tags, Rect HitArea, double StartX)> _tagOverflowByRow = new();
    private readonly Dictionary<int, List<(BranchLabel Label, Rect HitArea)>> _expandedItemHitAreas = new();
    private readonly Dictionary<int, List<Rect>> _expandedTagHitAreas = new();

    public void RegisterOverflowHitArea(int displayRow, List<BranchLabel> labels, Rect hitArea)
    {
        _overflowByRow[displayRow] = (labels, hitArea);
    }

    public void RegisterTagOverflowHitArea(int displayRow, List<string> tags, Rect hitArea, double startX)
    {
        _tagOverflowByRow[displayRow] = (tags, hitArea, startX);
    }

    public void RegisterExpandedItemHitArea(int nodeIndex, List<(BranchLabel Label, Rect HitArea)> items)
    {
        _expandedItemHitAreas[nodeIndex] = items;
    }

    public void RegisterExpandedTagHitArea(int nodeIndex, List<Rect> hitAreas)
    {
        _expandedTagHitAreas[nodeIndex] = hitAreas;
    }

    public (int NodeIndex, int BranchIndex)? GetExpandedItemAt(Point position)
    {
        foreach (var kvp in _expandedItemHitAreas)
        {
            for (int i = 0; i < kvp.Value.Count; i++)
            {
                if (kvp.Value[i].HitArea.Contains(position))
                {
                    return (kvp.Key, i);
                }
            }
        }
        return null;
    }

    public int? GetOverflowRowAt(Point position)
    {
        foreach (var kvp in _overflowByRow)
        {
            if (kvp.Value.HitArea.Contains(position))
            {
                return kvp.Key;
            }
        }
        return null;
    }

    public int? GetTagOverflowRowAt(Point position)
    {
        foreach (var kvp in _tagOverflowByRow)
        {
            if (kvp.Value.HitArea.Contains(position))
            {
                return kvp.Key;
            }
        }
        return null;
    }

    public bool IsInsideExpandedArea(Point position)
    {
        // Check expanded item hit areas
        foreach (var kvp in _expandedItemHitAreas)
        {
            foreach (var item in kvp.Value)
            {
                if (item.HitArea.Contains(position))
                    return true;
            }
        }

        // Check expanded tag hit areas
        foreach (var kvp in _expandedTagHitAreas)
        {
            foreach (var rect in kvp.Value)
            {
                if (rect.Contains(position))
                    return true;
            }
        }

        // Check overflow indicators
        foreach (var kvp in _overflowByRow)
        {
            if (kvp.Value.HitArea.Contains(position))
                return true;
        }

        foreach (var kvp in _tagOverflowByRow)
        {
            if (kvp.Value.HitArea.Contains(position))
                return true;
        }

        return false;
    }

    public (List<BranchLabel> Labels, Rect HitArea)? GetOverflowByRow(int displayRow)
    {
        return _overflowByRow.TryGetValue(displayRow, out var result) ? result : null;
    }

    public (List<string> Tags, Rect HitArea, double StartX)? GetTagOverflowByRow(int displayRow)
    {
        return _tagOverflowByRow.TryGetValue(displayRow, out var result) ? result : null;
    }

    public IReadOnlyDictionary<int, (List<BranchLabel> Labels, Rect HitArea)> GetAllOverflowAreas() => _overflowByRow;

    public IReadOnlyDictionary<int, (List<string> Tags, Rect HitArea, double StartX)> GetAllTagOverflowAreas() => _tagOverflowByRow;

    public List<(BranchLabel Label, Rect HitArea)>? GetExpandedItemHitAreas(int nodeIndex)
    {
        return _expandedItemHitAreas.TryGetValue(nodeIndex, out var result) ? result : null;
    }

    public List<Rect>? GetExpandedTagHitAreas(int nodeIndex)
    {
        return _expandedTagHitAreas.TryGetValue(nodeIndex, out var result) ? result : null;
    }

    public IReadOnlyDictionary<int, List<(BranchLabel Label, Rect HitArea)>> GetAllExpandedItemHitAreas() => _expandedItemHitAreas;

    public IReadOnlyDictionary<int, List<Rect>> GetAllExpandedTagHitAreas() => _expandedTagHitAreas;

    public void ClearHitAreas()
    {
        _overflowByRow.Clear();
        _tagOverflowByRow.Clear();
        // Note: expanded item hit areas are cleared per-node, not globally
    }

    public void ClearExpandedItemHitAreas(int nodeIndex)
    {
        _expandedItemHitAreas.Remove(nodeIndex);
    }

    public void ClearExpandedTagHitAreas(int nodeIndex)
    {
        _expandedTagHitAreas.Remove(nodeIndex);
    }
}
