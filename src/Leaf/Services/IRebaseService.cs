using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for rebase operations.
/// </summary>
/// <remarks>
/// This service is stateless - receives IRepositorySession for each operation.
/// All methods are safe to call concurrently for different sessions.
/// </remarks>
public interface IRebaseService
{
    /// <summary>
    /// Rebases the current branch onto another branch.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="ontoBranch">Name of the branch to rebase onto.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <returns>Merge result indicating success, conflicts, or failure.</returns>
    Task<MergeResult> RebaseAsync(
        IRepositorySession session,
        string ontoBranch,
        IProgress<string>? progress = null);

    /// <summary>
    /// Continues an in-progress rebase after resolving conflicts.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <returns>Merge result indicating success, more conflicts, or failure.</returns>
    Task<MergeResult> ContinueRebaseAsync(IRepositorySession session);

    /// <summary>
    /// Skips the current commit in a rebase and continues.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <returns>Merge result indicating success, more conflicts, or failure.</returns>
    Task<MergeResult> SkipRebaseCommitAsync(IRepositorySession session);

    /// <summary>
    /// Aborts an in-progress rebase.
    /// </summary>
    /// <param name="session">Repository session.</param>
    Task AbortRebaseAsync(IRepositorySession session);

    /// <summary>
    /// Checks if a rebase is currently in progress.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <returns>True if a rebase is in progress.</returns>
    Task<bool> IsRebaseInProgressAsync(IRepositorySession session);
}
