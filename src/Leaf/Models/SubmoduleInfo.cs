namespace Leaf.Models;

/// <summary>
/// Summary of a single git submodule in a parent repository. Combines
/// registration data from <c>.gitmodules</c> with live state from
/// <c>git submodule status</c>.
/// </summary>
public sealed class SubmoduleInfo
{
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
    /// True when <see cref="Status"/> is <see cref="SubmoduleStatus.OutOfSync"/>
    /// or <see cref="SubmoduleStatus.Conflicted"/>. Drives the amber
    /// "DIRTY" badge in the sidebar.
    /// </summary>
    /// <remarks>
    /// Scope: this reflects the parent's view of the submodule —
    /// "recorded commit differs from checked-out commit" or
    /// "merge conflict on the submodule pointer". It does <b>not</b>
    /// cover uncommitted modifications inside the submodule's own
    /// working tree; detecting those requires an extra per-submodule
    /// git call (<c>git status</c> inside each one) that Phase 1
    /// deliberately skips to keep sidebar refresh cheap.
    /// </remarks>
    public bool IsDirty =>
        Status == SubmoduleStatus.OutOfSync ||
        Status == SubmoduleStatus.Conflicted;

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
