#nullable enable
using System.ComponentModel;
using System.Windows;
using FluentIcons.Common;
using Leaf.Models;
// PropertyChangedEventManager lives in the WindowsBase WPF assembly under
// System.Windows; the explicit using keeps the weak-event subscription call
// readable below.

namespace Leaf.ViewModels.Merge;

/// <summary>
/// Node in the grouped conflict tree shown by
/// <see cref="Leaf.Controls.Merge.ConflictFileTree"/>. Represents either a
/// folder (grouping children) or a file (terminal leaf wrapping a
/// <see cref="ConflictInfo"/>). The structure (Conflict / Children /
/// DisplayName / IconSymbol) is fixed at construction; the count fields
/// (<see cref="TotalRegionCount"/> / <see cref="ResolvedRegionCount"/> /
/// <see cref="UnresolvedCount"/> / <see cref="IsResolved"/>) are live —
/// they forward / aggregate the underlying <see cref="ConflictInfo"/>'s
/// observable counts so the file tree's accent-stripe progress fill grows
/// in real time as the merge editor accepts regions, without a tree
/// rebuild on every keystroke.
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
/// File leaves subscribe to their <see cref="ConflictInfo"/>'s
/// <see cref="INotifyPropertyChanged"/> via WPF's
/// <see cref="PropertyChangedEventManager"/> (weak event) so a tree rebuild
/// can drop the old node references without leaking subscriptions on the
/// long-lived <see cref="ConflictInfo"/> instances. Folder nodes subscribe
/// to their immediate children with a strong handler — children and parent
/// have the same lifetime so there's no leak risk.
/// </para>
/// </remarks>
public sealed class ConflictTreeNode : INotifyPropertyChanged
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
    /// Total region count for this node:
    /// for a file leaf, the file's <see cref="ConflictInfo.ConflictCount"/>;
    /// for a folder, the sum across descendants. Drives the denominator of
    /// the progress-stripe fill ratio.
    /// </summary>
    public int TotalRegionCount
    {
        get
        {
            if (Conflict is not null) return Conflict.ConflictCount;
            int sum = 0;
            foreach (var c in Children) sum += c.TotalRegionCount;
            return sum;
        }
    }

    /// <summary>
    /// Resolved region count for this node:
    /// for a file leaf, forwards <see cref="ConflictInfo.ResolvedRegionCount"/>
    /// — except when <see cref="ConflictInfo.IsResolved"/> is <c>true</c>,
    /// in which case every region is treated as resolved regardless of
    /// what the field holds. This keeps the file-level resolution paths
    /// (Use Ours / Use Theirs / Mark Resolved on Selected) coherent
    /// against object-initializer ordering and against legacy callers
    /// that may flip <c>IsResolved</c> without touching the per-region
    /// count. For a folder, returns the sum across descendants.
    /// Drives the numerator of the progress-stripe fill ratio.
    /// </summary>
    public int ResolvedRegionCount
    {
        get
        {
            if (Conflict is not null)
            {
                return Conflict.IsResolved
                    ? Conflict.ConflictCount
                    : Conflict.ResolvedRegionCount;
            }
            int sum = 0;
            foreach (var c in Children) sum += c.ResolvedRegionCount;
            return sum;
        }
    }

    /// <summary>
    /// Unresolved region count = <see cref="TotalRegionCount"/> -
    /// <see cref="ResolvedRegionCount"/>. Live aggregate that re-emits when
    /// either count changes underneath.
    /// </summary>
    public int UnresolvedCount => TotalRegionCount - ResolvedRegionCount;

    /// <summary>
    /// <c>true</c> iff every region underneath is resolved. Folders never
    /// mark themselves resolved independently of their contents.
    /// </summary>
    public bool IsResolved => UnresolvedCount == 0;

    /// <summary>File-leaf convenience: returns the full repo-relative path.</summary>
    public string? FilePath => Conflict?.FilePath;

    /// <summary><c>true</c> for folder nodes (i.e. <see cref="Conflict"/> is null).</summary>
    public bool IsFolder => Conflict is null;

    /// <summary>
    /// FluentIcon <see cref="Symbol"/> the tree row renders next to
    /// <see cref="DisplayName"/>. Resolved once at node construction through
    /// <see cref="FileTypeIconResolver"/> so the XAML binding stays a simple
    /// <c>{Binding IconSymbol}</c> and swapping the extension-to-glyph table
    /// only touches the resolver.
    /// </summary>
    public Symbol IconSymbol { get; }

    /// <summary>Constructs a folder node. Throws if <paramref name="children"/> is empty.</summary>
    public static ConflictTreeNode Folder(string displayName, string fullPath, IReadOnlyList<ConflictTreeNode> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        ArgumentException.ThrowIfNullOrEmpty(fullPath);
        if (children.Count == 0)
        {
            throw new ArgumentException("Folder nodes must have at least one child.", nameof(children));
        }
        return new ConflictTreeNode(displayName, fullPath, children, conflict: null, Symbol.Folder);
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
        var icon = FileTypeIconResolver.ResolveForFile(conflict.FilePath);
        return new ConflictTreeNode(
            displayName: System.IO.Path.GetFileName(conflict.FilePath),
            fullPath: conflict.FilePath,
            children: Array.Empty<ConflictTreeNode>(),
            conflict: conflict,
            iconSymbol: icon);
    }

    private ConflictTreeNode(string displayName, string fullPath, IReadOnlyList<ConflictTreeNode> children, ConflictInfo? conflict, Symbol iconSymbol)
    {
        DisplayName = displayName ?? string.Empty;
        FullPath = fullPath;
        Children = children;
        Conflict = conflict;
        IconSymbol = iconSymbol;

        if (conflict is not null)
        {
            // Weak event so a tree rebuild can drop this node without
            // anchoring it via a strong delegate on the long-lived
            // ConflictInfo instance.
            PropertyChangedEventManager.AddHandler(conflict, OnConflictPropertyChanged, string.Empty);
        }
        else
        {
            // Children outlive this folder by exactly the duration of the
            // tree they're both in — strong subscription is safe.
            foreach (var c in children) c.PropertyChanged += OnChildPropertyChanged;
        }
    }

    private void OnConflictPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ConflictInfo.ResolvedRegionCount):
                EmitCountChanged();
                break;
            case nameof(ConflictInfo.ConflictCount):
                OnPropertyChanged(nameof(TotalRegionCount));
                OnPropertyChanged(nameof(UnresolvedCount));
                break;
            case nameof(ConflictInfo.IsResolved):
                OnPropertyChanged(nameof(IsResolved));
                break;
        }
    }

    private void OnChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Any of the four count properties on a child invalidates this
        // folder's aggregate. Re-emit the matching set; bindings refresh.
        if (e.PropertyName is nameof(ResolvedRegionCount)
                          or nameof(TotalRegionCount)
                          or nameof(UnresolvedCount)
                          or nameof(IsResolved))
        {
            EmitCountChanged();
        }
    }

    private void EmitCountChanged()
    {
        OnPropertyChanged(nameof(ResolvedRegionCount));
        OnPropertyChanged(nameof(UnresolvedCount));
        OnPropertyChanged(nameof(IsResolved));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
