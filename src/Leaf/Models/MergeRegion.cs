using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Leaf.Models;

/// <summary>
/// Represents a single selectable line in a conflict region.
/// </summary>
public partial class SelectableLine : ObservableObject
{
    /// <summary>
    /// Line number within the conflict region (0-based).
    /// </summary>
    public int LineIndex { get; set; }

    /// <summary>
    /// The text content of this line.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Whether this line is selected for inclusion in the merged result.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Represents a region in a three-way merge result.
/// Can be unchanged content, auto-merged changes, or a conflict requiring resolution.
/// </summary>
public partial class MergeRegion : ObservableObject
{
    /// <summary>
    /// Index of this region in the merge result.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Type of this region (Unchanged, OursOnly, TheirsOnly, Conflict).
    /// </summary>
    public MergeRegionType Type { get; set; }

    /// <summary>
    /// Starting line number in the merged file (1-based, for display).
    /// </summary>
    public int StartLineNumber { get; set; }

    /// <summary>
    /// For non-conflict regions: the content as a single string (memory efficient).
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// For conflict regions: raw lines from "ours" version.
    /// </summary>
    public List<string> OursLines { get; set; } = [];

    /// <summary>
    /// For conflict regions: raw lines from "theirs" version.
    /// </summary>
    public List<string> TheirsLines { get; set; } = [];

    /// <summary>
    /// Lazy-initialized selectable lines from "ours" (only for conflicts).
    /// </summary>
    public ObservableCollection<SelectableLine>? OursSelectableLines { get; set; }

    /// <summary>
    /// Lazy-initialized selectable lines from "theirs" (only for conflicts).
    /// </summary>
    public ObservableCollection<SelectableLine>? TheirsSelectableLines { get; set; }

    /// <summary>
    /// Whether this conflict is in manual edit mode.
    /// </summary>
    [ObservableProperty]
    private bool _isManualEditMode;

    /// <summary>
    /// Content for manual edit mode (free-form text editing).
    /// </summary>
    [ObservableProperty]
    private string _manualEditContent = string.Empty;

    /// <summary>
    /// How this conflict has been resolved.
    /// </summary>
    [ObservableProperty]
    private ConflictResolution _resolution = ConflictResolution.Unresolved;

    /// <summary>
    /// Whether this region is a conflict requiring resolution.
    /// </summary>
    public bool IsConflict => Type == MergeRegionType.Conflict;

    /// <summary>
    /// Whether this region is resolved (non-conflicts are always resolved).
    /// </summary>
    public bool IsResolved => !IsConflict || Resolution != ConflictResolution.Unresolved;

    /// <summary>
    /// Number of lines in this region.
    /// </summary>
    public int LineCount
    {
        get
        {
            if (Type == MergeRegionType.Conflict)
            {
                return Resolution switch
                {
                    ConflictResolution.UseOurs => OursLines.Count,
                    ConflictResolution.UseTheirs => TheirsLines.Count,
                    ConflictResolution.UseCustom => GetSelectedLineCount(),
                    ConflictResolution.UseManual => ManualEditContent.Split('\n').Length,
                    _ => Math.Max(OursLines.Count, TheirsLines.Count)
                };
            }

            return string.IsNullOrEmpty(Content) ? 0 : Content.Split('\n').Length;
        }
    }

    /// <summary>
    /// Initialize selectable lines for conflict resolution.
    /// Only creates them for conflict regions and only when needed (lazy).
    /// </summary>
    public void InitializeSelectableLines()
    {
        if (Type != MergeRegionType.Conflict)
            return;

        OursSelectableLines ??= new ObservableCollection<SelectableLine>(
            OursLines.Select((line, index) => new SelectableLine
            {
                LineIndex = index,
                Content = line,
                IsSelected = false
            }));

        TheirsSelectableLines ??= new ObservableCollection<SelectableLine>(
            TheirsLines.Select((line, index) => new SelectableLine
            {
                LineIndex = index,
                Content = line,
                IsSelected = false
            }));
    }

    /// <summary>
    /// Select all lines from "ours" version.
    /// </summary>
    public void SelectAllOurs()
    {
        InitializeSelectableLines();
        if (OursSelectableLines != null)
        {
            foreach (var line in OursSelectableLines)
                line.IsSelected = true;
        }
        if (TheirsSelectableLines != null)
        {
            foreach (var line in TheirsSelectableLines)
                line.IsSelected = false;
        }
        Resolution = ConflictResolution.UseOurs;
    }

    /// <summary>
    /// Select all lines from "theirs" version.
    /// </summary>
    public void SelectAllTheirs()
    {
        InitializeSelectableLines();
        if (OursSelectableLines != null)
        {
            foreach (var line in OursSelectableLines)
                line.IsSelected = false;
        }
        if (TheirsSelectableLines != null)
        {
            foreach (var line in TheirsSelectableLines)
                line.IsSelected = true;
        }
        Resolution = ConflictResolution.UseTheirs;
    }

    /// <summary>
    /// Enter manual edit mode with current content.
    /// </summary>
    public void EnterManualEditMode()
    {
        if (!IsManualEditMode)
        {
            ManualEditContent = GetResolvedContent();
            IsManualEditMode = true;
            Resolution = ConflictResolution.UseManual;
        }
    }

    /// <summary>
    /// Exit manual edit mode.
    /// </summary>
    public void ExitManualEditMode()
    {
        IsManualEditMode = false;
        if (Resolution == ConflictResolution.UseManual && string.IsNullOrEmpty(ManualEditContent))
        {
            Resolution = ConflictResolution.Unresolved;
        }
    }

    /// <summary>
    /// Get the resolved content for this region.
    /// </summary>
    public string GetResolvedContent()
    {
        return Type switch
        {
            MergeRegionType.Unchanged => Content,
            MergeRegionType.OursOnly => Content,
            MergeRegionType.TheirsOnly => Content,
            MergeRegionType.Conflict => GetConflictResolvedContent(),
            _ => Content
        };
    }

    private string GetConflictResolvedContent()
    {
        return Resolution switch
        {
            ConflictResolution.UseOurs => string.Join("\n", OursLines),
            ConflictResolution.UseTheirs => string.Join("\n", TheirsLines),
            ConflictResolution.UseCustom => GetCustomSelectedContent(),
            ConflictResolution.UseManual => ManualEditContent,
            ConflictResolution.Unresolved => string.Empty, // Or could show conflict markers
            _ => string.Empty
        };
    }

    private string GetCustomSelectedContent()
    {
        var sb = new StringBuilder();
        var first = true;

        if (OursSelectableLines != null)
        {
            foreach (var line in OursSelectableLines.Where(l => l.IsSelected))
            {
                if (!first) sb.Append('\n');
                sb.Append(line.Content);
                first = false;
            }
        }

        if (TheirsSelectableLines != null)
        {
            foreach (var line in TheirsSelectableLines.Where(l => l.IsSelected))
            {
                if (!first) sb.Append('\n');
                sb.Append(line.Content);
                first = false;
            }
        }

        return sb.ToString();
    }

    private int GetSelectedLineCount()
    {
        int count = 0;
        if (OursSelectableLines != null)
            count += OursSelectableLines.Count(l => l.IsSelected);
        if (TheirsSelectableLines != null)
            count += TheirsSelectableLines.Count(l => l.IsSelected);
        return count;
    }

    /// <summary>
    /// Update resolution state based on current line selections.
    /// Call this when individual line selections change.
    /// </summary>
    public void UpdateResolutionFromSelection()
    {
        if (Type != MergeRegionType.Conflict || IsManualEditMode)
            return;

        InitializeSelectableLines();

        bool allOursSelected = OursSelectableLines?.All(l => l.IsSelected) ?? false;
        bool allTheirsSelected = TheirsSelectableLines?.All(l => l.IsSelected) ?? false;
        bool noOursSelected = OursSelectableLines?.All(l => !l.IsSelected) ?? true;
        bool noTheirsSelected = TheirsSelectableLines?.All(l => !l.IsSelected) ?? true;

        if (allOursSelected && noTheirsSelected)
            Resolution = ConflictResolution.UseOurs;
        else if (allTheirsSelected && noOursSelected)
            Resolution = ConflictResolution.UseTheirs;
        else if (!noOursSelected || !noTheirsSelected)
            Resolution = ConflictResolution.UseCustom;
        else
            Resolution = ConflictResolution.Unresolved;
    }
}
