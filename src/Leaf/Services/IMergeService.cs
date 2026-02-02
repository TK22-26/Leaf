using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for merge operations.
/// </summary>
/// <remarks>
/// This service is stateless - receives IRepositorySession for each operation.
/// All methods are safe to call concurrently for different sessions.
/// </remarks>
public interface IMergeService
{
    /// <summary>
    /// Merges a branch into the current branch.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="branchName">Name of the branch to merge.</param>
    /// <param name="allowUnrelatedHistories">If true, allows merging unrelated histories.</param>
    /// <returns>Merge result indicating success, conflicts, or failure.</returns>
    Task<MergeResult> MergeBranchAsync(
        IRepositorySession session,
        string branchName,
        bool allowUnrelatedHistories = false);

    /// <summary>
    /// Fast-forwards the current branch to a target branch/commit.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="targetBranchName">Name of the branch to fast-forward to.</param>
    /// <returns>Merge result indicating success or failure.</returns>
    Task<MergeResult> FastForwardAsync(IRepositorySession session, string targetBranchName);

    /// <summary>
    /// Squash merges a branch (combines all commits into staged changes).
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="branchName">Name of the branch to squash merge.</param>
    /// <returns>Merge result indicating success, conflicts, or failure.</returns>
    Task<MergeResult> SquashMergeAsync(IRepositorySession session, string branchName);

    /// <summary>
    /// Completes an in-progress merge by creating the merge commit.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="commitMessage">Message for the merge commit.</param>
    Task CompleteMergeAsync(IRepositorySession session, string commitMessage);

    /// <summary>
    /// Aborts an in-progress merge.
    /// </summary>
    /// <param name="session">Repository session.</param>
    Task AbortMergeAsync(IRepositorySession session);
}
