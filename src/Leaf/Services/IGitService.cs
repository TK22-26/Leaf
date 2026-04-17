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
    Task<bool> IsValidRepositoryAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get commit history for a repository.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="count">Maximum number of commits to retrieve</param>
    /// <param name="branchName">Optional branch name to filter by</param>
    /// <param name="skip">Number of commits to skip (for lazy loading)</param>
    Task<List<CommitInfo>> GetCommitHistoryAsync(string repoPath, int count = 500, string? branchName = null, int skip = 0, CancellationToken cancellationToken = default);
    Task<List<CommitInfo>> GetMergeCommitsAsync(string repoPath, string mergeSha, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get details for a specific commit.
    /// </summary>
    Task<CommitInfo?> GetCommitAsync(string repoPath, string sha, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get file changes for a commit.
    /// </summary>
    Task<List<FileChangeInfo>> GetCommitChangesAsync(string repoPath, string sha, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all files in the repository at a given commit, with changed files marked with their status.
    /// </summary>
    Task<List<FileChangeInfo>> GetCommitAllFilesAsync(string repoPath, string sha, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get diff content for a specific file in a commit.
    /// </summary>
    Task<(string oldContent, string newContent)> GetFileDiffAsync(string repoPath, string sha, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get diff content for an unstaged file (working directory vs index).
    /// </summary>
    Task<(string oldContent, string newContent)> GetUnstagedFileDiffAsync(string repoPath, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get diff content for a staged file (index vs HEAD).
    /// </summary>
    Task<(string oldContent, string newContent)> GetStagedFileDiffAsync(string repoPath, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all branches in the repository.
    /// </summary>
    Task<List<BranchInfo>> GetBranchesAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all remotes in the repository.
    /// </summary>
    Task<List<RemoteInfo>> GetRemotesAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new remote to the repository.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="remoteName">Name for the new remote</param>
    /// <param name="url">Fetch URL for the remote</param>
    /// <param name="pushUrl">Optional separate push URL</param>
    Task AddRemoteAsync(string repoPath, string remoteName, string url, string? pushUrl = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a remote from the repository.
    /// </summary>
    Task RemoveRemoteAsync(string repoPath, string remoteName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rename a remote.
    /// </summary>
    Task RenameRemoteAsync(string repoPath, string oldName, string newName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set a remote's URL.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="remoteName">Name of the remote</param>
    /// <param name="url">New URL</param>
    /// <param name="isPushUrl">If true, sets the push URL; otherwise sets the fetch URL</param>
    Task SetRemoteUrlAsync(string repoPath, string remoteName, string url, bool isPushUrl = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set a git config value.
    /// </summary>
    Task SetConfigAsync(string repoPath, string key, string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a git config value.
    /// </summary>
    Task<string?> GetConfigAsync(string repoPath, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get repository status information.
    /// </summary>
    Task<RepositoryInfo> GetRepositoryInfoAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get repository status information using fast git CLI commands.
    /// Prefer this over <see cref="GetRepositoryInfoAsync"/> for performance-critical paths.
    /// </summary>
    Task<RepositoryInfo> GetRepositoryInfoFastAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clone a remote repository.
    /// </summary>
    /// <param name="credentialKey">Optional credential storage key (e.g. "GitHub:microsoft") for GIT_ASKPASS auth.</param>
    Task<string> CloneAsync(string url, string localPath, string? credentialKey = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch from remote.
    /// </summary>
    /// <param name="credentialKey">Optional credential storage key (e.g. "GitHub:microsoft") for GIT_ASKPASS auth.</param>
    Task FetchAsync(string repoPath, string remoteName = "origin", string? credentialKey = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pull from remote.
    /// </summary>
    /// <param name="credentialKey">Optional credential storage key (e.g. "GitHub:microsoft") for GIT_ASKPASS auth.</param>
    Task PullAsync(string repoPath, string? credentialKey = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Push to remote.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="remoteName">Optional remote name (uses tracking branch's remote or default if not specified)</param>
    /// <param name="credentialKey">Optional credential storage key (e.g. "GitHub:microsoft") for GIT_ASKPASS auth.</param>
    /// <param name="progress">Optional progress reporter</param>
    Task PushAsync(string repoPath, string? remoteName = null, string? credentialKey = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pull updates for a specific branch (fast-forward if possible).
    /// </summary>
    Task PullBranchFastForwardAsync(string repoPath, string branchName, string remoteName, string remoteBranchName, bool isCurrentBranch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Push a specific branch to remote.
    /// </summary>
    Task PushBranchAsync(string repoPath, string branchName, string remoteName, string remoteBranchName, bool isCurrentBranch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set upstream tracking for a branch.
    /// </summary>
    Task SetUpstreamAsync(string repoPath, string branchName, string remoteName, string remoteBranchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rename a local branch.
    /// </summary>
    Task RenameBranchAsync(string repoPath, string oldName, string newName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revert a commit (creates a new commit).
    /// </summary>
    Task RevertCommitAsync(string repoPath, string commitSha, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revert a merge commit using the specified parent index.
    /// </summary>
    Task RevertMergeCommitAsync(string repoPath, string commitSha, int parentIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redo the last undone commit (if available).
    /// </summary>
    Task<bool> RedoCommitAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reset the current branch to a specific commit.
    /// </summary>
    Task ResetCurrentBranchToCommitAsync(string repoPath, string commitSha, GitResetMode mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checkout a branch.
    /// </summary>
    Task CheckoutAsync(string repoPath, string branchName, bool allowConflicts = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checkout a specific commit (detached HEAD).
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="commitSha">SHA of the commit to checkout</param>
    Task CheckoutCommitAsync(string repoPath, string commitSha, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new branch.
    /// </summary>
    Task CreateBranchAsync(string repoPath, string branchName, bool checkout = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new branch at a specific commit.
    /// </summary>
    Task CreateBranchAtCommitAsync(string repoPath, string branchName, string commitSha, bool checkout = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cherry-pick a commit onto the current branch.
    /// </summary>
    Task<Models.MergeResult> CherryPickAsync(string repoPath, string commitSha, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a unified diff between a commit and the working tree.
    /// </summary>
    Task<string> GetCommitToWorkingTreeDiffAsync(string repoPath, string commitSha, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a unified diff between two refs.
    /// Uses three-dot diff semantics so the diff is computed from the merge base to <paramref name="headRef"/>.
    /// </summary>
    Task<string> GetRefToRefDiffAsync(string repoPath, string baseRef, string headRef, string? filePath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stash changes.
    /// </summary>
    Task StashAsync(string repoPath, string? message = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stash only staged changes (requires Git 2.35+).
    /// </summary>
    Task StashStagedAsync(string repoPath, string? message = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pop stashed changes.
    /// </summary>
    Task<Models.MergeResult> PopStashAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pop a specific stash by index.
    /// </summary>
    Task<Models.MergeResult> PopStashAsync(string repoPath, int stashIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all stashes in the repository.
    /// </summary>
    Task<List<StashInfo>> GetStashesAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a specific stash by index.
    /// </summary>
    Task DeleteStashAsync(string repoPath, int stashIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clean up any temporary stash created during smart pop operation.
    /// Call this after conflict resolution completes.
    /// </summary>
    Task CleanupTempStashAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Undo last commit (soft reset HEAD~1). Only works if not pushed.
    /// </summary>
    Task<bool> UndoCommitAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the current HEAD has been pushed to remote.
    /// </summary>
    Task<bool> IsHeadPushedAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Search commits by message or SHA.
    /// </summary>
    Task<List<CommitInfo>> SearchCommitsAsync(string repoPath, string searchText, int maxResults = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get working directory changes (staged and unstaged files).
    /// </summary>
    Task<WorkingChangesInfo> GetWorkingChangesAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the combined diff of staged and unstaged changes.
    /// </summary>
    Task<string> GetWorkingChangesPatchAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a compact summary of staged changes including diff content.
    /// </summary>
    Task<string> GetStagedSummaryAsync(string repoPath, int maxFiles = 100, int maxDiffChars = 50000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stage a single file for commit.
    /// </summary>
    Task StageFileAsync(string repoPath, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unstage a single file (remove from staging area).
    /// </summary>
    Task UnstageFileAsync(string repoPath, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a tracked file from the index (git rm --cached) without deleting it from disk.
    /// </summary>
    Task UntrackFileAsync(string repoPath, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stage all modified files for commit.
    /// </summary>
    Task StageAllAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unstage all files (remove all from staging area).
    /// </summary>
    Task UnstageAllAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discard all working directory changes (destructive - cannot be undone).
    /// </summary>
    Task DiscardAllChangesAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discard changes to a single file.
    /// </summary>
    Task DiscardFileChangesAsync(string repoPath, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a commit with staged files.
    /// </summary>
    /// <param name="repoPath">Path to repository</param>
    /// <param name="message">Commit message (required, max 72 chars recommended)</param>
    /// <param name="description">Optional extended description</param>
    /// <param name="amend">
    /// If true, replace the current HEAD commit with a new one containing
    /// the staged changes and the supplied message. Caller is responsible
    /// for gating this on <see cref="IsHeadPushedAsync"/> — amending a
    /// published commit rewrites history.
    /// </param>
    Task CommitAsync(string repoPath, string message, string? description = null, bool amend = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the commit at HEAD, or null if the repository is in an
    /// unborn/empty state. Convenience for flows like commit amend that
    /// need HEAD's current message without separately resolving its SHA.
    /// </summary>
    Task<CommitInfo?> GetHeadCommitAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get list of conflicting files during a merge.
    /// </summary>
    Task<List<ConflictInfo>> GetConflictsAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve a conflict by using the current branch version (ours).
    /// </summary>
    Task ResolveConflictWithOursAsync(string repoPath, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve a conflict by using the incoming branch version (theirs).
    /// </summary>
    Task ResolveConflictWithTheirsAsync(string repoPath, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark a conflict as resolved (after manual edit).
    /// </summary>
    Task MarkConflictResolvedAsync(string repoPath, string filePath, CancellationToken cancellationToken = default);

    Task ReopenConflictAsync(string repoPath, string filePath, string baseContent, string oursContent, string theirsContent, CancellationToken cancellationToken = default);

    Task<List<ConflictInfo>> GetResolvedMergeFilesAsync(string repoPath, CancellationToken cancellationToken = default);

    Task<List<string>> GetStoredMergeConflictFilesAsync(string repoPath, CancellationToken cancellationToken = default);

    Task SaveStoredMergeConflictFilesAsync(string repoPath, IEnumerable<string> files, CancellationToken cancellationToken = default);

    Task ClearStoredMergeConflictFilesAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Complete a merge by creating the merge commit.
    /// </summary>
    Task CompleteMergeAsync(string repoPath, string commitMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Abort an in-progress merge and return to pre-merge state.
    /// </summary>
    Task AbortMergeAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Abort an in-progress cherry-pick.
    /// </summary>
    Task AbortCherryPickAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Abort an in-progress revert.
    /// </summary>
    Task AbortRevertAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the repository is in an "orphaned conflict" state.
    /// This occurs when the index has unmerged entries but MERGE_HEAD doesn't exist.
    /// </summary>
    Task<bool> IsOrphanedConflictStateAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reset the index to clear orphaned conflict state.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="discardWorkingChanges">If true, also discards all working directory changes</param>
    Task ResetOrphanedConflictsAsync(string repoPath, bool discardWorkingChanges, CancellationToken cancellationToken = default);

    /// <summary>
    /// Merge a branch into the current branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to merge</param>
    /// <param name="allowUnrelatedHistories">If true, allows merging branches with no common ancestor</param>
    /// <returns>MergeResult indicating success, conflicts, or failure</returns>
    Task<Models.MergeResult> MergeBranchAsync(string repoPath, string branchName, bool allowUnrelatedHistories = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fast-forward the current branch to match a target branch (e.g., origin/main).
    /// Only succeeds if the current branch is strictly behind the target (no divergence).
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="targetBranchName">Name of the branch to fast-forward to (e.g., "origin/main")</param>
    /// <returns>MergeResult indicating success or failure</returns>
    Task<Models.MergeResult> FastForwardAsync(string repoPath, string targetBranchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Open a conflict in VS Code for resolution.
    /// </summary>
    Task OpenConflictInVsCodeAsync(string repoPath, string filePath, CancellationToken cancellationToken = default);

    #region Branch Deletion

    /// <summary>
    /// Delete a local branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to delete</param>
    /// <param name="force">Force delete even if branch is not fully merged</param>
    Task DeleteBranchAsync(string repoPath, string branchName, bool force = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a remote branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="remoteName">Name of the remote (e.g., "origin")</param>
    /// <param name="branchName">Name of the branch to delete</param>
    /// <param name="credentialKey">Optional credential storage key for GIT_ASKPASS auth.</param>
    Task DeleteRemoteBranchAsync(string repoPath, string remoteName, string branchName,
        string? credentialKey = null, CancellationToken cancellationToken = default);

    #endregion

    #region Tag Operations

    /// <summary>
    /// Get all tags in the repository.
    /// </summary>
    Task<List<TagInfo>> GetTagsAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new tag.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="tagName">Name of the tag</param>
    /// <param name="message">Optional message for annotated tag (if null, creates lightweight tag)</param>
    /// <param name="targetSha">Optional target commit SHA (defaults to HEAD)</param>
    Task CreateTagAsync(string repoPath, string tagName, string? message = null, string? targetSha = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a local tag.
    /// </summary>
    Task DeleteTagAsync(string repoPath, string tagName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Push a tag to remote.
    /// </summary>
    /// <param name="credentialKey">Optional credential storage key for GIT_ASKPASS auth.</param>
    Task PushTagAsync(string repoPath, string tagName, string remoteName = "origin",
        string? credentialKey = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a remote tag.
    /// </summary>
    /// <param name="credentialKey">Optional credential storage key for GIT_ASKPASS auth.</param>
    Task DeleteRemoteTagAsync(string repoPath, string tagName, string remoteName = "origin",
        string? credentialKey = null, CancellationToken cancellationToken = default);

    #endregion

    #region Rebase Operations

    /// <summary>
    /// Rebase the current branch onto another branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="ontoBranch">Name of the branch to rebase onto</param>
    /// <param name="progress">Optional progress reporter</param>
    /// <returns>Result indicating success, conflicts, or failure</returns>
    Task<Models.MergeResult> RebaseAsync(string repoPath, string ontoBranch, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Abort an in-progress rebase operation.
    /// </summary>
    Task AbortRebaseAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Continue a rebase after resolving conflicts.
    /// </summary>
    Task<Models.MergeResult> ContinueRebaseAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Skip the current commit during a rebase.
    /// </summary>
    Task<Models.MergeResult> SkipRebaseCommitAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a rebase is in progress.
    /// </summary>
    Task<bool> IsRebaseInProgressAsync(string repoPath, CancellationToken cancellationToken = default);

    #endregion

    #region Squash Merge

    /// <summary>
    /// Perform a squash merge of a branch into the current branch.
    /// This stages all changes but does not create a commit.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to squash merge</param>
    /// <returns>Result indicating success, conflicts, or failure</returns>
    Task<Models.MergeResult> SquashMergeAsync(string repoPath, string branchName, CancellationToken cancellationToken = default);

    #endregion

    #region Commit Log

    /// <summary>
    /// Get commits between two references (for changelog generation).
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="fromRef">Starting reference (exclusive)</param>
    /// <param name="toRef">Ending reference (inclusive), defaults to HEAD</param>
    Task<List<CommitInfo>> GetCommitsBetweenAsync(string repoPath, string fromRef, string? toRef = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get blame information for a file.
    /// </summary>
    Task<List<FileBlameLine>> GetFileBlameAsync(string repoPath, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get history for a file.
    /// </summary>
    Task<List<CommitInfo>> GetFileHistoryAsync(string repoPath, string filePath, int maxCount = 200, CancellationToken cancellationToken = default);

    #endregion

    #region Hunk Operations

    /// <summary>
    /// Revert a single hunk by applying a reverse patch.
    /// </summary>
    /// <param name="repoPath">Path to the repository.</param>
    /// <param name="patchContent">The unified diff patch content (will be applied with --reverse).</param>
    Task RevertHunkAsync(string repoPath, string patchContent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stage a single hunk by applying a patch to the index.
    /// </summary>
    /// <param name="repoPath">Path to the repository.</param>
    /// <param name="patchContent">The unified diff patch content.</param>
    Task StageHunkAsync(string repoPath, string patchContent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unstage a single hunk by applying a reverse patch to the index.
    /// </summary>
    /// <param name="repoPath">Path to the repository.</param>
    /// <param name="patchContent">The unified diff patch content (will be applied with --reverse to index).</param>
    Task UnstageHunkAsync(string repoPath, string patchContent, CancellationToken cancellationToken = default);

    #endregion

    #region Worktree Operations

    /// <summary>
    /// Get all worktrees for the repository.
    /// </summary>
    Task<List<WorktreeInfo>> GetWorktreesAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new worktree for an existing branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="worktreePath">Path where the worktree will be created</param>
    /// <param name="branchName">Name of the branch to check out in the worktree</param>
    Task CreateWorktreeAsync(string repoPath, string worktreePath, string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new worktree with a new branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="worktreePath">Path where the worktree will be created</param>
    /// <param name="newBranchName">Name of the new branch to create</param>
    /// <param name="startPoint">Optional starting point for the new branch</param>
    Task CreateWorktreeWithNewBranchAsync(string repoPath, string worktreePath, string newBranchName, string? startPoint = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new worktree in detached HEAD state at a specific commit.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="worktreePath">Path where the worktree will be created</param>
    /// <param name="commitSha">SHA of the commit to check out</param>
    Task CreateWorktreeDetachedAsync(string repoPath, string worktreePath, string commitSha, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a worktree.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="worktreePath">Path of the worktree to remove</param>
    /// <param name="force">Force removal even if worktree has modifications</param>
    Task RemoveWorktreeAsync(string repoPath, string worktreePath, bool force = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lock a worktree to prevent removal.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="worktreePath">Path of the worktree to lock</param>
    /// <param name="reason">Optional reason for locking</param>
    Task LockWorktreeAsync(string repoPath, string worktreePath, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unlock a worktree.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="worktreePath">Path of the worktree to unlock</param>
    Task UnlockWorktreeAsync(string repoPath, string worktreePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prune stale worktree references.
    /// </summary>
    Task PruneWorktreesAsync(string repoPath, CancellationToken cancellationToken = default);

    #endregion
}
