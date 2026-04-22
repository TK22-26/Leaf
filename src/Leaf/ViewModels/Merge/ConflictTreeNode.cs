#nullable enable
using Leaf.Models;

namespace Leaf.ViewModels.Merge;

/// <summary>
/// Node in the grouped conflict tree shown by
/// <see cref="Leaf.Controls.Merge.ConflictFileTree"/>. Represents either a
/// folder (grouping children) or a file (terminal leaf wrapping a
/// <see cref="ConflictInfo"/>). Immutable — the tree is rebuilt whenever
/// the underlying <c>Conflicts</c> collection changes.
/// </summary>
/// <remarks>
/// <para>
/// Folders have <see cref="Conflict"/> = <c>null</c> and non-empty
/// <see cref="Children"/>. Files have a non-null <see cref="Conflict"/> and
/// an empty <see cref="Children"/>. The WPF TreeView binds each node through
/// a type-selecting <c>HierarchicalDataTemplate</c> keyed on
/// <see cref="ConflictTreeNode"/>.
/// </para>
/// <para>
/// <see cref="UnresolvedCount"/> aggregates up the tree: for a file it's 0
/// when <see cref="ConflictInfo.IsResolved"/> is true and
/// <see cref="ConflictInfo.ConflictCount"/> otherwise; for a folder it's the
/// sum over all descendants. This makes the "(N)" pill next to a folder
/// meaningful without the view having to walk children.
/// </para>
/// </remarks>
public sealed class ConflictTreeNode
{
    /// <summary>Path segment (file name for leaves, folder name for groups).</summary>
    public string DisplayName { get; }

    /// <summary>
    /// Full repo-relative path for folders (e.g. <c>"src/Utils"</c>). File
    /// leaves re-use <see cref="ConflictInfo.FilePath"/>. Used by
    /// <see cref="Leaf.Controls.Merge.ConflictFileTree"/> to persist
    /// per-folder expansion state across tree rebuilds.
    /// </summary>
    public string FullPath { get; }

    /// <summary>Children for folder nodes; empty list for file leaves.</summary>
    public IReadOnlyList<ConflictTreeNode> Children { get; }

    /// <summary>Backing <see cref="ConflictInfo"/> for file leaves; <c>null</c> for folders.</summary>
    public ConflictInfo? Conflict { get; }

    /// <summary>
    /// Unresolved conflict count:
    /// for a file leaf, 0 when resolved / <see cref="ConflictInfo.ConflictCount"/> otherwise;
    /// for a folder, the sum over descendants.
    /// </summary>
    public int UnresolvedCount { get; }

    /// <summary>
    /// <c>true</c> iff every file underneath is resolved (i.e.
    /// <see cref="UnresolvedCount"/> == 0). Folders never mark themselves
    /// resolved independently of their contents.
    /// </summary>
    public bool IsResolved => UnresolvedCount == 0;

    /// <summary>File-leaf convenience: returns the full repo-relative path.</summary>
    public string? FilePath => Conflict?.FilePath;

    /// <summary><c>true</c> for folder nodes (i.e. <see cref="Conflict"/> is null).</summary>
    public bool IsFolder => Conflict is null;

    /// <summary>Constructs a folder node. Throws if <paramref name="children"/> is empty.</summary>
    public static ConflictTreeNode Folder(string displayName, string fullPath, IReadOnlyList<ConflictTreeNode> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        ArgumentException.ThrowIfNullOrEmpty(fullPath);
        if (children.Count == 0)
        {
            throw new ArgumentException("Folder nodes must have at least one child.", nameof(children));
        }
        var unresolved = children.Sum(c => c.UnresolvedCount);
        return new ConflictTreeNode(displayName, fullPath, children, conflict: null, unresolved);
    }

    /// <summary>Constructs a file leaf node.</summary>
    public static ConflictTreeNode File(ConflictInfo conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        // ConflictCount is declared with a default of 1 in ConflictInfo and
        // Leaf's git-plumbing path always sets it >= 1 before the model
        // reaches the VM. No clamp here — if we ever see ConflictCount == 0
        // on an unresolved file that's a bug upstream, and hiding it behind
        // a Max(..., 1) would mask the diagnostic.
        var unresolved = conflict.IsResolved ? 0 : conflict.ConflictCount;
        return new ConflictTreeNode(
            displayName: System.IO.Path.GetFileName(conflict.FilePath),
            fullPath: conflict.FilePath,
            children: Array.Empty<ConflictTreeNode>(),
            conflict: conflict,
            unresolvedCount: unresolved);
    }

    private ConflictTreeNode(string displayName, string fullPath, IReadOnlyList<ConflictTreeNode> children, ConflictInfo? conflict, int unresolvedCount)
    {
        DisplayName = displayName ?? string.Empty;
        FullPath = fullPath;
        Children = children;
        Conflict = conflict;
        UnresolvedCount = unresolvedCount;
    }
}
