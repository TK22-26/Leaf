using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Leaf.Models;

/// <summary>
/// Represents the result of a three-way merge for a single file.
/// Contains all merge regions and provides methods to build the final merged content.
/// </summary>
public partial class FileMergeResult : ObservableObject
{
    /// <summary>
    /// Path to the file being merged.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// All regions in the merge result, in order.
    /// </summary>
    public ObservableCollection<MergeRegion> Regions { get; set; } = [];

    /// <summary>
    /// Total number of conflict regions.
    /// </summary>
    public int ConflictCount => Regions.Count(r => r.IsConflict);

    /// <summary>
    /// Number of unresolved conflict regions.
    /// </summary>
    public int UnresolvedCount => Regions.Count(r => r.IsConflict && !r.IsResolved);

    /// <summary>
    /// Number of resolved conflict regions.
    /// </summary>
    public int ResolvedCount => ConflictCount - UnresolvedCount;

    /// <summary>
    /// Whether all conflicts have been resolved.
    /// </summary>
    public bool IsFullyResolved => UnresolvedCount == 0;

    /// <summary>
    /// Whether there are any auto-merged changes (OursOnly or TheirsOnly).
    /// </summary>
    public bool HasAutoMergedChanges => Regions.Any(r =>
        r.Type == MergeRegionType.OursOnly || r.Type == MergeRegionType.TheirsOnly);

    /// <summary>
    /// Get the fully merged content from all resolved regions.
    /// </summary>
    public string GetMergedContent()
    {
        var sb = new StringBuilder();
        var first = true;

        foreach (var region in Regions)
        {
            var content = region.GetResolvedContent();
            if (string.IsNullOrEmpty(content) && region.IsConflict && !region.IsResolved)
                continue; // Skip unresolved conflicts

            if (!first && !string.IsNullOrEmpty(content))
                sb.Append('\n');

            sb.Append(content);
            first = false;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Get content for display, showing conflict markers for unresolved regions.
    /// </summary>
    public string GetDisplayContent()
    {
        var sb = new StringBuilder();
        var first = true;

        foreach (var region in Regions)
        {
            if (!first)
                sb.Append('\n');

            if (region.IsConflict && !region.IsResolved)
            {
                // Show conflict markers for unresolved conflicts
                sb.Append("<<<<<<< OURS (current)\n");
                sb.Append(string.Join("\n", region.OursLines));
                sb.Append("\n=======\n");
                sb.Append(string.Join("\n", region.TheirsLines));
                sb.Append("\n>>>>>>> THEIRS (incoming)");
            }
            else
            {
                sb.Append(region.GetResolvedContent());
            }

            first = false;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Calculate cumulative line numbers for all regions.
    /// Call this after building the regions to set StartLineNumber correctly.
    /// </summary>
    public void CalculateLineNumbers()
    {
        int lineNumber = 1;
        foreach (var region in Regions)
        {
            region.StartLineNumber = lineNumber;
            lineNumber += region.LineCount;
            if (region.LineCount > 0)
                lineNumber++; // Account for newline between regions
        }
    }

    /// <summary>
    /// Get the index of the first unresolved conflict.
    /// </summary>
    public int GetFirstUnresolvedConflictIndex()
    {
        for (int i = 0; i < Regions.Count; i++)
        {
            if (Regions[i].IsConflict && !Regions[i].IsResolved)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Get the index of the next unresolved conflict after the given index.
    /// </summary>
    public int GetNextUnresolvedConflictIndex(int afterIndex)
    {
        for (int i = afterIndex + 1; i < Regions.Count; i++)
        {
            if (Regions[i].IsConflict && !Regions[i].IsResolved)
                return i;
        }
        // Wrap around to beginning
        for (int i = 0; i <= afterIndex && i < Regions.Count; i++)
        {
            if (Regions[i].IsConflict && !Regions[i].IsResolved)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Get the index of the previous unresolved conflict before the given index.
    /// </summary>
    public int GetPreviousUnresolvedConflictIndex(int beforeIndex)
    {
        for (int i = beforeIndex - 1; i >= 0; i--)
        {
            if (Regions[i].IsConflict && !Regions[i].IsResolved)
                return i;
        }
        // Wrap around to end
        for (int i = Regions.Count - 1; i >= beforeIndex && i >= 0; i--)
        {
            if (Regions[i].IsConflict && !Regions[i].IsResolved)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Notify that resolution status has changed.
    /// </summary>
    public void NotifyResolutionChanged()
    {
        OnPropertyChanged(nameof(UnresolvedCount));
        OnPropertyChanged(nameof(ResolvedCount));
        OnPropertyChanged(nameof(IsFullyResolved));
    }
}
