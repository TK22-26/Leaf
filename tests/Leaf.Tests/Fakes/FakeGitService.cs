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

    public Task RevertHunkAsync(string repoPath, string patchContent, CancellationToken cancellationToken = default)
    {
        RevertHunkCalls.Add((repoPath, patchContent));
        if (ShouldThrowOnRevertHunk)
            throw new InvalidOperationException(ExceptionMessage);
        return Task.CompletedTask;
    }

    public Task StageHunkAsync(string repoPath, string patchContent, CancellationToken cancellationToken = default)
    {
        StageHunkCalls.Add((repoPath, patchContent));
        if (ShouldThrowOnStageHunk)
            throw new InvalidOperationException(ExceptionMessage);
        return Task.CompletedTask;
    }

    public Task UnstageHunkAsync(string repoPath, string patchContent, CancellationToken cancellationToken = default)
    {
        UnstageHunkCalls.Add((repoPath, patchContent));
        if (ShouldThrowOnUnstageHunk)
            throw new InvalidOperationException(ExceptionMessage);
        return Task.CompletedTask;
    }

    public Task DiscardFileChangesAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
    {
        DiscardFileChangesCalls.Add((repoPath, filePath));
        return Task.CompletedTask;
    }

    // Stub implementations for other interface methods
    public Task<bool> IsValidRepositoryAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<List<CommitInfo>> GetCommitHistoryAsync(string repoPath, int count = 500, string? branchName = null, int skip = 0, CancellationToken cancellationToken = default) => Task.FromResult(new List<CommitInfo>());
    public Task<List<CommitInfo>> GetMergeCommitsAsync(string repoPath, string mergeSha, CancellationToken cancellationToken = default) => Task.FromResult(new List<CommitInfo>());
    public Task<CommitInfo?> GetCommitAsync(string repoPath, string sha, CancellationToken cancellationToken = default) => Task.FromResult<CommitInfo?>(null);
    public Task<List<FileChangeInfo>> GetCommitChangesAsync(string repoPath, string sha, CancellationToken cancellationToken = default) => Task.FromResult(new List<FileChangeInfo>());
    public Task<List<FileChangeInfo>> GetCommitAllFilesAsync(string repoPath, string sha, CancellationToken cancellationToken = default) => Task.FromResult(new List<FileChangeInfo>());
    public Task<(string oldContent, string newContent)> GetFileDiffAsync(string repoPath, string sha, string filePath, CancellationToken cancellationToken = default) => Task.FromResult(("", ""));
    public Task<(string oldContent, string newContent)> GetUnstagedFileDiffAsync(string repoPath, string filePath, CancellationToken cancellationToken = default) => Task.FromResult(("", ""));
    public Task<(string oldContent, string newContent)> GetStagedFileDiffAsync(string repoPath, string filePath, CancellationToken cancellationToken = default) => Task.FromResult(("", ""));
    public Task<List<BranchInfo>> GetBranchesAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(new List<BranchInfo>());
    public Task<List<RemoteInfo>> GetRemotesAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(new List<RemoteInfo>());
    public Task AddRemoteAsync(string repoPath, string remoteName, string url, string? pushUrl = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RemoveRemoteAsync(string repoPath, string remoteName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RenameRemoteAsync(string repoPath, string oldName, string newName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetRemoteUrlAsync(string repoPath, string remoteName, string url, bool isPushUrl = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public virtual Task SetConfigAsync(string repoPath, string key, string value, GitConfigScope scope = GitConfigScope.Local, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public virtual Task<string?> GetConfigAsync(string repoPath, string key, GitConfigScope scope = GitConfigScope.Local, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    public virtual Task UnsetConfigAsync(string repoPath, string key, GitConfigScope scope = GitConfigScope.Local, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<RepositoryInfo> GetRepositoryInfoAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(new RepositoryInfo());
    public Task<RepositoryInfo> GetRepositoryInfoFastAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(new RepositoryInfo());
    public Task<string> CloneAsync(string url, string localPath, string? credentialKey = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult("");
    public Task FetchAsync(string repoPath, string remoteName = "origin", string? credentialKey = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PullAsync(string repoPath, string? credentialKey = null, IProgress<string>? progress = null, bool? rebase = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PushAsync(string repoPath, string? remoteName = null, string? credentialKey = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PullBranchFastForwardAsync(string repoPath, string branchName, string remoteName, string remoteBranchName, bool isCurrentBranch, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PushBranchAsync(string repoPath, string branchName, string remoteName, string remoteBranchName, bool isCurrentBranch, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetUpstreamAsync(string repoPath, string branchName, string remoteName, string remoteBranchName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RenameBranchAsync(string repoPath, string oldName, string newName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RevertCommitAsync(string repoPath, string commitSha, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RevertMergeCommitAsync(string repoPath, string commitSha, int parentIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> RedoCommitAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task ResetCurrentBranchToCommitAsync(string repoPath, string commitSha, GitResetMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CheckoutAsync(string repoPath, string branchName, bool allowConflicts = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CheckoutCommitAsync(string repoPath, string commitSha, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CreateBranchAsync(string repoPath, string branchName, bool checkout = true, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CreateBranchAtCommitAsync(string repoPath, string branchName, string commitSha, bool checkout = true, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<MergeResult> CherryPickAsync(string repoPath, string commitSha, CancellationToken cancellationToken = default) => Task.FromResult(new MergeResult());
    public Task<string> GetCommitToWorkingTreeDiffAsync(string repoPath, string commitSha, CancellationToken cancellationToken = default) => Task.FromResult("");
    public Task<string> GetRefToRefDiffAsync(string repoPath, string baseRef, string headRef, string? filePath = null, CancellationToken cancellationToken = default) => Task.FromResult("");
    public Task StashAsync(string repoPath, string? message = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StashStagedAsync(string repoPath, string? message = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<MergeResult> PopStashAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(new MergeResult());
    public Task<MergeResult> PopStashAsync(string repoPath, int stashIndex, CancellationToken cancellationToken = default) => Task.FromResult(new MergeResult());
    public Task<List<StashInfo>> GetStashesAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(new List<StashInfo>());
    public Task DeleteStashAsync(string repoPath, int stashIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CleanupTempStashAsync(string repoPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> UndoCommitAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<bool> IsHeadPushedAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<List<CommitInfo>> SearchCommitsAsync(string repoPath, string searchText, int maxResults = 100, CancellationToken cancellationToken = default) => Task.FromResult(new List<CommitInfo>());
    public Task<WorkingChangesInfo> GetWorkingChangesAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(new WorkingChangesInfo());
    public Task<string> GetWorkingChangesPatchAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult("");
    public Task<string> GetStagedSummaryAsync(string repoPath, int maxFiles = 100, int maxDiffChars = 50000, CancellationToken cancellationToken = default) => Task.FromResult("");
    public Task StageFileAsync(string repoPath, string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UnstageFileAsync(string repoPath, string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UntrackFileAsync(string repoPath, string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StageAllAsync(string repoPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UnstageAllAsync(string repoPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DiscardAllChangesAsync(string repoPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CommitAsync(string repoPath, string message, string? description = null, bool amend = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public virtual Task<CommitInfo?> GetHeadCommitAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult<CommitInfo?>(null);
    public Task<List<ConflictInfo>> GetConflictsAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(new List<ConflictInfo>());
    public Task ResolveConflictWithOursAsync(string repoPath, string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ResolveConflictWithTheirsAsync(string repoPath, string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task MarkConflictResolvedAsync(string repoPath, string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ReopenConflictAsync(string repoPath, string filePath, string baseContent, string oursContent, string theirsContent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<List<ConflictInfo>> GetResolvedMergeFilesAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(new List<ConflictInfo>());
    public Task<List<string>> GetStoredMergeConflictFilesAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(new List<string>());
    public Task SaveStoredMergeConflictFilesAsync(string repoPath, IEnumerable<string> files, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ClearStoredMergeConflictFilesAsync(string repoPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CompleteMergeAsync(string repoPath, string commitMessage, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task AbortMergeAsync(string repoPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task AbortCherryPickAsync(string repoPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task AbortRevertAsync(string repoPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> IsOrphanedConflictStateAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task ResetOrphanedConflictsAsync(string repoPath, bool discardWorkingChanges, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<MergeResult> MergeBranchAsync(string repoPath, string branchName, bool allowUnrelatedHistories = false, CancellationToken cancellationToken = default) => Task.FromResult(new MergeResult());
    public Task<MergeResult> FastForwardAsync(string repoPath, string targetBranchName, CancellationToken cancellationToken = default) => Task.FromResult(new MergeResult());
    public Task<bool> OpenConflictInMergeToolAsync(string repoPath, string filePath, Func<string, string, string, string, CancellationToken, Task<int>> launch, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task DeleteBranchAsync(string repoPath, string branchName, bool force = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteRemoteBranchAsync(string repoPath, string remoteName, string branchName, string? credentialKey = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<List<TagInfo>> GetTagsAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(new List<TagInfo>());
    public Task CreateTagAsync(string repoPath, string tagName, string? message = null, string? targetSha = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteTagAsync(string repoPath, string tagName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PushTagAsync(string repoPath, string tagName, string remoteName = "origin", string? credentialKey = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteRemoteTagAsync(string repoPath, string tagName, string remoteName = "origin", string? credentialKey = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<MergeResult> RebaseAsync(string repoPath, string ontoBranch, bool autosquash = false, bool updateRefs = false, IProgress<string>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(new MergeResult());
    public Task AbortRebaseAsync(string repoPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<MergeResult> ContinueRebaseAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(new MergeResult());
    public Task<MergeResult> SkipRebaseCommitAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(new MergeResult());
    public Task<bool> IsRebaseInProgressAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<bool> IsAmInProgressAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<MergeResult> ContinueAmAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(new MergeResult());
    public Task<MergeResult> SkipAmAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(new MergeResult());
    public Task AbortAmAsync(string repoPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<MergeResult> SquashMergeAsync(string repoPath, string branchName, CancellationToken cancellationToken = default) => Task.FromResult(new MergeResult());
    public Task<List<CommitInfo>> GetCommitsBetweenAsync(string repoPath, string fromRef, string? toRef = null, CancellationToken cancellationToken = default) => Task.FromResult(new List<CommitInfo>());
    public virtual Task<List<FileBlameLine>> GetFileBlameAsync(string repoPath, string filePath, CancellationToken cancellationToken = default) => Task.FromResult(new List<FileBlameLine>());
    public Task<List<CommitInfo>> GetFileHistoryAsync(string repoPath, string filePath, int maxCount = 200, CancellationToken cancellationToken = default) => Task.FromResult(new List<CommitInfo>());

    // Worktree operations
    public Task<List<WorktreeInfo>> GetWorktreesAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(new List<WorktreeInfo>());
    public Task CreateWorktreeAsync(string repoPath, string worktreePath, string branchName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CreateWorktreeWithNewBranchAsync(string repoPath, string worktreePath, string newBranchName, string? startPoint = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CreateWorktreeDetachedAsync(string repoPath, string worktreePath, string commitSha, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RemoveWorktreeAsync(string repoPath, string worktreePath, bool force = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task LockWorktreeAsync(string repoPath, string worktreePath, string? reason = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UnlockWorktreeAsync(string repoPath, string worktreePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PruneWorktreesAsync(string repoPath, CancellationToken cancellationToken = default) => Task.CompletedTask;

    // Submodule operations
    public Task<List<SubmoduleInfo>> GetSubmodulesAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(new List<SubmoduleInfo>());
    public Task<bool> GetSubmoduleWorkingTreeDirtyAsync(string parentRepoPath, string submodulePath, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task InitAndUpdateSubmodulesAsync(string repoPath, IReadOnlyList<string> paths, bool recursive, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SyncSubmodulesAsync(string repoPath, IReadOnlyList<string> paths, bool recursive, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeinitSubmoduleAsync(string repoPath, string path, bool force, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task AddSubmoduleAsync(string repoPath, string url, string path, string? branch, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UpdateSubmoduleToRemoteAsync(string repoPath, string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RemoveSubmoduleAsync(string repoPath, SubmoduleInfo submodule, CancellationToken cancellationToken = default) => Task.CompletedTask;

    // Reflog operations
    public Task<List<ReflogEntry>> GetReflogAsync(string repoPath, CancellationToken cancellationToken = default) => Task.FromResult(new List<ReflogEntry>());
}
