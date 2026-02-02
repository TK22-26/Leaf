using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for commit operations (create, revert, undo).
/// </summary>
/// <remarks>
/// This service is stateless - receives IRepositorySession for each operation.
/// All methods are safe to call concurrently for different sessions.
/// </remarks>
public interface ICommitOperationService
{
    /// <summary>
    /// Creates a new commit with staged changes.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="message">Commit message.</param>
    Task CommitAsync(IRepositorySession session, string message);

    /// <summary>
    /// Cherry-picks a commit onto the current branch.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="commitSha">SHA of the commit to cherry-pick.</param>
    /// <returns>Merge result indicating success or conflicts.</returns>
    Task<MergeResult> CherryPickAsync(IRepositorySession session, string commitSha);

    /// <summary>
    /// Reverts a commit by creating a new commit that undoes its changes.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="commitSha">SHA of the commit to revert.</param>
    Task RevertCommitAsync(IRepositorySession session, string commitSha);

    /// <summary>
    /// Reverts a merge commit by creating a new commit that undoes its changes.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="commitSha">SHA of the merge commit to revert.</param>
    /// <param name="parentIndex">Which parent to revert to (1 for first parent, typically mainline).</param>
    Task RevertMergeCommitAsync(IRepositorySession session, string commitSha, int parentIndex);

    /// <summary>
    /// Undoes the last commit (soft reset to HEAD~1).
    /// Only works if the commit hasn't been pushed.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <returns>True if undo was successful, false if commit was already pushed.</returns>
    Task<bool> UndoCommitAsync(IRepositorySession session);

    /// <summary>
    /// Redoes a previously undone commit.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <returns>True if redo was successful.</returns>
    Task<bool> RedoCommitAsync(IRepositorySession session);
}
