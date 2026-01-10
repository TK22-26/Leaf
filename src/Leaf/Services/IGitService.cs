using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Interface for Git operations.
/// All operations return POCOs and run asynchronously.
/// </summary>
public interface IGitService
{
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
    /// Checkout a branch.
    /// </summary>
    Task CheckoutAsync(string repoPath, string branchName);

    /// <summary>
    /// Create a new branch.
    /// </summary>
    Task CreateBranchAsync(string repoPath, string branchName, bool checkout = true);

    /// <summary>
    /// Stash changes.
    /// </summary>
    Task StashAsync(string repoPath, string? message = null);

    /// <summary>
    /// Pop stashed changes.
    /// </summary>
    Task PopStashAsync(string repoPath);

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
}
