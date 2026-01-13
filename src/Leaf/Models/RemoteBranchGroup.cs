using System.Collections.ObjectModel;

namespace Leaf.Models;

/// <summary>
/// Groups remote branches by remote name (e.g., origin, upstream).
/// </summary>
public class RemoteBranchGroup
{
    /// <summary>
    /// Remote name (e.g., "origin", "upstream").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Branches from this remote.
    /// </summary>
    public ObservableCollection<BranchInfo> Branches { get; set; } = [];

    /// <summary>
    /// Whether this remote group is expanded.
    /// </summary>
    public bool IsExpanded { get; set; } = true;
}
