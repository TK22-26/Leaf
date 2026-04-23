#nullable enable
using Leaf.Models;

namespace Leaf.Services.Merge;

/// <summary>
/// Per-line blame lookup for the merge editor's C5 hover-peek popover.
/// Hides two concerns the raw <see cref="IGitService.GetFileBlameAsync"/>
/// call can't handle cheaply on its own:
/// </summary>
/// <remarks>
/// <para>
/// <b>Process churn.</b> Each pane's <c>MouseMove</c> triggers a potential
/// lookup after a 500 ms debounce; without the service's cache, hovering a
/// 1000-line file would spawn a new <c>git blame</c> subprocess for every
/// unique line the pointer settles on. The service runs the full blame
/// once per file+HEAD-sha pair and serves subsequent per-line requests
/// from an in-memory dictionary.
/// </para>
/// <para>
/// <b>HEAD invalidation.</b> After a fetch / pull / reset / merge the
/// blame output for a file may change. Keying the cache on the repository's
/// current HEAD sha lets stale entries sit in memory until GC without the
/// service ever serving an outdated record; the next lookup hashes the
/// new sha, misses, and refreshes.
/// </para>
/// </remarks>
public interface IMergeBlameService
{
    /// <summary>
    /// Return the blame record for <paramref name="oneBasedLineNumber"/> in
    /// <paramref name="filePath"/>. Returns <c>null</c> when git blame has
    /// no entry for that line (e.g. line is past the file's current length,
    /// or the file is untracked). Throws only for unrecoverable failures
    /// (the git subprocess returned a non-success exit and the error is
    /// not a transient cancellation).
    /// </summary>
    /// <param name="repoPath">Repository root. Used both to invoke git and to key the cache.</param>
    /// <param name="filePath">Repo-relative or absolute path to the conflict file.</param>
    /// <param name="oneBasedLineNumber">1-based line index matching git blame's output.</param>
    /// <param name="cancellationToken">Cancellation fires when the user moves the pointer off the line before the 500 ms debounce elapses.</param>
    Task<FileBlameLine?> GetLineBlameAsync(
        string repoPath,
        string filePath,
        int oneBasedLineNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicit cache invalidation. Called by <see cref="IRepositoryEventHub"/>
    /// subscribers on fetch / pull / reset / merge so the next hover
    /// refreshes instead of serving pre-ref-update data. HEAD-sha keying
    /// guards against missed invalidations but this keeps the memory
    /// footprint from growing across long sessions.
    /// </summary>
    void InvalidateRepo(string repoPath);
}
