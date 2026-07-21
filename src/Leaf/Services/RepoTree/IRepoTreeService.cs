namespace Leaf.Services.RepoTree;

/// <summary>
/// Headless operations over a repository tree: a root repo plus every
/// (nested) submodule. This is the single implementation of the
/// ordering rules the workspace grid and the MCP server both depend
/// on — submodules commit and push before their parent, parents stage
/// the new gitlink SHAs before committing, and a parent write is
/// skipped when a descendant failed so no dangling pointers are ever
/// recorded or published.
/// </summary>
/// <remarks>
/// Every method takes explicit paths and returns POCOs; nothing here
/// touches WPF. All git access goes through <see cref="IGitService"/>
/// (git CLI underneath), so concurrent use alongside a running Leaf
/// GUI is coordinated by git's own <c>index.lock</c> — collisions fail
/// loudly with git's message rather than being retried.
/// </remarks>
public interface IRepoTreeService
{
    /// <summary>
    /// Enumerate the repository tree rooted at <paramref name="rootPath"/>
    /// in post-order: deepest submodules first, the root last. This is
    /// the write order. Uninitialized submodules appear as nodes
    /// (<see cref="RepoNode.IsInitialized"/> false) but are not recursed
    /// into. Throws when a submodule cycle or a nesting depth over 8 is
    /// detected.
    /// </summary>
    Task<IReadOnlyList<RepoNode>> GetTreeAsync(string rootPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Status of every repo in the tree in one call: branch,
    /// ahead/behind, staged/unstaged files, merge-in-progress, and
    /// submodule pointer drift.
    /// </summary>
    Task<RepoTreeStatus> GetTreeStatusAsync(string rootPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commit every dirty repo in the tree, submodules first. After a
    /// repo's direct children commit, their pointer paths are staged in
    /// that repo so its commit records the new gitlink SHAs. A repo
    /// whose descendant failed is skipped (<see cref="TreeOpOutcome.SkippedChildFailed"/>);
    /// a dirty repo whose <see cref="TreeCommitOptions.MessageProvider"/>
    /// returns null fails loudly.
    /// </summary>
    Task<TreeOpResult> CommitTreeAsync(
        string rootPath,
        TreeCommitOptions options,
        IProgress<TreeOpProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Push every repo in the tree, submodules first. A repo whose
    /// descendant push failed is skipped — publishing the parent would
    /// dangle its submodule references on the remote. Repos without a
    /// remote are skipped as <see cref="TreeOpOutcome.SkippedNoRemote"/>.
    /// </summary>
    Task<TreeOpResult> PushTreeAsync(
        string rootPath,
        IProgress<TreeOpProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pull every repo in the tree in parallel (throttled). A conflicted
    /// pull surfaces as <see cref="TreeOpOutcome.Failed"/> with git's
    /// stderr; the repo is left in its merge state for the user to
    /// resolve, exactly as a single-repo pull would.
    /// </summary>
    Task<TreeOpResult> PullTreeAsync(
        string rootPath,
        IProgress<TreeOpProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Fetch every repo in the tree in parallel (throttled).</summary>
    Task<TreeOpResult> FetchTreeAsync(
        string rootPath,
        IProgress<TreeOpProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stage the given submodule paths (relative to
    /// <paramref name="parentRepoPath"/>, forward slashes) in the parent's
    /// index so its next commit records the submodules' current SHAs.
    /// </summary>
    Task StageSubmodulePointersAsync(
        string parentRepoPath,
        IEnumerable<string> submoduleRelativePaths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve the outermost enclosing repository for any path inside a
    /// working tree: walks up to the repo root, then follows the
    /// superproject chain until there is no parent. Throws when the
    /// path is not inside a git working tree.
    /// </summary>
    Task<string> ResolveTreeRootAsync(string anyPathInsideTree, CancellationToken cancellationToken = default);
}
