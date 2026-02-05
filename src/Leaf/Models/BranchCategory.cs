using System.Collections.ObjectModel;

namespace Leaf.Models;

/// <summary>
/// Category for grouping branches (Local/Remote) in the tree view.
/// </summary>
public class BranchCategory
{
    /// <summary>
    /// Category name (e.g., "LOCAL", "REMOTE").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Icon for this category.
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// Number of branches in this category (for display).
    /// </summary>
    public int BranchCount { get; set; }

    /// <summary>
    /// Whether this category is expanded.
    /// </summary>
    public bool IsExpanded { get; set; } = true;

    /// <summary>
    /// Direct branches (used for LOCAL category).
    /// </summary>
    public ObservableCollection<BranchInfo> Branches { get; set; } = [];

    /// <summary>
    /// Remote groups (used for REMOTE category).
    /// </summary>
    public ObservableCollection<RemoteBranchGroup> RemoteGroups { get; set; } = [];

    /// <summary>
    /// Tags (used for TAGS category).
    /// </summary>
    public ObservableCollection<TagInfo> Tags { get; set; } = [];

    /// <summary>
    /// Worktrees (used for WORKTREES category).
    /// </summary>
    public ObservableCollection<WorktreeInfo> Worktrees { get; set; } = [];

    /// <summary>
    /// Whether this is the LOCAL category (for template selection).
    /// </summary>
    public bool IsLocalCategory => Name == "LOCAL";

    /// <summary>
    /// Whether this is the REMOTE category (for template selection).
    /// </summary>
    public bool IsRemoteCategory => Name == "REMOTE";

    /// <summary>
    /// Whether this is the TAGS category (for template selection).
    /// </summary>
    public bool IsTagsCategory => Name == "TAGS";

    /// <summary>
    /// Whether this is the GITFLOW category (for template selection).
    /// </summary>
    public bool IsGitFlowCategory => Name == "GITFLOW";

    /// <summary>
    /// Whether this is the WORKTREES category (for template selection).
    /// </summary>
    public bool IsWorktreesCategory => Name == "WORKTREES";

    /// <summary>
    /// Categories are never "current" - this silences binding warnings in TreeView.
    /// </summary>
    public bool IsCurrent => false;

    /// <summary>
    /// Categories are never "selected" - this silences binding warnings in TreeView.
    /// </summary>
    public bool IsSelected => false;
}
