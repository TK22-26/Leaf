namespace Leaf.Services.RepoTree;

/// <summary>
/// One repository in a submodule tree. Produced by
/// <see cref="IRepoTreeService.GetTreeAsync"/> in post-order (deepest
/// submodules first, root last) — the order every tree-wide write
/// operation must follow so parent commits record child SHAs that
/// already exist.
/// </summary>
/// <param name="Path">Absolute working-tree path.</param>
/// <param name="RelativePath">Path relative to the tree root, forward slashes; <c>"."</c> for the root itself.</param>
/// <param name="ParentPath">Absolute path of the immediate parent repo; null for the root.</param>
/// <param name="Depth">0 for the root, 1 for its direct submodules, and so on.</param>
/// <param name="IsInitialized">False when the submodule is registered but not cloned. Uninitialized nodes are never recursed into and never written to.</param>
public sealed record RepoNode(
    string Path,
    string RelativePath,
    string? ParentPath,
    int Depth,
    bool IsInitialized);

/// <summary>Per-repo outcome of a tree-wide write operation.</summary>
public enum TreeOpOutcome
{
    /// <summary>The operation ran and succeeded in this repo.</summary>
    Succeeded,

    /// <summary>Nothing to do — the working tree was clean.</summary>
    SkippedClean,

    /// <summary>The repo has no remote configured; push/pull/fetch have no target. Not a failure.</summary>
    SkippedNoRemote,

    /// <summary>The submodule is registered but not cloned; it is never written to.</summary>
    SkippedUninitialized,

    /// <summary>
    /// The repo is on a detached HEAD (the default state of an
    /// initialized submodule) with nothing unpublished — push has no
    /// branch to publish and pull has no upstream. Not a failure.
    /// A detached HEAD with UNPUBLISHED commits is reported as
    /// <see cref="Failed"/> instead: publishing an ancestor would record
    /// gitlinks to objects no remote has.
    /// </summary>
    SkippedDetachedHead,

    /// <summary>
    /// A descendant repo failed, so running the operation here would
    /// record or publish pointers the run could not produce (e.g.
    /// pushing a parent whose submodule push failed would dangle its
    /// gitlink references on the remote).
    /// </summary>
    SkippedChildFailed,

    /// <summary>The operation ran and failed in this repo. See <see cref="TreeOpEntry.Detail"/>.</summary>
    Failed,
}

/// <summary>Outcome of one repo within a tree-wide write operation.</summary>
/// <param name="RelativePath">Repo path relative to the tree root; <c>"."</c> for the root.</param>
/// <param name="Outcome">What happened in this repo.</param>
/// <param name="Detail">Human-readable detail — git stderr for failures, skip reasons otherwise. Null on plain success.</param>
/// <param name="CommitSha">SHA of the commit created here, when the operation was a commit that succeeded.</param>
public sealed record TreeOpEntry(
    string RelativePath,
    TreeOpOutcome Outcome,
    string? Detail,
    string? CommitSha);

/// <summary>Aggregate result of a tree-wide write operation.</summary>
/// <param name="AllSucceeded">True when no entry is <see cref="TreeOpOutcome.Failed"/> or <see cref="TreeOpOutcome.SkippedChildFailed"/>.</param>
/// <param name="Entries">Per-repo outcomes in the order the repos were processed (post-order for ordered ops).</param>
public sealed record TreeOpResult(
    bool AllSucceeded,
    IReadOnlyList<TreeOpEntry> Entries);

/// <summary>Progress callback payload for tree-wide operations.</summary>
/// <param name="Node">The repo currently being processed.</param>
/// <param name="Phase">Short verb, e.g. <c>"committing"</c>, <c>"pushing"</c>, <c>"pulling"</c>, <c>"fetching"</c>.</param>
public sealed record TreeOpProgress(RepoNode Node, string Phase);

/// <summary>Options for <see cref="IRepoTreeService.CommitTreeAsync"/>.</summary>
public sealed class TreeCommitOptions
{
    /// <summary>
    /// Supplies the commit message (and optional description) for a
    /// dirty repo. Called AFTER submodule pointer paths from this run
    /// have been staged, so the provider can inspect the final staged
    /// diff. Returning null marks that repo <see cref="TreeOpOutcome.Failed"/>
    /// — a dirty repo without a message is a loud per-repo failure,
    /// never a silent skip.
    /// </summary>
    public required Func<RepoNode, CancellationToken, Task<(string Message, string? Description)?>> MessageProvider { get; init; }

    /// <summary>
    /// When true (default), unstaged changes are staged before the
    /// commit. When false the commit records only what is already in
    /// the index (plus submodule pointer bumps from this run, which are
    /// always staged).
    /// </summary>
    public bool StageAll { get; init; } = true;
}

/// <summary>A changed file inside one repo of a tree status snapshot.</summary>
/// <param name="Path">Path relative to that repo's root, forward slashes.</param>
/// <param name="Status">Change kind, e.g. <c>Modified</c>, <c>Added</c>, <c>Deleted</c>, <c>Untracked</c>, <c>Renamed</c>, <c>Conflicted</c>.</param>
public sealed record RepoFileEntry(string Path, string Status);

/// <summary>A submodule whose checked-out SHA differs from the SHA recorded in its parent.</summary>
/// <param name="Path">Submodule path relative to its parent repo.</param>
/// <param name="RecordedSha">SHA the parent's index/tree records.</param>
/// <param name="WorkingSha">SHA currently checked out; null when uninitialized.</param>
/// <param name="Status">Parent's view of the pointer: <c>UpToDate</c>, <c>OutOfSync</c>, <c>Conflicted</c>, <c>Uninitialized</c>.</param>
public sealed record SubmodulePointerChange(
    string Path,
    string RecordedSha,
    string? WorkingSha,
    string Status);

/// <summary>Status of one repo within a tree status snapshot.</summary>
public sealed record RepoStatusEntry(
    string RelativePath,
    bool IsInitialized,
    string? Branch,
    bool IsDetachedHead,
    int AheadBy,
    int BehindBy,
    IReadOnlyList<RepoFileEntry> StagedFiles,
    IReadOnlyList<RepoFileEntry> UnstagedFiles,
    bool FilesTruncated,
    bool MergeInProgress,
    IReadOnlyList<SubmodulePointerChange> SubmodulePointerChanges)
{
    /// <summary>True when this repo has staged or unstaged changes.</summary>
    public bool IsDirty => StagedFiles.Count > 0 || UnstagedFiles.Count > 0;
}

/// <summary>Whole-tree status snapshot: the root repo and every (nested) submodule.</summary>
/// <param name="RootPath">Absolute path of the tree root.</param>
/// <param name="Repos">Per-repo status in post-order (deepest first, root last).</param>
public sealed record RepoTreeStatus(
    string RootPath,
    IReadOnlyList<RepoStatusEntry> Repos)
{
    /// <summary>Number of repos with uncommitted changes.</summary>
    public int DirtyCount => Repos.Count(r => r.IsDirty);

    /// <summary>Number of repos with commits not yet pushed to their upstream.</summary>
    public int UnpushedCount => Repos.Count(r => r.AheadBy > 0);
}
