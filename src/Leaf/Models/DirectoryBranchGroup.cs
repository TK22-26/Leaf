using System.Collections.ObjectModel;

namespace Leaf.Models;

/// <summary>
/// Groups local branches by their directory prefix (e.g., "dev/", "feature/").
/// Similar to <see cref="RemoteBranchGroup"/> but for local branch directory structures.
/// </summary>
public class DirectoryBranchGroup
{
    /// <summary>
    /// Directory prefix name (e.g., "dev", "feature").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Branches within this directory group.
    /// </summary>
    public ObservableCollection<BranchInfo> Branches { get; set; } = [];

    /// <summary>
    /// Whether this group is expanded in the TreeView.
    /// </summary>
    public bool IsExpanded { get; set; } = true;

    /// <summary>
    /// Groups are never "current" — silences TreeView binding warnings.
    /// </summary>
    public bool IsCurrent => false;

    /// <summary>
    /// Groups are never "selected" — silences TreeView binding warnings.
    /// </summary>
    public bool IsSelected => false;
}
