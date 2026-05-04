namespace Leaf.Models;

/// <summary>
/// Represents a file with merge conflicts.
/// </summary>
public partial class ConflictInfo : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    /// <summary>
    /// Full path to the conflicting file.
    /// </summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _filePath = string.Empty;

    /// <summary>
    /// Just the file name (no path).
    /// </summary>
    public string FileName => System.IO.Path.GetFileName(FilePath);

    /// <summary>
    /// Whether this conflict has been resolved.
    /// </summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isResolved;

    /// <summary>
    /// How many of the file's <see cref="ConflictCount"/> regions have been
    /// resolved (Accept Ours / Theirs / Both / Manual). 0 ≤ value ≤
    /// <see cref="ConflictCount"/>. The merge editor's
    /// <c>NotifyResolutionCountsChanged</c> writes the live count here so the
    /// file tree can render a Sublime-Merge-style accent stripe with a
    /// progress fill that grows green as regions get accepted, instead of a
    /// static "unresolved/resolved" binary.
    /// </summary>
    /// <remarks>
    /// Auto-syncs with <see cref="IsResolved"/>: flipping IsResolved=true
    /// forces ResolvedRegionCount to <see cref="ConflictCount"/> (covers the
    /// file-level "Use Ours" / "Use Theirs" path, which doesn't go through
    /// the editor's per-range bookkeeping). Flipping back to false resets
    /// the count to 0 — an unresolve action discards every accept made
    /// inside the file, matching the existing semantics.
    /// </remarks>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private int _resolvedRegionCount;

    partial void OnFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(FileName));
    }

    partial void OnIsResolvedChanged(bool value)
    {
        ResolvedRegionCount = value ? ConflictCount : 0;
    }

    /// <summary>
    /// Content from the current branch (HEAD / "ours").
    /// </summary>
    public string OursContent { get; set; } = string.Empty;

    /// <summary>
    /// Content from the incoming branch ("theirs").
    /// </summary>
    public string TheirsContent { get; set; } = string.Empty;

    /// <summary>
    /// The base/ancestor content (before both branches diverged).
    /// </summary>
    public string BaseContent { get; set; } = string.Empty;

    /// <summary>
    /// The final resolved/merged content.
    /// </summary>
    public string MergedContent { get; set; } = string.Empty;

    /// <summary>
    /// Number of conflict regions in this file. Defaults to 1 because the
    /// git-plumbing path (which creates these instances from
    /// <c>git status --porcelain</c>) only learns "this file is conflicted"
    /// — the per-region count comes from the merge engine and is pushed
    /// back here by <c>MergeEditorViewModel.BuildDocumentForSelectedAsync</c>
    /// once the engine parses the file. Marked observable so the file tree's
    /// progress-stripe denominator updates the moment the engine reports
    /// the real count instead of leaving the stripe to overshoot to 100 %
    /// after one accept on a file that actually has many regions.
    /// </summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private int _conflictCount = 1;

    partial void OnConflictCountChanged(int value)
    {
        // Keep ResolvedRegionCount in sync when ConflictCount changes while
        // IsResolved=true (e.g. an unresolved-then-resolved-then-unresolved
        // cycle, or the engine landing the real count after a Use-Ours
        // bypass already set IsResolved). For the partial-progress path,
        // ResolvedRegionCount is owned by the editor and shouldn't be
        // clobbered here.
        if (IsResolved) ResolvedRegionCount = value;
    }
}
