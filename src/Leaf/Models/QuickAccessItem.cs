namespace Leaf.Models;

/// <summary>
/// Wrapper for RepositoryInfo used in PINNED and MOST RECENT sections.
/// This ensures unique object instances for TreeView selection to work correctly
/// when the same repository appears in both quick access sections and folder groups.
/// </summary>
public class QuickAccessItem
{
    public RepositoryInfo Repository { get; }

    public QuickAccessItem(RepositoryInfo repository)
    {
        Repository = repository;
    }

    // Convenience properties that delegate to the underlying repository
    public string Name => Repository.Name;
    public string Path => Repository.Path;
    public bool IsPinned => Repository.IsPinned;

    // Required for TreeViewItem binding (leaf nodes don't expand, but binding must exist)
    public bool IsExpanded { get; set; }
}
