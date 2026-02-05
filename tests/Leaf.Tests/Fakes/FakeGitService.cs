using Leaf.Models;
using Leaf.Services;

namespace Leaf.Tests.Fakes;

/// <summary>
/// Fake implementation of IGitService for testing.
/// </summary>
public class FakeGitService : IGitService
{
    public event EventHandler<GitCommandEventArgs>? GitCommandExecuted;

    // Track method calls
    public List<(string RepoPath, string PatchContent)> RevertHunkCalls { get; } = [];
    public List<(string RepoPath, string PatchContent)> StageHunkCalls { get; } = [];
    public List<(string RepoPath, string PatchContent)> UnstageHunkCalls { get; } = [];
    public List<(string RepoPath, string FilePath)> DiscardFileChangesCalls { get; } = [];

    // Configure behavior
    public bool ShouldThrowOnRevertHunk { get; set; }
    public bool ShouldThrowOnStageHunk { get; set; }
    public bool ShouldThrowOnUnstageHunk { get; set; }
    public string? ExceptionMessage { get; set; } = "Operation failed";

    public Task RevertHunkAsync(string repoPath, string patchContent)
    {
        RevertHunkCalls.Add((repoPath, patchContent));
        if (ShouldThrowOnRevertHunk)
            throw new InvalidOperationException(ExceptionMessage);
        return Task.CompletedTask;
    }

    public Task StageHunkAsync(string repoPath, string patchContent)
    {
        StageHunkCalls.Add((repoPath, patchContent));
        if (ShouldThrowOnStageHunk)
            throw new InvalidOperationException(ExceptionMessage);
        return Task.CompletedTask;
    }

    public Task UnstageHunkAsync(string repoPath, string patchContent)
    {
        UnstageHunkCalls.Add((repoPath, patchContent));
        if (ShouldThrowOnUnstageHunk)
            throw new InvalidOperationException(ExceptionMessage);
        return Task.CompletedTask;
    }

    public Task DiscardFileChangesAsync(string repoPath, string filePath)
    {
        DiscardFileChangesCalls.Add((repoPath, filePath));
        return Task.CompletedTask;
    }

    // Stub implementations for other interface methods
    public Task<bool> IsValidRepositoryAsync(string path) => Task.FromResult(true);
    public Task<List<CommitInfo>> GetCommitHistoryAsync(string repoPath, int count = 500, string? branchName = null, int skip = 0) => Task.FromResult(new List<CommitInfo>());
    public Task<List<CommitInfo>> GetMergeCommitsAsync(string repoPath, string mergeSha) => Task.FromResult(new List<CommitInfo>());
    public Task<CommitInfo?> GetCommitAsync(string repoPath, string sha) => Task.FromResult<CommitInfo?>(null);
    public Task<List<FileChangeInfo>> GetCommitChangesAsync(string repoPath, string sha) => Task.FromResult(new List<FileChangeInfo>());
    public Task<(string oldContent, string newContent)> GetFileDiffAsync(string repoPath, string sha, string filePath) => Task.FromResult(("", ""));
    public Task<(string oldContent, string newContent)> GetUnstagedFileDiffAsync(string repoPath, string filePath) => Task.FromResult(("", ""));
    public Task<(string oldContent, string newContent)> GetStagedFileDiffAsync(string repoPath, string filePath) => Task.FromResult(("", ""));
    public Task<List<BranchInfo>> GetBranchesAsync(string repoPath) => Task.FromResult(new List<BranchInfo>());
    public Task<List<RemoteInfo>> GetRemotesAsync(string repoPath) => Task.FromResult(new List<RemoteInfo>());
    public Task AddRemoteAsync(string repoPath, string remoteName, string url, string? pushUrl = null) => Task.CompletedTask;
    public Task RemoveRemoteAsync(string repoPath, string remoteName) => Task.CompletedTask;
    public Task RenameRemoteAsync(string repoPath, string oldName, string newName) => Task.CompletedTask;
    public Task SetRemoteUrlAsync(string repoPath, string remoteName, string url, bool isPushUrl = false) => Task.CompletedTask;
    public Task SetConfigAsync(string repoPath, string key, string value) => Task.CompletedTask;
    public Task<string?> GetConfigAsync(string repoPath, string key) => Task.FromResult<string?>(null);
    public Task<RepositoryInfo> GetRepositoryInfoAsync(string repoPath) => Task.FromResult(new RepositoryInfo());
    public Task<string> CloneAsync(string url, string localPath, string? username = null, string? password = null, IProgress<string>? progress = null) => Task.FromResult("");
    public Task FetchAsync(string repoPath, string remoteName = "origin", string? username = null, string? password = null, IProgress<string>? progress = null) => Task.CompletedTask;
    public Task PullAsync(string repoPath, string? username = null, string? password = null, IProgress<string>? progress = null) => Task.CompletedTask;
    public Task PushAsync(string repoPath, string? remoteName = null, string? username = null, string? password = null, IProgress<string>? progress = null) => Task.CompletedTask;
    public Task PullBranchFastForwardAsync(string repoPath, string branchName, string remoteName, string remoteBranchName, bool isCurrentBranch) => Task.CompletedTask;
    public Task PushBranchAsync(string repoPath, string branchName, string remoteName, string remoteBranchName, bool isCurrentBranch) => Task.CompletedTask;
    public Task SetUpstreamAsync(string repoPath, string branchName, string remoteName, string remoteBranchName) => Task.CompletedTask;
    public Task RenameBranchAsync(string repoPath, string oldName, string newName) => Task.CompletedTask;
    public Task RevertCommitAsync(string repoPath, string commitSha) => Task.CompletedTask;
    public Task RevertMergeCommitAsync(string repoPath, string commitSha, int parentIndex) => Task.CompletedTask;
    public Task<bool> RedoCommitAsync(string repoPath) => Task.FromResult(false);
    public Task ResetBranchToCommitAsync(string repoPath, string branchName, string commitSha, bool updateWorkingTree) => Task.CompletedTask;
    public Task CheckoutAsync(string repoPath, string branchName, bool allowConflicts = false) => Task.CompletedTask;
    public Task CheckoutCommitAsync(string repoPath, string commitSha) => Task.CompletedTask;
    public Task CreateBranchAsync(string repoPath, string branchName, bool checkout = true) => Task.CompletedTask;
    public Task CreateBranchAtCommitAsync(string repoPath, string branchName, string commitSha, bool checkout = true) => Task.CompletedTask;
    public Task<MergeResult> CherryPickAsync(string repoPath, string commitSha) => Task.FromResult(new MergeResult());
    public Task<string> GetCommitToWorkingTreeDiffAsync(string repoPath, string commitSha) => Task.FromResult("");
    public Task StashAsync(string repoPath, string? message = null) => Task.CompletedTask;
    public Task StashStagedAsync(string repoPath, string? message = null) => Task.CompletedTask;
    public Task<MergeResult> PopStashAsync(string repoPath) => Task.FromResult(new MergeResult());
    public Task<MergeResult> PopStashAsync(string repoPath, int stashIndex) => Task.FromResult(new MergeResult());
    public Task<List<StashInfo>> GetStashesAsync(string repoPath) => Task.FromResult(new List<StashInfo>());
    public Task DeleteStashAsync(string repoPath, int stashIndex) => Task.CompletedTask;
    public Task CleanupTempStashAsync(string repoPath) => Task.CompletedTask;
    public Task<bool> UndoCommitAsync(string repoPath) => Task.FromResult(false);
    public Task<bool> IsHeadPushedAsync(string repoPath) => Task.FromResult(false);
    public Task<List<CommitInfo>> SearchCommitsAsync(string repoPath, string searchText, int maxResults = 100) => Task.FromResult(new List<CommitInfo>());
    public Task<WorkingChangesInfo> GetWorkingChangesAsync(string repoPath) => Task.FromResult(new WorkingChangesInfo());
    public Task<string> GetWorkingChangesPatchAsync(string repoPath) => Task.FromResult("");
    public Task<string> GetStagedSummaryAsync(string repoPath, int maxFiles = 100, int maxDiffChars = 50000) => Task.FromResult("");
    public Task StageFileAsync(string repoPath, string filePath) => Task.CompletedTask;
    public Task UnstageFileAsync(string repoPath, string filePath) => Task.CompletedTask;
    public Task UntrackFileAsync(string repoPath, string filePath) => Task.CompletedTask;
    public Task StageAllAsync(string repoPath) => Task.CompletedTask;
    public Task UnstageAllAsync(string repoPath) => Task.CompletedTask;
    public Task DiscardAllChangesAsync(string repoPath) => Task.CompletedTask;
    public Task CommitAsync(string repoPath, string message, string? description = null) => Task.CompletedTask;
    public Task<List<ConflictInfo>> GetConflictsAsync(string repoPath) => Task.FromResult(new List<ConflictInfo>());
    public Task ResolveConflictWithOursAsync(string repoPath, string filePath) => Task.CompletedTask;
    public Task ResolveConflictWithTheirsAsync(string repoPath, string filePath) => Task.CompletedTask;
    public Task MarkConflictResolvedAsync(string repoPath, string filePath) => Task.CompletedTask;
    public Task ReopenConflictAsync(string repoPath, string filePath, string baseContent, string oursContent, string theirsContent) => Task.CompletedTask;
    public Task<List<ConflictInfo>> GetResolvedMergeFilesAsync(string repoPath) => Task.FromResult(new List<ConflictInfo>());
    public Task<List<string>> GetStoredMergeConflictFilesAsync(string repoPath) => Task.FromResult(new List<string>());
    public Task SaveStoredMergeConflictFilesAsync(string repoPath, IEnumerable<string> files) => Task.CompletedTask;
    public Task ClearStoredMergeConflictFilesAsync(string repoPath) => Task.CompletedTask;
    public Task CompleteMergeAsync(string repoPath, string commitMessage) => Task.CompletedTask;
    public Task AbortMergeAsync(string repoPath) => Task.CompletedTask;
    public Task<bool> IsOrphanedConflictStateAsync(string repoPath) => Task.FromResult(false);
    public Task ResetOrphanedConflictsAsync(string repoPath, bool discardWorkingChanges) => Task.CompletedTask;
    public Task<MergeResult> MergeBranchAsync(string repoPath, string branchName, bool allowUnrelatedHistories = false) => Task.FromResult(new MergeResult());
    public Task<MergeResult> FastForwardAsync(string repoPath, string targetBranchName) => Task.FromResult(new MergeResult());
    public Task OpenConflictInVsCodeAsync(string repoPath, string filePath) => Task.CompletedTask;
    public Task DeleteBranchAsync(string repoPath, string branchName, bool force = false) => Task.CompletedTask;
    public Task DeleteRemoteBranchAsync(string repoPath, string remoteName, string branchName, string? username = null, string? password = null) => Task.CompletedTask;
    public Task<List<TagInfo>> GetTagsAsync(string repoPath) => Task.FromResult(new List<TagInfo>());
    public Task CreateTagAsync(string repoPath, string tagName, string? message = null, string? targetSha = null) => Task.CompletedTask;
    public Task DeleteTagAsync(string repoPath, string tagName) => Task.CompletedTask;
    public Task PushTagAsync(string repoPath, string tagName, string remoteName = "origin", string? username = null, string? password = null) => Task.CompletedTask;
    public Task DeleteRemoteTagAsync(string repoPath, string tagName, string remoteName = "origin", string? username = null, string? password = null) => Task.CompletedTask;
    public Task<MergeResult> RebaseAsync(string repoPath, string ontoBranch, IProgress<string>? progress = null) => Task.FromResult(new MergeResult());
    public Task AbortRebaseAsync(string repoPath) => Task.CompletedTask;
    public Task<MergeResult> ContinueRebaseAsync(string repoPath) => Task.FromResult(new MergeResult());
    public Task<MergeResult> SkipRebaseCommitAsync(string repoPath) => Task.FromResult(new MergeResult());
    public Task<bool> IsRebaseInProgressAsync(string repoPath) => Task.FromResult(false);
    public Task<MergeResult> SquashMergeAsync(string repoPath, string branchName) => Task.FromResult(new MergeResult());
    public Task<List<CommitInfo>> GetCommitsBetweenAsync(string repoPath, string fromRef, string? toRef = null) => Task.FromResult(new List<CommitInfo>());
    public Task<List<FileBlameLine>> GetFileBlameAsync(string repoPath, string filePath) => Task.FromResult(new List<FileBlameLine>());
    public Task<List<CommitInfo>> GetFileHistoryAsync(string repoPath, string filePath, int maxCount = 200) => Task.FromResult(new List<CommitInfo>());

    // Worktree operations
    public Task<List<WorktreeInfo>> GetWorktreesAsync(string repoPath) => Task.FromResult(new List<WorktreeInfo>());
    public Task CreateWorktreeAsync(string repoPath, string worktreePath, string branchName) => Task.CompletedTask;
    public Task CreateWorktreeWithNewBranchAsync(string repoPath, string worktreePath, string newBranchName, string? startPoint = null) => Task.CompletedTask;
    public Task CreateWorktreeDetachedAsync(string repoPath, string worktreePath, string commitSha) => Task.CompletedTask;
    public Task RemoveWorktreeAsync(string repoPath, string worktreePath, bool force = false) => Task.CompletedTask;
    public Task LockWorktreeAsync(string repoPath, string worktreePath, string? reason = null) => Task.CompletedTask;
    public Task UnlockWorktreeAsync(string repoPath, string worktreePath) => Task.CompletedTask;
    public Task PruneWorktreesAsync(string repoPath) => Task.CompletedTask;
}
