using Leaf.Models;
using Leaf.Services.Git.Core;
using Leaf.Services.Git.Operations;

namespace Leaf.Services;

/// <summary>
/// Thin facade implementing IGitService that delegates to specialized operation classes.
/// All operations run on background threads and return POCOs.
/// </summary>
public class GitService : IGitService
{
    private readonly GitOperationContext _context;
    private readonly RepositoryOperations _repositoryOps;
    private readonly CommitHistoryOperations _commitHistoryOps;
    private readonly CommitOperations _commitOps;
    private readonly DiffOperations _diffOps;
    private readonly BranchOperations _branchOps;
    private readonly RemoteSyncOperations _remoteSyncOps;
    private readonly StagingOperations _stagingOps;
    private readonly ConflictOperations _conflictOps;
    private readonly MergeOperations _mergeOps;
    private readonly RebaseOperations _rebaseOps;
    private readonly StashOperations _stashOps;
    private readonly TagOperations _tagOps;
    private readonly HunkOperations _hunkOps;
    private readonly ConfigOperations _configOps;

    public event EventHandler<GitCommandEventArgs>? GitCommandExecuted;

    public GitService() : this(new GitCommandRunner())
    {
    }

    public GitService(IGitCommandRunner commandRunner)
    {
        _context = new GitOperationContext(commandRunner);
        _context.GitCommandExecuted += (sender, args) => GitCommandExecuted?.Invoke(this, args);

        // Create operations in dependency order
        _repositoryOps = new RepositoryOperations(_context);
        _commitHistoryOps = new CommitHistoryOperations(_context);
        _commitOps = new CommitOperations(_context);
        _diffOps = new DiffOperations(_context);
        _branchOps = new BranchOperations(_context);
        _remoteSyncOps = new RemoteSyncOperations(_context);
        _stagingOps = new StagingOperations(_context);
        _conflictOps = new ConflictOperations(_context);
        _mergeOps = new MergeOperations(_context);
        _rebaseOps = new RebaseOperations(_context);
        _stashOps = new StashOperations(_context, _conflictOps);
        _tagOps = new TagOperations(_context);
        _hunkOps = new HunkOperations(_context);
        _configOps = new ConfigOperations(_context);
    }

    #region Repository Operations

    public Task<bool> IsValidRepositoryAsync(string path)
        => _repositoryOps.IsValidRepositoryAsync(path);

    public Task<RepositoryInfo> GetRepositoryInfoAsync(string repoPath)
        => _repositoryOps.GetRepositoryInfoAsync(repoPath);

    #endregion

    #region Commit History Operations

    public Task<List<CommitInfo>> GetCommitHistoryAsync(string repoPath, int count = 500, string? branchName = null, int skip = 0)
        => _commitHistoryOps.GetCommitHistoryAsync(repoPath, count, branchName, skip);

    public Task<CommitInfo?> GetCommitAsync(string repoPath, string sha)
        => _commitHistoryOps.GetCommitAsync(repoPath, sha);

    public Task<List<FileChangeInfo>> GetCommitChangesAsync(string repoPath, string sha)
        => _commitHistoryOps.GetCommitChangesAsync(repoPath, sha);

    public Task<List<CommitInfo>> GetMergeCommitsAsync(string repoPath, string mergeSha)
        => _commitHistoryOps.GetMergeCommitsAsync(repoPath, mergeSha);

    public Task<List<CommitInfo>> GetCommitsBetweenAsync(string repoPath, string fromRef, string? toRef = null)
        => _commitHistoryOps.GetCommitsBetweenAsync(repoPath, fromRef, toRef);

    public Task<List<CommitInfo>> SearchCommitsAsync(string repoPath, string searchText, int maxResults = 100)
        => _commitHistoryOps.SearchCommitsAsync(repoPath, searchText, maxResults);

    public Task<List<FileBlameLine>> GetFileBlameAsync(string repoPath, string filePath)
        => _commitHistoryOps.GetFileBlameAsync(repoPath, filePath);

    public Task<List<CommitInfo>> GetFileHistoryAsync(string repoPath, string filePath, int maxCount = 200)
        => _commitHistoryOps.GetFileHistoryAsync(repoPath, filePath, maxCount);

    #endregion

    #region Commit Operations

    public Task CommitAsync(string repoPath, string message, string? description = null)
        => _commitOps.CommitAsync(repoPath, message, description);

    public Task RevertCommitAsync(string repoPath, string commitSha)
        => _commitOps.RevertCommitAsync(repoPath, commitSha);

    public Task RevertMergeCommitAsync(string repoPath, string commitSha, int parentIndex)
        => _commitOps.RevertMergeCommitAsync(repoPath, commitSha, parentIndex);

    public Task<bool> UndoCommitAsync(string repoPath)
        => _commitOps.UndoCommitAsync(repoPath);

    public Task<bool> RedoCommitAsync(string repoPath)
        => _commitOps.RedoCommitAsync(repoPath);

    public Task<bool> IsHeadPushedAsync(string repoPath)
        => _commitOps.IsHeadPushedAsync(repoPath);

    #endregion

    #region Diff Operations

    public Task<(string oldContent, string newContent)> GetFileDiffAsync(string repoPath, string sha, string filePath)
        => _diffOps.GetFileDiffAsync(repoPath, sha, filePath);

    public Task<(string oldContent, string newContent)> GetUnstagedFileDiffAsync(string repoPath, string filePath)
        => _diffOps.GetUnstagedFileDiffAsync(repoPath, filePath);

    public Task<(string oldContent, string newContent)> GetStagedFileDiffAsync(string repoPath, string filePath)
        => _diffOps.GetStagedFileDiffAsync(repoPath, filePath);

    public Task<string> GetCommitToWorkingTreeDiffAsync(string repoPath, string commitSha)
        => _diffOps.GetCommitToWorkingTreeDiffAsync(repoPath, commitSha);

    #endregion

    #region Branch Operations

    public Task<List<BranchInfo>> GetBranchesAsync(string repoPath)
        => _branchOps.GetBranchesAsync(repoPath);

    public Task CheckoutAsync(string repoPath, string branchName, bool allowConflicts = false)
        => _branchOps.CheckoutAsync(repoPath, branchName, allowConflicts);

    public Task CheckoutCommitAsync(string repoPath, string commitSha)
        => _branchOps.CheckoutCommitAsync(repoPath, commitSha);

    public Task CreateBranchAsync(string repoPath, string branchName, bool checkout = true)
        => _branchOps.CreateBranchAsync(repoPath, branchName, checkout);

    public Task CreateBranchAtCommitAsync(string repoPath, string branchName, string commitSha, bool checkout = true)
        => _branchOps.CreateBranchAtCommitAsync(repoPath, branchName, commitSha, checkout);

    public Task DeleteBranchAsync(string repoPath, string branchName, bool force = false)
        => _branchOps.DeleteBranchAsync(repoPath, branchName, force);

    public Task DeleteRemoteBranchAsync(string repoPath, string remoteName, string branchName, string? username = null, string? password = null)
        => _branchOps.DeleteRemoteBranchAsync(repoPath, remoteName, branchName, username, password);

    public Task RenameBranchAsync(string repoPath, string oldName, string newName)
        => _branchOps.RenameBranchAsync(repoPath, oldName, newName);

    public Task SetUpstreamAsync(string repoPath, string branchName, string remoteName, string remoteBranchName)
        => _branchOps.SetUpstreamAsync(repoPath, branchName, remoteName, remoteBranchName);

    public Task ResetBranchToCommitAsync(string repoPath, string branchName, string commitSha, bool updateWorkingTree)
        => _branchOps.ResetBranchToCommitAsync(repoPath, branchName, commitSha, updateWorkingTree);

    #endregion

    #region Remote Sync Operations

    public Task<List<RemoteInfo>> GetRemotesAsync(string repoPath)
        => _remoteSyncOps.GetRemotesAsync(repoPath);

    public Task AddRemoteAsync(string repoPath, string remoteName, string url, string? pushUrl = null)
        => _remoteSyncOps.AddRemoteAsync(repoPath, remoteName, url, pushUrl);

    public Task RemoveRemoteAsync(string repoPath, string remoteName)
        => _remoteSyncOps.RemoveRemoteAsync(repoPath, remoteName);

    public Task RenameRemoteAsync(string repoPath, string oldName, string newName)
        => _remoteSyncOps.RenameRemoteAsync(repoPath, oldName, newName);

    public Task SetRemoteUrlAsync(string repoPath, string remoteName, string url, bool isPushUrl = false)
        => _remoteSyncOps.SetRemoteUrlAsync(repoPath, remoteName, url, isPushUrl);

    public Task<string> CloneAsync(string url, string localPath, string? username = null, string? password = null, IProgress<string>? progress = null)
        => _remoteSyncOps.CloneAsync(url, localPath, username, password, progress);

    public Task FetchAsync(string repoPath, string remoteName = "origin", string? username = null, string? password = null, IProgress<string>? progress = null)
        => _remoteSyncOps.FetchAsync(repoPath, remoteName, username, password, progress);

    public Task PullAsync(string repoPath, string? username = null, string? password = null, IProgress<string>? progress = null)
        => _remoteSyncOps.PullAsync(repoPath, username, password, progress);

    public Task PushAsync(string repoPath, string? remoteName = null, string? username = null, string? password = null, IProgress<string>? progress = null)
        => _remoteSyncOps.PushAsync(repoPath, remoteName, username, password, progress);

    public Task PullBranchFastForwardAsync(string repoPath, string branchName, string remoteName, string remoteBranchName, bool isCurrentBranch)
        => _remoteSyncOps.PullBranchFastForwardAsync(repoPath, branchName, remoteName, remoteBranchName, isCurrentBranch);

    public Task PushBranchAsync(string repoPath, string branchName, string remoteName, string remoteBranchName, bool isCurrentBranch)
        => _remoteSyncOps.PushBranchAsync(repoPath, branchName, remoteName, remoteBranchName, isCurrentBranch);

    #endregion

    #region Staging Operations

    public Task<WorkingChangesInfo> GetWorkingChangesAsync(string repoPath)
        => _stagingOps.GetWorkingChangesAsync(repoPath);

    public Task<string> GetWorkingChangesPatchAsync(string repoPath)
        => _stagingOps.GetWorkingChangesPatchAsync(repoPath);

    public Task<string> GetStagedSummaryAsync(string repoPath, int maxFiles = 100, int maxDiffChars = 50000)
        => _stagingOps.GetStagedSummaryAsync(repoPath, maxFiles, maxDiffChars);

    public Task StageFileAsync(string repoPath, string filePath)
        => _stagingOps.StageFileAsync(repoPath, filePath);

    public Task UnstageFileAsync(string repoPath, string filePath)
        => _stagingOps.UnstageFileAsync(repoPath, filePath);

    public Task UntrackFileAsync(string repoPath, string filePath)
        => _stagingOps.UntrackFileAsync(repoPath, filePath);

    public Task StageAllAsync(string repoPath)
        => _stagingOps.StageAllAsync(repoPath);

    public Task UnstageAllAsync(string repoPath)
        => _stagingOps.UnstageAllAsync(repoPath);

    public Task DiscardAllChangesAsync(string repoPath)
        => _stagingOps.DiscardAllChangesAsync(repoPath);

    public Task DiscardFileChangesAsync(string repoPath, string filePath)
        => _stagingOps.DiscardFileChangesAsync(repoPath, filePath);

    #endregion

    #region Conflict Operations

    public Task<List<ConflictInfo>> GetConflictsAsync(string repoPath)
        => _conflictOps.GetConflictsAsync(repoPath);

    public Task ResolveConflictWithOursAsync(string repoPath, string filePath)
        => _conflictOps.ResolveConflictWithOursAsync(repoPath, filePath);

    public Task ResolveConflictWithTheirsAsync(string repoPath, string filePath)
        => _conflictOps.ResolveConflictWithTheirsAsync(repoPath, filePath);

    public Task MarkConflictResolvedAsync(string repoPath, string filePath)
        => _conflictOps.MarkConflictResolvedAsync(repoPath, filePath);

    public Task ReopenConflictAsync(string repoPath, string filePath, string baseContent, string oursContent, string theirsContent)
        => _conflictOps.ReopenConflictAsync(repoPath, filePath, baseContent, oursContent, theirsContent);

    public Task<List<ConflictInfo>> GetResolvedMergeFilesAsync(string repoPath)
        => _conflictOps.GetResolvedMergeFilesAsync(repoPath);

    public Task<List<string>> GetStoredMergeConflictFilesAsync(string repoPath)
        => _conflictOps.GetStoredMergeConflictFilesAsync(repoPath);

    public Task SaveStoredMergeConflictFilesAsync(string repoPath, IEnumerable<string> files)
        => _conflictOps.SaveStoredMergeConflictFilesAsync(repoPath, files);

    public Task ClearStoredMergeConflictFilesAsync(string repoPath)
        => _conflictOps.ClearStoredMergeConflictFilesAsync(repoPath);

    public Task OpenConflictInVsCodeAsync(string repoPath, string filePath)
        => _conflictOps.OpenConflictInVsCodeAsync(repoPath, filePath);

    #endregion

    #region Merge Operations

    public Task<MergeResult> MergeBranchAsync(string repoPath, string branchName, bool allowUnrelatedHistories = false)
        => _mergeOps.MergeBranchAsync(repoPath, branchName, allowUnrelatedHistories);

    public Task<MergeResult> FastForwardAsync(string repoPath, string targetBranchName)
        => _mergeOps.FastForwardAsync(repoPath, targetBranchName);

    public Task<MergeResult> SquashMergeAsync(string repoPath, string branchName)
        => _mergeOps.SquashMergeAsync(repoPath, branchName);

    public Task CompleteMergeAsync(string repoPath, string commitMessage)
        => _mergeOps.CompleteMergeAsync(repoPath, commitMessage);

    public Task AbortMergeAsync(string repoPath)
        => _mergeOps.AbortMergeAsync(repoPath);

    public Task<MergeResult> CherryPickAsync(string repoPath, string commitSha)
        => _mergeOps.CherryPickAsync(repoPath, commitSha);

    #endregion

    #region Rebase Operations

    public Task<MergeResult> RebaseAsync(string repoPath, string ontoBranch, IProgress<string>? progress = null)
        => _rebaseOps.RebaseAsync(repoPath, ontoBranch, progress);

    public Task AbortRebaseAsync(string repoPath)
        => _rebaseOps.AbortRebaseAsync(repoPath);

    public Task<MergeResult> ContinueRebaseAsync(string repoPath)
        => _rebaseOps.ContinueRebaseAsync(repoPath);

    public Task<MergeResult> SkipRebaseCommitAsync(string repoPath)
        => _rebaseOps.SkipRebaseCommitAsync(repoPath);

    public Task<bool> IsRebaseInProgressAsync(string repoPath)
        => _rebaseOps.IsRebaseInProgressAsync(repoPath);

    #endregion

    #region Stash Operations

    public Task StashAsync(string repoPath, string? message = null)
        => _stashOps.StashAsync(repoPath, message);

    public Task StashStagedAsync(string repoPath, string? message = null)
        => _stashOps.StashStagedAsync(repoPath, message);

    public Task<MergeResult> PopStashAsync(string repoPath)
        => _stashOps.PopStashAsync(repoPath);

    public Task<MergeResult> PopStashAsync(string repoPath, int stashIndex)
        => _stashOps.PopStashAsync(repoPath, stashIndex);

    public Task<List<StashInfo>> GetStashesAsync(string repoPath)
        => _stashOps.GetStashesAsync(repoPath);

    public Task DeleteStashAsync(string repoPath, int stashIndex)
        => _stashOps.DeleteStashAsync(repoPath, stashIndex);

    public Task CleanupTempStashAsync(string repoPath)
        => _stashOps.CleanupTempStashAsync(repoPath);

    #endregion

    #region Tag Operations

    public Task<List<TagInfo>> GetTagsAsync(string repoPath)
        => _tagOps.GetTagsAsync(repoPath);

    public Task CreateTagAsync(string repoPath, string tagName, string? message = null, string? targetSha = null)
        => _tagOps.CreateTagAsync(repoPath, tagName, message, targetSha);

    public Task DeleteTagAsync(string repoPath, string tagName)
        => _tagOps.DeleteTagAsync(repoPath, tagName);

    public Task PushTagAsync(string repoPath, string tagName, string remoteName = "origin", string? username = null, string? password = null)
        => _tagOps.PushTagAsync(repoPath, tagName, remoteName, username, password);

    public Task DeleteRemoteTagAsync(string repoPath, string tagName, string remoteName = "origin", string? username = null, string? password = null)
        => _tagOps.DeleteRemoteTagAsync(repoPath, tagName, remoteName, username, password);

    #endregion

    #region Hunk Operations

    public Task RevertHunkAsync(string repoPath, string patchContent)
        => _hunkOps.RevertHunkAsync(repoPath, patchContent);

    public Task StageHunkAsync(string repoPath, string patchContent)
        => _hunkOps.StageHunkAsync(repoPath, patchContent);

    public Task UnstageHunkAsync(string repoPath, string patchContent)
        => _hunkOps.UnstageHunkAsync(repoPath, patchContent);

    #endregion

    #region Config Operations

    public Task SetConfigAsync(string repoPath, string key, string value)
        => _configOps.SetConfigAsync(repoPath, key, value);

    public Task<string?> GetConfigAsync(string repoPath, string key)
        => _configOps.GetConfigAsync(repoPath, key);

    #endregion
}
