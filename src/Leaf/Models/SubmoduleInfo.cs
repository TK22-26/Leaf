using CommunityToolkit.Mvvm.ComponentModel;

namespace Leaf.Models;

/// <summary>
/// Summary of a single git submodule in a parent repository. Combines
/// registration data from <c>.gitmodules</c> with live state from
/// <c>git submodule status</c>.
/// </summary>
public sealed partial class SubmoduleInfo : ObservableObject
{
    /// <summary>
    /// Whether this submodule row is currently selected in the sidebar.
    /// Drives the same selection-visual style as branches and worktrees
    /// (tree-level Border DataTrigger in BranchListView's TreeViewItem
    /// style — the row's highlight + the green selection bar). Single-
    /// selection model: clicking a submodule clears the others' flags.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Satisfies <see cref="System.Windows.Controls.TreeViewItem.IsExpanded"/>
    /// binding inherited from <c>BranchListView.xaml</c>'s style. Submodules
    /// are leaf items (they don't have child rows to expand into) but every
    /// other model bound to that style — <c>BranchInfo</c>, <c>WorktreeInfo</c>,
    /// <c>TagInfo</c>, <c>PullRequestInfo</c> — declares the property too,
    /// so the binding-engine warning that fired for <c>SubmoduleInfo</c>
    /// rows ("IsExpanded property not found on object of type SubmoduleInfo")
    /// went away once this was added. The value is unused — kept private-
    /// init so callers can't accidentally rely on it.
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded;


    /// <summary>
    /// Logical name of the submodule as recorded in <c>.gitmodules</c>
    /// (the section key, <c>[submodule "name"]</c>). Usually matches
    /// <see cref="Path"/> but doesn't have to.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Path of the submodule within the parent repo, using forward
    /// slashes to match git's native form.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Clone URL from <c>.gitmodules</c> / <c>.git/config</c>. May be a
    /// relative URL; we surface it verbatim rather than resolving.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// The branch the submodule tracks, if <c>submodule.&lt;name&gt;.branch</c>
    /// is set. Null when the submodule pins to an explicit commit (the
    /// common case).
    /// </summary>
    public string? Branch { get; init; }

    /// <summary>
    /// Commit SHA recorded in the parent repository's tree for this
    /// submodule. Always present — it's what git is authoritative about.
    /// </summary>
    public required string RecordedSha { get; init; }

    /// <summary>
    /// Commit SHA currently checked out in the submodule's working
    /// directory. Null when the submodule is uninitialized (no clone on
    /// disk yet).
    /// </summary>
    public string? WorkingSha { get; init; }

    /// <summary>
    /// Human-readable ref the working commit matches, e.g.
    /// <c>heads/main</c> or <c>v1.2.3-4-gabc1234</c>. Null when
    /// uninitialized or when git cannot describe the commit.
    /// </summary>
    public string? Describe { get; init; }

    /// <summary>
    /// Overall submodule state — derived from the prefix character in
    /// <c>git submodule status</c> output.
    /// </summary>
    public required SubmoduleStatus Status { get; init; }

    /// <summary>
    /// Convenience: true when the submodule has been cloned and the
    /// working copy exists. Uninitialized entries are just registrations
    /// in <c>.gitmodules</c> with no on-disk content.
    /// </summary>
    public bool IsInitialized => Status != SubmoduleStatus.Uninitialized;

    /// <summary>
    /// True when the submodule's working tree has uncommitted
    /// modifications, untracked files, or staged changes. Populated
    /// by a parallel per-submodule <c>git status --porcelain</c> at
    /// list-build time and refreshed by the file-watcher dispatch
    /// helper when a file under this submodule's working tree changes.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Status"/> (which is the parent's view
    /// of the submodule pointer). Either dimension can be true on its
    /// own; <see cref="IsDirty"/> ORs them so the sidebar badge lights
    /// up for any kind of dirtiness.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _hasWorkingTreeChanges;

    /// <summary>
    /// True when this submodule needs the user's attention. Combines
    /// pointer-side state (<see cref="SubmoduleStatus.OutOfSync"/>,
    /// <see cref="SubmoduleStatus.Conflicted"/>) with working-tree
    /// state (<see cref="HasWorkingTreeChanges"/>). Drives the amber
    /// "DIRTY" badge in the sidebar.
    /// </summary>
    public bool IsDirty =>
        Status == SubmoduleStatus.OutOfSync ||
        Status == SubmoduleStatus.Conflicted ||
        HasWorkingTreeChanges;

    /// <summary>
    /// Tooltip text for the sidebar entry: the clone URL when one is
    /// configured, otherwise the path. Prevents the empty-tooltip
    /// flicker on submodules with no entry in <c>.gitmodules</c>.
    /// </summary>
    public string TooltipText => string.IsNullOrEmpty(Url) ? Path : Url;
}

/// <summary>
/// Coarse status of a submodule relative to the parent repo's record.
/// Maps to the prefix characters in <c>git submodule status</c> output:
/// <list type="bullet">
///   <item><c>' '</c> → <see cref="UpToDate"/></item>
///   <item><c>'-'</c> → <see cref="Uninitialized"/></item>
///   <item><c>'+'</c> → <see cref="OutOfSync"/></item>
///   <item><c>'U'</c> → <see cref="Conflicted"/></item>
/// </list>
/// </summary>
public enum SubmoduleStatus
{
    /// <summary>
    /// Submodule is registered in <c>.gitmodules</c> but not cloned
    /// yet — <c>git submodule init</c> / <c>update</c> hasn't run.
    /// </summary>
    Uninitialized,

    /// <summary>
    /// Working tree commit matches the commit recorded in the parent.
    /// </summary>
    UpToDate,

    /// <summary>
    /// Working tree is at a different commit than the one recorded in
    /// the parent. Typical after pulling a parent change that advances
    /// the submodule pointer before the user runs <c>submodule update</c>.
    /// </summary>
    OutOfSync,

    /// <summary>
    /// Submodule pointer is in a merge conflict.
    /// </summary>
    Conflicted,
}
