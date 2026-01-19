using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Interface for Git operations.
/// All operations return POCOs and run asynchronously.
/// </summary>
public interface IGitService
{
    event EventHandler<GitCommandEventArgs>? GitCommandExecuted;

    /// <summary>
    /// Check if a path contains a valid Git repository.
    /// </summary>
    Task<bool> IsValidRepositoryAsync(string path);

    /// <summary>
    /// Get commit history for a repository.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="count">Maximum number of commits to retrieve</param>
    /// <param name="branchName">Optional branch name to filter by</param>
    Task<List<CommitInfo>> GetCommitHistoryAsync(string repoPath, int count = 500, string? branchName = null);
    Task<List<CommitInfo>> GetMergeCommitsAsync(string repoPath, string mergeSha);

    /// <summary>
    /// Get details for a specific commit.
    /// </summary>
    Task<CommitInfo?> GetCommitAsync(string repoPath, string sha);

    /// <summary>
    /// Get file changes for a commit.
    /// </summary>
    Task<List<FileChangeInfo>> GetCommitChangesAsync(string repoPath, string sha);

    /// <summary>
    /// Get diff content for a specific file in a commit.
    /// </summary>
    Task<(string oldContent, string newContent)> GetFileDiffAsync(string repoPath, string sha, string filePath);

    /// <summary>
    /// Get diff content for an unstaged file (working directory vs index).
    /// </summary>
    Task<(string oldContent, string newContent)> GetUnstagedFileDiffAsync(string repoPath, string filePath);

    /// <summary>
    /// Get diff content for a staged file (index vs HEAD).
    /// </summary>
    Task<(string oldContent, string newContent)> GetStagedFileDiffAsync(string repoPath, string filePath);

    /// <summary>
    /// Get all branches in the repository.
    /// </summary>
    Task<List<BranchInfo>> GetBranchesAsync(string repoPath);

    /// <summary>
    /// Get all remotes in the repository.
    /// </summary>
    Task<List<RemoteInfo>> GetRemotesAsync(string repoPath);

    /// <summary>
    /// Get repository status information.
    /// </summary>
    Task<RepositoryInfo> GetRepositoryInfoAsync(string repoPath);

    /// <summary>
    /// Clone a remote repository.
    /// </summary>
    Task<string> CloneAsync(string url, string localPath, string? username = null, string? password = null, IProgress<string>? progress = null);

    /// <summary>
    /// Fetch from remote.
    /// </summary>
    Task FetchAsync(string repoPath, string remoteName = "origin", string? username = null, string? password = null, IProgress<string>? progress = null);

    /// <summary>
    /// Pull from remote.
    /// </summary>
    Task PullAsync(string repoPath, string? username = null, string? password = null, IProgress<string>? progress = null);

    /// <summary>
    /// Push to remote.
    /// </summary>
    Task PushAsync(string repoPath, string? username = null, string? password = null, IProgress<string>? progress = null);

    /// <summary>
    /// Pull updates for a specific branch (fast-forward if possible).
    /// </summary>
    Task PullBranchFastForwardAsync(string repoPath, string branchName, string remoteName, string remoteBranchName, bool isCurrentBranch);

    /// <summary>
    /// Push a specific branch to remote.
    /// </summary>
    Task PushBranchAsync(string repoPath, string branchName, string remoteName, string remoteBranchName, bool isCurrentBranch);

    /// <summary>
    /// Set upstream tracking for a branch.
    /// </summary>
    Task SetUpstreamAsync(string repoPath, string branchName, string remoteName, string remoteBranchName);

    /// <summary>
    /// Rename a local branch.
    /// </summary>
    Task RenameBranchAsync(string repoPath, string oldName, string newName);

    /// <summary>
    /// Revert a commit (creates a new commit).
    /// </summary>
    Task RevertCommitAsync(string repoPath, string commitSha);

    /// <summary>
    /// Revert a merge commit using the specified parent index.
    /// </summary>
    Task RevertMergeCommitAsync(string repoPath, string commitSha, int parentIndex);

    /// <summary>
    /// Redo the last undone commit (if available).
    /// </summary>
    Task<bool> RedoCommitAsync(string repoPath);

    /// <summary>
    /// Reset a branch to a specific commit.
    /// </summary>
    Task ResetBranchToCommitAsync(string repoPath, string branchName, string commitSha, bool updateWorkingTree);

    /// <summary>
    /// Checkout a branch.
    /// </summary>
    Task CheckoutAsync(string repoPath, string branchName, bool allowConflicts = false);

    /// <summary>
    /// Checkout a specific commit (detached HEAD).
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="commitSha">SHA of the commit to checkout</param>
    Task CheckoutCommitAsync(string repoPath, string commitSha);

    /// <summary>
    /// Create a new branch.
    /// </summary>
    Task CreateBranchAsync(string repoPath, string branchName, bool checkout = true);

    /// <summary>
    /// Create a new branch at a specific commit.
    /// </summary>
    Task CreateBranchAtCommitAsync(string repoPath, string branchName, string commitSha, bool checkout = true);

    /// <summary>
    /// Cherry-pick a commit onto the current branch.
    /// </summary>
    Task<Models.MergeResult> CherryPickAsync(string repoPath, string commitSha);

    /// <summary>
    /// Get a unified diff between a commit and the working tree.
    /// </summary>
    Task<string> GetCommitToWorkingTreeDiffAsync(string repoPath, string commitSha);

    /// <summary>
    /// Stash changes.
    /// </summary>
    Task StashAsync(string repoPath, string? message = null);

    /// <summary>
    /// Stash only staged changes (requires Git 2.35+).
    /// </summary>
    Task StashStagedAsync(string repoPath, string? message = null);

    /// <summary>
    /// Pop stashed changes.
    /// </summary>
    Task<Models.MergeResult> PopStashAsync(string repoPath);

    /// <summary>
    /// Pop a specific stash by index.
    /// </summary>
    Task<Models.MergeResult> PopStashAsync(string repoPath, int stashIndex);

    /// <summary>
    /// Get all stashes in the repository.
    /// </summary>
    Task<List<StashInfo>> GetStashesAsync(string repoPath);

    /// <summary>
    /// Delete a specific stash by index.
    /// </summary>
    Task DeleteStashAsync(string repoPath, int stashIndex);

    /// <summary>
    /// Clean up any temporary stash created during smart pop operation.
    /// Call this after conflict resolution completes.
    /// </summary>
    Task CleanupTempStashAsync(string repoPath);

    /// <summary>
    /// Undo last commit (soft reset HEAD~1). Only works if not pushed.
    /// </summary>
    Task<bool> UndoCommitAsync(string repoPath);

    /// <summary>
    /// Check if the current HEAD has been pushed to remote.
    /// </summary>
    Task<bool> IsHeadPushedAsync(string repoPath);

    /// <summary>
    /// Search commits by message or SHA.
    /// </summary>
    Task<List<CommitInfo>> SearchCommitsAsync(string repoPath, string searchText, int maxResults = 100);

    /// <summary>
    /// Get working directory changes (staged and unstaged files).
    /// </summary>
    Task<WorkingChangesInfo> GetWorkingChangesAsync(string repoPath);

    /// <summary>
    /// Get the combined diff of staged and unstaged changes.
    /// </summary>
    Task<string> GetWorkingChangesPatchAsync(string repoPath);

    /// <summary>
    /// Get a compact summary of staged changes including diff content.
    /// </summary>
    Task<string> GetStagedSummaryAsync(string repoPath, int maxFiles = 100, int maxDiffChars = 50000);

    /// <summary>
    /// Stage a single file for commit.
    /// </summary>
    Task StageFileAsync(string repoPath, string filePath);

    /// <summary>
    /// Unstage a single file (remove from staging area).
    /// </summary>
    Task UnstageFileAsync(string repoPath, string filePath);

    /// <summary>
    /// Stage all modified files for commit.
    /// </summary>
    Task StageAllAsync(string repoPath);

    /// <summary>
    /// Unstage all files (remove all from staging area).
    /// </summary>
    Task UnstageAllAsync(string repoPath);

    /// <summary>
    /// Discard all working directory changes (destructive - cannot be undone).
    /// </summary>
    Task DiscardAllChangesAsync(string repoPath);

    /// <summary>
    /// Discard changes to a single file.
    /// </summary>
    Task DiscardFileChangesAsync(string repoPath, string filePath);

    /// <summary>
    /// Create a commit with staged files.
    /// </summary>
    /// <param name="repoPath">Path to repository</param>
    /// <param name="message">Commit message (required, max 72 chars recommended)</param>
    /// <param name="description">Optional extended description</param>
    Task CommitAsync(string repoPath, string message, string? description = null);

    /// <summary>
    /// Get list of conflicting files during a merge.
    /// </summary>
    Task<List<ConflictInfo>> GetConflictsAsync(string repoPath);

    /// <summary>
    /// Resolve a conflict by using the current branch version (ours).
    /// </summary>
    Task ResolveConflictWithOursAsync(string repoPath, string filePath);

    /// <summary>
    /// Resolve a conflict by using the incoming branch version (theirs).
    /// </summary>
    Task ResolveConflictWithTheirsAsync(string repoPath, string filePath);

    /// <summary>
    /// Mark a conflict as resolved (after manual edit).
    /// </summary>
    Task MarkConflictResolvedAsync(string repoPath, string filePath);

    Task ReopenConflictAsync(string repoPath, string filePath, string baseContent, string oursContent, string theirsContent);

    Task<List<ConflictInfo>> GetResolvedMergeFilesAsync(string repoPath);

    Task<List<string>> GetStoredMergeConflictFilesAsync(string repoPath);

    Task SaveStoredMergeConflictFilesAsync(string repoPath, IEnumerable<string> files);

    Task ClearStoredMergeConflictFilesAsync(string repoPath);

    /// <summary>
    /// Complete a merge by creating the merge commit.
    /// </summary>
    Task CompleteMergeAsync(string repoPath, string commitMessage);

    /// <summary>
    /// Abort an in-progress merge and return to pre-merge state.
    /// </summary>
    Task AbortMergeAsync(string repoPath);

    /// <summary>
    /// Merge a branch into the current branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to merge</param>
    /// <param name="allowUnrelatedHistories">If true, allows merging branches with no common ancestor</param>
    /// <returns>MergeResult indicating success, conflicts, or failure</returns>
    Task<Models.MergeResult> MergeBranchAsync(string repoPath, string branchName, bool allowUnrelatedHistories = false);

    /// <summary>
    /// Fast-forward the current branch to match a target branch (e.g., origin/main).
    /// Only succeeds if the current branch is strictly behind the target (no divergence).
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="targetBranchName">Name of the branch to fast-forward to (e.g., "origin/main")</param>
    /// <returns>MergeResult indicating success or failure</returns>
    Task<Models.MergeResult> FastForwardAsync(string repoPath, string targetBranchName);

    /// <summary>
    /// Open a conflict in VS Code for resolution.
    /// </summary>
    Task OpenConflictInVsCodeAsync(string repoPath, string filePath);

    #region Branch Deletion

    /// <summary>
    /// Delete a local branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to delete</param>
    /// <param name="force">Force delete even if branch is not fully merged</param>
    Task DeleteBranchAsync(string repoPath, string branchName, bool force = false);

    /// <summary>
    /// Delete a remote branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="remoteName">Name of the remote (e.g., "origin")</param>
    /// <param name="branchName">Name of the branch to delete</param>
    /// <param name="username">Optional username for authentication</param>
    /// <param name="password">Optional password/token for authentication</param>
    Task DeleteRemoteBranchAsync(string repoPath, string remoteName, string branchName,
        string? username = null, string? password = null);

    #endregion

    #region Tag Operations

    /// <summary>
    /// Get all tags in the repository.
    /// </summary>
    Task<List<TagInfo>> GetTagsAsync(string repoPath);

    /// <summary>
    /// Create a new tag.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="tagName">Name of the tag</param>
    /// <param name="message">Optional message for annotated tag (if null, creates lightweight tag)</param>
    /// <param name="targetSha">Optional target commit SHA (defaults to HEAD)</param>
    Task CreateTagAsync(string repoPath, string tagName, string? message = null, string? targetSha = null);

    /// <summary>
    /// Delete a local tag.
    /// </summary>
    Task DeleteTagAsync(string repoPath, string tagName);

    /// <summary>
    /// Push a tag to remote.
    /// </summary>
    Task PushTagAsync(string repoPath, string tagName, string remoteName = "origin",
        string? username = null, string? password = null);

    /// <summary>
    /// Delete a remote tag.
    /// </summary>
    Task DeleteRemoteTagAsync(string repoPath, string tagName, string remoteName = "origin",
        string? username = null, string? password = null);

    #endregion

    #region Rebase Operations

    /// <summary>
    /// Rebase the current branch onto another branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="ontoBranch">Name of the branch to rebase onto</param>
    /// <param name="progress">Optional progress reporter</param>
    /// <returns>Result indicating success, conflicts, or failure</returns>
    Task<Models.MergeResult> RebaseAsync(string repoPath, string ontoBranch, IProgress<string>? progress = null);

    /// <summary>
    /// Abort an in-progress rebase operation.
    /// </summary>
    Task AbortRebaseAsync(string repoPath);

    /// <summary>
    /// Continue a rebase after resolving conflicts.
    /// </summary>
    Task<Models.MergeResult> ContinueRebaseAsync(string repoPath);

    /// <summary>
    /// Skip the current commit during a rebase.
    /// </summary>
    Task<Models.MergeResult> SkipRebaseCommitAsync(string repoPath);

    /// <summary>
    /// Check if a rebase is in progress.
    /// </summary>
    Task<bool> IsRebaseInProgressAsync(string repoPath);

    #endregion

    #region Squash Merge

    /// <summary>
    /// Perform a squash merge of a branch into the current branch.
    /// This stages all changes but does not create a commit.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to squash merge</param>
    /// <returns>Result indicating success, conflicts, or failure</returns>
    Task<Models.MergeResult> SquashMergeAsync(string repoPath, string branchName);

    #endregion

    #region Commit Log

    /// <summary>
    /// Get commits between two references (for changelog generation).
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="fromRef">Starting reference (exclusive)</param>
    /// <param name="toRef">Ending reference (inclusive), defaults to HEAD</param>
    Task<List<CommitInfo>> GetCommitsBetweenAsync(string repoPath, string fromRef, string? toRef = null);

    #endregion
}
