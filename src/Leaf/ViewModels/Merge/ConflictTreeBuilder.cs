#nullable enable
using Leaf.Models;

namespace Leaf.ViewModels.Merge;

/// <summary>
/// Groups a flat list of <see cref="ConflictInfo"/> by folder path into the
/// hierarchical tree rendered by <see cref="Leaf.Controls.Merge.ConflictFileTree"/>.
/// </summary>
/// <remarks>
/// <para>
/// The input paths are assumed to be repo-relative and use either forward
/// slashes (git's canonical form) or back-slashes (Windows UI); the builder
/// normalizes both by splitting on either separator. Empty path segments
/// (leading '/', double separators) are skipped silently.
/// </para>
/// <para>
/// Folder nodes are ordered alphabetically (ordinal, case-insensitive) and
/// folders come before files at each level — mirroring the pattern used by
/// Sublime Merge, VS Code, and GitKraken so users don't have to relearn the
/// layout.
/// </para>
/// </remarks>
public static class ConflictTreeBuilder
{
    /// <summary>
    /// Build the tree. Returns an empty list when <paramref name="conflicts"/>
    /// is empty; returns a flat list of file nodes (no folder wrapper) when
    /// all files live at the repo root.
    /// </summary>
    public static IReadOnlyList<ConflictTreeNode> Build(IReadOnlyList<ConflictInfo> conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        if (conflicts.Count == 0) return Array.Empty<ConflictTreeNode>();

        // Mutable intermediate tree: each directory maps name → sub-builder.
        var root = new FolderBuilder();
        foreach (var conflict in conflicts)
        {
            var segments = SplitPath(conflict.FilePath);
            InsertConflict(root, conflict, segments, depth: 0);
        }
        return Materialize(root, parentPath: string.Empty);
    }

    private static string[] SplitPath(string filePath)
    {
        // Null / empty FilePath indicates an upstream bug (every ConflictInfo
        // from the git-plumbing path has a non-empty path). Fail loudly rather
        // than silently emitting a nameless leaf attached to the root.
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        return filePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static void InsertConflict(FolderBuilder current, ConflictInfo conflict, string[] segments, int depth)
    {
        if (segments.Length == 0 || depth >= segments.Length - 1)
        {
            // Terminal: either a bare file at this depth or a path whose last
            // segment is the file name. Either way, attach the leaf here.
            current.Files.Add(ConflictTreeNode.File(conflict));
            return;
        }
        var dirName = segments[depth];
        if (!current.Folders.TryGetValue(dirName, out var child))
        {
            child = new FolderBuilder();
            current.Folders[dirName] = child;
        }
        InsertConflict(child, conflict, segments, depth + 1);
    }

    private static IReadOnlyList<ConflictTreeNode> Materialize(FolderBuilder builder, string parentPath)
    {
        var nodes = new List<ConflictTreeNode>();
        // Folders first, alphabetically.
        foreach (var kvp in builder.Folders.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var folderPath = parentPath.Length == 0 ? kvp.Key : parentPath + "/" + kvp.Key;
            var children = Materialize(kvp.Value, folderPath);
            if (children.Count == 0) continue;
            nodes.Add(ConflictTreeNode.Folder(kvp.Key, folderPath, children));
        }
        // Files after, alphabetically by display name.
        foreach (var file in builder.Files.OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            nodes.Add(file);
        }
        return nodes;
    }

    private sealed class FolderBuilder
    {
        public Dictionary<string, FolderBuilder> Folders { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public List<ConflictTreeNode> Files { get; } = new();
    }
}
