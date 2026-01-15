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
    /// Whether this is the REMOTE category (for template selection).
    /// </summary>
    public bool IsRemoteCategory => Name == "REMOTE";

    /// <summary>
    /// Whether this is the GITFLOW category (for template selection).
    /// </summary>
    public bool IsGitFlowCategory => Name == "GITFLOW";

    /// <summary>
    /// Categories are never "current" - this silences binding warnings in TreeView.
    /// </summary>
    public bool IsCurrent => false;
}
