using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for managing git branches.
/// </summary>
/// <remarks>
/// This service is stateless - receives IRepositorySession for each operation.
/// All methods are safe to call concurrently for different sessions.
/// </remarks>
public interface IBranchService
{
    /// <summary>
    /// Gets all branches (local and remote) in the repository.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <returns>List of branch information.</returns>
    Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(IRepositorySession session);

    /// <summary>
    /// Creates a new branch at HEAD.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="branchName">Name for the new branch.</param>
    /// <param name="checkout">Whether to checkout the new branch after creation.</param>
    Task CreateBranchAsync(IRepositorySession session, string branchName, bool checkout = true);

    /// <summary>
    /// Creates a new branch at a specific commit.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="branchName">Name for the new branch.</param>
    /// <param name="commitSha">SHA of the commit to branch from.</param>
    /// <param name="checkout">Whether to checkout the new branch after creation.</param>
    Task CreateBranchAtCommitAsync(IRepositorySession session, string branchName, string commitSha, bool checkout = true);

    /// <summary>
    /// Checks out a branch.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="branchName">Name of the branch to checkout.</param>
    /// <param name="allowConflicts">If true, allows checkout even with local changes that might conflict.</param>
    Task CheckoutAsync(IRepositorySession session, string branchName, bool allowConflicts = false);

    /// <summary>
    /// Checks out a specific commit (detached HEAD state).
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="commitSha">SHA of the commit to checkout.</param>
    Task CheckoutCommitAsync(IRepositorySession session, string commitSha);

    /// <summary>
    /// Renames a branch.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="oldName">Current branch name.</param>
    /// <param name="newName">New branch name.</param>
    Task RenameBranchAsync(IRepositorySession session, string oldName, string newName);

    /// <summary>
    /// Deletes a local branch.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="branchName">Name of the branch to delete.</param>
    /// <param name="force">If true, force delete even if not fully merged.</param>
    Task DeleteBranchAsync(IRepositorySession session, string branchName, bool force = false);

    /// <summary>
    /// Deletes a remote branch.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="remoteName">Name of the remote (e.g., "origin").</param>
    /// <param name="branchName">Name of the branch to delete.</param>
    /// <param name="username">Optional username for authentication.</param>
    /// <param name="password">Optional password/token for authentication.</param>
    Task DeleteRemoteBranchAsync(
        IRepositorySession session,
        string remoteName,
        string branchName,
        string? username = null,
        string? password = null);

    /// <summary>
    /// Sets the upstream tracking branch for a local branch.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="branchName">Local branch name.</param>
    /// <param name="remoteName">Remote name (e.g., "origin").</param>
    /// <param name="remoteBranchName">Remote branch name.</param>
    Task SetUpstreamAsync(IRepositorySession session, string branchName, string remoteName, string remoteBranchName);

    /// <summary>
    /// Pulls changes for a specific branch using fast-forward only.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="branchName">Local branch name.</param>
    /// <param name="remoteName">Remote name.</param>
    /// <param name="remoteBranchName">Remote branch name.</param>
    /// <param name="isCurrentBranch">Whether this is the currently checked out branch.</param>
    Task PullBranchFastForwardAsync(
        IRepositorySession session,
        string branchName,
        string remoteName,
        string remoteBranchName,
        bool isCurrentBranch);

    /// <summary>
    /// Pushes a specific branch to remote.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="branchName">Local branch name.</param>
    /// <param name="remoteName">Remote name.</param>
    /// <param name="remoteBranchName">Remote branch name.</param>
    /// <param name="isCurrentBranch">Whether this is the currently checked out branch.</param>
    Task PushBranchAsync(
        IRepositorySession session,
        string branchName,
        string remoteName,
        string remoteBranchName,
        bool isCurrentBranch);

    /// <summary>
    /// Resets a branch to a specific commit.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="branchName">Branch name to reset.</param>
    /// <param name="commitSha">Target commit SHA.</param>
    /// <param name="updateWorkingTree">If true, also updates the working tree (hard reset).</param>
    Task ResetBranchToCommitAsync(
        IRepositorySession session,
        string branchName,
        string commitSha,
        bool updateWorkingTree);
}
