using CommunityToolkit.Mvvm.ComponentModel;

namespace Leaf.Models;

/// <summary>
/// Represents a Git worktree.
/// </summary>
public partial class WorktreeInfo : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    private bool _isCurrent;

    /// <summary>
    /// The absolute path to the worktree directory.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// The SHA of the HEAD commit in this worktree.
    /// </summary>
    public string HeadSha { get; set; } = string.Empty;

    /// <summary>
    /// The branch name checked out in this worktree, or null if detached.
    /// </summary>
    public string? BranchName { get; set; }

    /// <summary>
    /// True if this is the main (original) worktree of the repository.
    /// </summary>
    public bool IsMainWorktree { get; set; }

    /// <summary>
    /// True if the worktree is in detached HEAD state.
    /// </summary>
    public bool IsDetached { get; set; }

    /// <summary>
    /// The reason the worktree is locked, if any.
    /// </summary>
    public string? LockReason { get; set; }

    /// <summary>
    /// Whether the worktree node is expanded in the TreeView.
    /// </summary>
    public bool IsExpanded { get; set; }

    /// <summary>
    /// Gets the display name (folder name) for this worktree.
    /// </summary>
    public string DisplayName => System.IO.Path.GetFileName(
        Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

    /// <summary>
    /// Gets the full display text including branch info, for use in truncating TextBlock.
    /// Example: "worktree-name (main)" or "worktree-name (detached)"
    /// </summary>
    public string DisplayText
    {
        get
        {
            if (IsDetached)
                return $"{DisplayName} (detached)";
            if (!string.IsNullOrEmpty(BranchName))
                return $"{DisplayName} ({BranchName})";
            return DisplayName;
        }
    }

    /// <summary>
    /// Returns true if the worktree directory exists on disk.
    /// </summary>
    public bool Exists => System.IO.Directory.Exists(Path);
}
