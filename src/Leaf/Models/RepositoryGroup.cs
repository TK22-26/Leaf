using System.Collections.ObjectModel;

namespace Leaf.Models;

/// <summary>
/// Type of repository grouping.
/// </summary>
public enum GroupType
{
    /// <summary>
    /// Auto-detected based on parent folder.
    /// </summary>
    Folder,

    /// <summary>
    /// User-defined custom group.
    /// </summary>
    Custom
}

/// <summary>
/// A group of repositories (folder-based or custom).
/// Supports nested groups via Composite pattern.
/// </summary>
public class RepositoryGroup
{
    /// <summary>
    /// Unique identifier for the group.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name of the group.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Type of grouping (Folder or Custom).
    /// </summary>
    public GroupType Type { get; set; }

    /// <summary>
    /// Repositories in this group.
    /// </summary>
    public ObservableCollection<RepositoryInfo> Repositories { get; set; } = [];

    /// <summary>
    /// Nested child groups (for hierarchical organization).
    /// </summary>
    public ObservableCollection<RepositoryGroup> Children { get; set; } = [];

    /// <summary>
    /// True if this group is expanded in the UI.
    /// </summary>
    public bool IsExpanded { get; set; } = true;

    /// <summary>
    /// True if this folder group is being watched for new repositories.
    /// </summary>
    public bool IsWatched { get; set; }

    /// <summary>
    /// Total count of repositories including nested groups.
    /// </summary>
    public int TotalRepositoryCount
    {
        get
        {
            int count = Repositories.Count;
            foreach (var child in Children)
            {
                count += child.TotalRepositoryCount;
            }
            return count;
        }
    }
}
