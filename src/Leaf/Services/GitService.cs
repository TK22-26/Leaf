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
    private readonly CommitSignatureOperations _commitSignatureOps;
    private readonly CommitOperations _commitOps;
    private readonly DiffOperations _diffOps;
    private readonly BranchOperations _branchOps;
    private readonly RemoteSyncOperations _remoteSyncOps;
    private readonly StagingOperations _stagingOps;
    private readonly ConflictOperations _conflictOps;
    private readonly MergeOperations _mergeOps;
    private readonly RebaseOperations _rebaseOps;
    private readonly AmOperations _amOps;
    private readonly StashOperations _stashOps;
    private readonly TagOperations _tagOps;
    private readonly HunkOperations _hunkOps;
    private readonly ConfigOperations _configOps;
    private readonly WorktreeOperations _worktreeOps;
    private readonly SubmoduleOperations _submoduleOps;
    private readonly ReflogOperations _reflogOps;

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
        _commitSignatureOps = new CommitSignatureOperations(_context);
        _commitOps = new CommitOperations(_context);
        _diffOps = new DiffOperations(_context);
        _branchOps = new BranchOperations(_context);
        _remoteSyncOps = new RemoteSyncOperations(_context);
        _stagingOps = new StagingOperations(_context);
        _conflictOps = new ConflictOperations(_context);
        _mergeOps = new MergeOperations(_context);
        _rebaseOps = new RebaseOperations(_context);
        _amOps = new AmOperations(_context);
        _stashOps = new StashOperations(_context, _conflictOps);
        _tagOps = new TagOperations(_context);
        _hunkOps = new HunkOperations(_context);
        _configOps = new ConfigOperations(_context);
        _worktreeOps = new WorktreeOperations(_context);
        _submoduleOps = new SubmoduleOperations(_context);
        _reflogOps = new ReflogOperations(_context);
    }

    #region Repository Operations

    public Task<bool> IsValidRepositoryAsync(string path, CancellationToken cancellationToken = default)
        => _repositoryOps.IsValidRepositoryAsync(path, cancellationToken);

    public Task<RepositoryInfo> GetRepositoryInfoAsync(string repoPath, CancellationToken cancellationToken = default)
#pragma warning disable CS0618 // Obsolete — kept for callers that need LibGit2Sharp fallback
        => _repositoryOps.GetRepositoryInfoAsync(repoPath, cancellationToken);
#pragma warning restore CS0618

    public Task<RepositoryInfo> GetRepositoryInfoFastAsync(string repoPath, CancellationToken cancellationToken = default)
        => _repositoryOps.GetRepositoryInfoFastAsync(repoPath, cancellationToken);

    #endregion

    #region Commit History Operations

    public async Task<List<CommitInfo>> GetCommitHistoryAsync(string repoPath, int count = 500, string? branchName = null, int skip = 0, CancellationToken cancellationToken = default)
    {
        var commits = await _commitHistoryOps
            .GetCommitHistoryAsync(repoPath, count, branchName, skip, cancellationToken)
            .ConfigureAwait(false);
        await EnrichSignaturesAsync(repoPath, commits, cancellationToken).ConfigureAwait(false);
        return commits;
    }

    public async Task<CommitInfo?> GetCommitAsync(string repoPath, string sha, CancellationToken cancellationToken = default)
    {
        var commit = await _commitHistoryOps.GetCommitAsync(repoPath, sha, cancellationToken).ConfigureAwait(false);
        if (commit != null)
            await EnrichSignaturesAsync(repoPath, [commit], cancellationToken).ConfigureAwait(false);
        return commit;
    }

    /// <summary>
    /// Stamp <see cref="CommitInfo.SignatureStatus"/> + signer fields on
    /// each commit by running a chunked <c>git log</c> query. Failures
    /// are non-fatal — the badges just don't appear.
    /// </summary>
    private async Task EnrichSignaturesAsync(string repoPath, IReadOnlyList<CommitInfo> commits, CancellationToken cancellationToken)
    {
        if (commits.Count == 0) return;
        try
        {
            var shas = commits.Select(c => c.Sha).ToList();
            var sigs = await _commitSignatureOps
                .GetSignaturesAsync(repoPath, shas, cancellationToken)
                .ConfigureAwait(false);
            if (sigs.Count == 0) return;

            foreach (var commit in commits)
            {
                if (!sigs.TryGetValue(commit.Sha, out var data)) continue;
                commit.SignatureStatus = data.Status;
                commit.SignerName = data.SignerName;
                commit.SignerEmail = data.SignerEmail;
                commit.SignerKeyFingerprint = data.Fingerprint;
            }
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled — let CallerExitTokenException flow up by
            // not rethrowing here; the partial enrichment that did make
            // it onto commits stays.
        }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException)
        {
            Log.Warn("Signing", $"Signature enrichment failed for {repoPath}: {ex.Message}");
        }
    }

    public Task<List<FileChangeInfo>> GetCommitChangesAsync(string repoPath, string sha, CancellationToken cancellationToken = default)
        => _commitHistoryOps.GetCommitChangesAsync(repoPath, sha, cancellationToken);

    public Task<List<FileChangeInfo>> GetCommitAllFilesAsync(string repoPath, string sha, CancellationToken cancellationToken = default)
        => _commitHistoryOps.GetCommitAllFilesAsync(repoPath, sha, cancellationToken);

    public Task<List<CommitInfo>> GetMergeCommitsAsync(string repoPath, string mergeSha, CancellationToken cancellationToken = default)
        => _commitHistoryOps.GetMergeCommitsAsync(repoPath, mergeSha, cancellationToken);

    public Task<List<CommitInfo>> GetCommitsBetweenAsync(string repoPath, string fromRef, string? toRef = null, CancellationToken cancellationToken = default)
        => _commitHistoryOps.GetCommitsBetweenAsync(repoPath, fromRef, toRef, cancellationToken);

    public Task<List<CommitInfo>> SearchCommitsAsync(string repoPath, string searchText, int maxResults = 100, CancellationToken cancellationToken = default)
        => _commitHistoryOps.SearchCommitsAsync(repoPath, searchText, maxResults, cancellationToken);

    public Task<List<FileBlameLine>> GetFileBlameAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
        => _commitHistoryOps.GetFileBlameAsync(repoPath, filePath, cancellationToken);

    public Task<List<CommitInfo>> GetFileHistoryAsync(string repoPath, string filePath, int maxCount = 200, CancellationToken cancellationToken = default)
        => _commitHistoryOps.GetFileHistoryAsync(repoPath, filePath, maxCount, cancellationToken);

    #endregion

    #region Commit Operations

    public Task CommitAsync(string repoPath, string message, string? description = null, bool amend = false, CancellationToken cancellationToken = default)
        => _commitOps.CommitAsync(repoPath, message, description, amend, cancellationToken);

    public Task<CommitInfo?> GetHeadCommitAsync(string repoPath, CancellationToken cancellationToken = default)
        => _commitHistoryOps.GetHeadCommitAsync(repoPath, cancellationToken);

    public Task RevertCommitAsync(string repoPath, string commitSha, CancellationToken cancellationToken = default)
        => _commitOps.RevertCommitAsync(repoPath, commitSha, cancellationToken);

    public Task RevertMergeCommitAsync(string repoPath, string commitSha, int parentIndex, CancellationToken cancellationToken = default)
        => _commitOps.RevertMergeCommitAsync(repoPath, commitSha, parentIndex, cancellationToken);

    public Task<bool> UndoCommitAsync(string repoPath, CancellationToken cancellationToken = default)
        => _commitOps.UndoCommitAsync(repoPath, cancellationToken);

    public Task<bool> RedoCommitAsync(string repoPath, CancellationToken cancellationToken = default)
        => _commitOps.RedoCommitAsync(repoPath, cancellationToken);

    public Task<bool> IsHeadPushedAsync(string repoPath, CancellationToken cancellationToken = default)
        => _commitOps.IsHeadPushedAsync(repoPath, cancellationToken);

    #endregion

    #region Diff Operations

    public Task<(string oldContent, string newContent)> GetFileDiffAsync(string repoPath, string sha, string filePath, CancellationToken cancellationToken = default)
        => _diffOps.GetFileDiffAsync(repoPath, sha, filePath, cancellationToken);

    public Task<(string oldContent, string newContent)> GetUnstagedFileDiffAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
        => _diffOps.GetUnstagedFileDiffAsync(repoPath, filePath, cancellationToken);

    public Task<(string oldContent, string newContent)> GetStagedFileDiffAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
        => _diffOps.GetStagedFileDiffAsync(repoPath, filePath, cancellationToken);

    public Task<string> GetCommitToWorkingTreeDiffAsync(string repoPath, string commitSha, CancellationToken cancellationToken = default)
        => _diffOps.GetCommitToWorkingTreeDiffAsync(repoPath, commitSha, cancellationToken);

    public Task<string> GetRefToRefDiffAsync(string repoPath, string baseRef, string headRef, string? filePath = null, CancellationToken cancellationToken = default)
        => _diffOps.GetRefToRefDiffAsync(repoPath, baseRef, headRef, filePath, cancellationToken);

    #endregion

    #region Branch Operations

    public Task<List<BranchInfo>> GetBranchesAsync(string repoPath, CancellationToken cancellationToken = default)
        => _branchOps.GetBranchesAsync(repoPath, cancellationToken);

    public Task CheckoutAsync(string repoPath, string branchName, bool allowConflicts = false, CancellationToken cancellationToken = default)
        => _branchOps.CheckoutAsync(repoPath, branchName, allowConflicts, cancellationToken);

    public Task CheckoutCommitAsync(string repoPath, string commitSha, CancellationToken cancellationToken = default)
        => _branchOps.CheckoutCommitAsync(repoPath, commitSha, cancellationToken);

    public Task CreateBranchAsync(string repoPath, string branchName, bool checkout = true, CancellationToken cancellationToken = default)
        => _branchOps.CreateBranchAsync(repoPath, branchName, checkout, cancellationToken);

    public Task CreateBranchAtCommitAsync(string repoPath, string branchName, string commitSha, bool checkout = true, CancellationToken cancellationToken = default)
        => _branchOps.CreateBranchAtCommitAsync(repoPath, branchName, commitSha, checkout, cancellationToken);

    public Task DeleteBranchAsync(string repoPath, string branchName, bool force = false, CancellationToken cancellationToken = default)
        => _branchOps.DeleteBranchAsync(repoPath, branchName, force, cancellationToken);

    public Task DeleteRemoteBranchAsync(string repoPath, string remoteName, string branchName, string? credentialKey = null, CancellationToken cancellationToken = default)
        => _branchOps.DeleteRemoteBranchAsync(repoPath, remoteName, branchName, credentialKey, cancellationToken);

    public Task RenameBranchAsync(string repoPath, string oldName, string newName, CancellationToken cancellationToken = default)
        => _branchOps.RenameBranchAsync(repoPath, oldName, newName, cancellationToken);

    public Task SetUpstreamAsync(string repoPath, string branchName, string remoteName, string remoteBranchName, CancellationToken cancellationToken = default)
        => _branchOps.SetUpstreamAsync(repoPath, branchName, remoteName, remoteBranchName, cancellationToken);

    public Task ResetCurrentBranchToCommitAsync(string repoPath, string commitSha, GitResetMode mode, CancellationToken cancellationToken = default)
        => _branchOps.ResetCurrentBranchToCommitAsync(repoPath, commitSha, mode, cancellationToken);

    #endregion

    #region Remote Sync Operations

    public Task<List<RemoteInfo>> GetRemotesAsync(string repoPath, CancellationToken cancellationToken = default)
        => _remoteSyncOps.GetRemotesAsync(repoPath, cancellationToken);

    public Task AddRemoteAsync(string repoPath, string remoteName, string url, string? pushUrl = null, CancellationToken cancellationToken = default)
        => _remoteSyncOps.AddRemoteAsync(repoPath, remoteName, url, pushUrl, cancellationToken);

    public Task RemoveRemoteAsync(string repoPath, string remoteName, CancellationToken cancellationToken = default)
        => _remoteSyncOps.RemoveRemoteAsync(repoPath, remoteName, cancellationToken);

    public Task RenameRemoteAsync(string repoPath, string oldName, string newName, CancellationToken cancellationToken = default)
        => _remoteSyncOps.RenameRemoteAsync(repoPath, oldName, newName, cancellationToken);

    public Task SetRemoteUrlAsync(string repoPath, string remoteName, string url, bool isPushUrl = false, CancellationToken cancellationToken = default)
        => _remoteSyncOps.SetRemoteUrlAsync(repoPath, remoteName, url, isPushUrl, cancellationToken);

    public Task<string> CloneAsync(string url, string localPath, string? credentialKey = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        => _remoteSyncOps.CloneAsync(url, localPath, credentialKey, progress, cancellationToken);

    public Task FetchAsync(string repoPath, string remoteName = "origin", string? credentialKey = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        => _remoteSyncOps.FetchAsync(repoPath, remoteName, credentialKey, progress, cancellationToken);

    public Task PullAsync(string repoPath, string? credentialKey = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        => _remoteSyncOps.PullAsync(repoPath, credentialKey, progress, cancellationToken);

    public Task PushAsync(string repoPath, string? remoteName = null, string? credentialKey = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        => _remoteSyncOps.PushAsync(repoPath, remoteName, credentialKey, progress, cancellationToken);

    public Task PullBranchFastForwardAsync(string repoPath, string branchName, string remoteName, string remoteBranchName, bool isCurrentBranch, CancellationToken cancellationToken = default)
        => _remoteSyncOps.PullBranchFastForwardAsync(repoPath, branchName, remoteName, remoteBranchName, isCurrentBranch, cancellationToken);

    public Task PushBranchAsync(string repoPath, string branchName, string remoteName, string remoteBranchName, bool isCurrentBranch, CancellationToken cancellationToken = default)
        => _remoteSyncOps.PushBranchAsync(repoPath, branchName, remoteName, remoteBranchName, isCurrentBranch, cancellationToken);

    #endregion

    #region Staging Operations

    public Task<WorkingChangesInfo> GetWorkingChangesAsync(string repoPath, CancellationToken cancellationToken = default)
        => _stagingOps.GetWorkingChangesAsync(repoPath, cancellationToken);

    public Task<string> GetWorkingChangesPatchAsync(string repoPath, CancellationToken cancellationToken = default)
        => _stagingOps.GetWorkingChangesPatchAsync(repoPath, cancellationToken);

    public Task<string> GetStagedSummaryAsync(string repoPath, int maxFiles = 100, int maxDiffChars = 50000, CancellationToken cancellationToken = default)
        => _stagingOps.GetStagedSummaryAsync(repoPath, maxFiles, maxDiffChars, cancellationToken);

    public Task StageFileAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
        => _stagingOps.StageFileAsync(repoPath, filePath, cancellationToken);

    public Task UnstageFileAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
        => _stagingOps.UnstageFileAsync(repoPath, filePath, cancellationToken);

    public Task UntrackFileAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
        => _stagingOps.UntrackFileAsync(repoPath, filePath, cancellationToken);

    public Task StageAllAsync(string repoPath, CancellationToken cancellationToken = default)
        => _stagingOps.StageAllAsync(repoPath, cancellationToken);

    public Task UnstageAllAsync(string repoPath, CancellationToken cancellationToken = default)
        => _stagingOps.UnstageAllAsync(repoPath, cancellationToken);

    public Task DiscardAllChangesAsync(string repoPath, CancellationToken cancellationToken = default)
        => _stagingOps.DiscardAllChangesAsync(repoPath, cancellationToken);

    public Task DiscardFileChangesAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
        => _stagingOps.DiscardFileChangesAsync(repoPath, filePath, cancellationToken);

    #endregion

    #region Conflict Operations

    public Task<List<ConflictInfo>> GetConflictsAsync(string repoPath, CancellationToken cancellationToken = default)
        => _conflictOps.GetConflictsAsync(repoPath, cancellationToken);

    public Task ResolveConflictWithOursAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
        => _conflictOps.ResolveConflictWithOursAsync(repoPath, filePath, cancellationToken);

    public Task ResolveConflictWithTheirsAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
        => _conflictOps.ResolveConflictWithTheirsAsync(repoPath, filePath, cancellationToken);

    public Task MarkConflictResolvedAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
        => _conflictOps.MarkConflictResolvedAsync(repoPath, filePath, cancellationToken);

    public Task ReopenConflictAsync(string repoPath, string filePath, string baseContent, string oursContent, string theirsContent, CancellationToken cancellationToken = default)
        => _conflictOps.ReopenConflictAsync(repoPath, filePath, baseContent, oursContent, theirsContent, cancellationToken);

    public Task<List<ConflictInfo>> GetResolvedMergeFilesAsync(string repoPath, CancellationToken cancellationToken = default)
        => _conflictOps.GetResolvedMergeFilesAsync(repoPath, cancellationToken);

    public Task<List<string>> GetStoredMergeConflictFilesAsync(string repoPath, CancellationToken cancellationToken = default)
        => _conflictOps.GetStoredMergeConflictFilesAsync(repoPath, cancellationToken);

    public Task SaveStoredMergeConflictFilesAsync(string repoPath, IEnumerable<string> files, CancellationToken cancellationToken = default)
        => _conflictOps.SaveStoredMergeConflictFilesAsync(repoPath, files, cancellationToken);

    public Task ClearStoredMergeConflictFilesAsync(string repoPath, CancellationToken cancellationToken = default)
        => _conflictOps.ClearStoredMergeConflictFilesAsync(repoPath, cancellationToken);

    public Task<bool> OpenConflictInMergeToolAsync(
        string repoPath,
        string filePath,
        Func<string, string, string, string, CancellationToken, Task<int>> launch,
        CancellationToken cancellationToken = default)
        => _conflictOps.OpenConflictInMergeToolAsync(repoPath, filePath, launch, cancellationToken);

    #endregion

    #region Merge Operations

    public Task<MergeResult> MergeBranchAsync(string repoPath, string branchName, bool allowUnrelatedHistories = false, CancellationToken cancellationToken = default)
        => _mergeOps.MergeBranchAsync(repoPath, branchName, allowUnrelatedHistories, cancellationToken);

    public Task<MergeResult> FastForwardAsync(string repoPath, string targetBranchName, CancellationToken cancellationToken = default)
        => _mergeOps.FastForwardAsync(repoPath, targetBranchName, cancellationToken);

    public Task<MergeResult> SquashMergeAsync(string repoPath, string branchName, CancellationToken cancellationToken = default)
        => _mergeOps.SquashMergeAsync(repoPath, branchName, cancellationToken);

    public Task CompleteMergeAsync(string repoPath, string commitMessage, CancellationToken cancellationToken = default)
        => _mergeOps.CompleteMergeAsync(repoPath, commitMessage, cancellationToken);

    public Task AbortMergeAsync(string repoPath, CancellationToken cancellationToken = default)
        => _mergeOps.AbortMergeAsync(repoPath, cancellationToken);

    public Task AbortCherryPickAsync(string repoPath, CancellationToken cancellationToken = default)
        => _mergeOps.AbortCherryPickAsync(repoPath, cancellationToken);

    public Task AbortRevertAsync(string repoPath, CancellationToken cancellationToken = default)
        => _mergeOps.AbortRevertAsync(repoPath, cancellationToken);

    public Task<bool> IsOrphanedConflictStateAsync(string repoPath, CancellationToken cancellationToken = default)
        => _mergeOps.IsOrphanedConflictStateAsync(repoPath, cancellationToken);

    public Task ResetOrphanedConflictsAsync(string repoPath, bool discardWorkingChanges, CancellationToken cancellationToken = default)
        => _mergeOps.ResetOrphanedConflictsAsync(repoPath, discardWorkingChanges, cancellationToken);

    public Task<MergeResult> CherryPickAsync(string repoPath, string commitSha, CancellationToken cancellationToken = default)
        => _mergeOps.CherryPickAsync(repoPath, commitSha, cancellationToken);

    #endregion

    #region Rebase Operations

    public Task<MergeResult> RebaseAsync(string repoPath, string ontoBranch, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        => _rebaseOps.RebaseAsync(repoPath, ontoBranch, progress, cancellationToken);

    public Task AbortRebaseAsync(string repoPath, CancellationToken cancellationToken = default)
        => _rebaseOps.AbortRebaseAsync(repoPath, cancellationToken);

    public Task<MergeResult> ContinueRebaseAsync(string repoPath, CancellationToken cancellationToken = default)
        => _rebaseOps.ContinueRebaseAsync(repoPath, cancellationToken);

    public Task<MergeResult> SkipRebaseCommitAsync(string repoPath, CancellationToken cancellationToken = default)
        => _rebaseOps.SkipRebaseCommitAsync(repoPath, cancellationToken);

    public Task<bool> IsRebaseInProgressAsync(string repoPath, CancellationToken cancellationToken = default)
        => _rebaseOps.IsRebaseInProgressAsync(repoPath, cancellationToken);

    public Task<bool> IsAmInProgressAsync(string repoPath, CancellationToken cancellationToken = default)
        => _amOps.IsAmInProgressAsync(repoPath, cancellationToken);

    public Task<MergeResult> ContinueAmAsync(string repoPath, CancellationToken cancellationToken = default)
        => _amOps.ContinueAmAsync(repoPath, cancellationToken);

    public Task<MergeResult> SkipAmAsync(string repoPath, CancellationToken cancellationToken = default)
        => _amOps.SkipAmAsync(repoPath, cancellationToken);

    public Task AbortAmAsync(string repoPath, CancellationToken cancellationToken = default)
        => _amOps.AbortAmAsync(repoPath, cancellationToken);

    #endregion

    #region Stash Operations

    public Task StashAsync(string repoPath, string? message = null, CancellationToken cancellationToken = default)
        => _stashOps.StashAsync(repoPath, message, cancellationToken);

    public Task StashStagedAsync(string repoPath, string? message = null, CancellationToken cancellationToken = default)
        => _stashOps.StashStagedAsync(repoPath, message, cancellationToken);

    public Task<MergeResult> PopStashAsync(string repoPath, CancellationToken cancellationToken = default)
        => _stashOps.PopStashAsync(repoPath, cancellationToken);

    public Task<MergeResult> PopStashAsync(string repoPath, int stashIndex, CancellationToken cancellationToken = default)
        => _stashOps.PopStashAsync(repoPath, stashIndex, cancellationToken);

    public Task<List<StashInfo>> GetStashesAsync(string repoPath, CancellationToken cancellationToken = default)
        => _stashOps.GetStashesAsync(repoPath, cancellationToken);

    public Task DeleteStashAsync(string repoPath, int stashIndex, CancellationToken cancellationToken = default)
        => _stashOps.DeleteStashAsync(repoPath, stashIndex, cancellationToken);

    public Task CleanupTempStashAsync(string repoPath, CancellationToken cancellationToken = default)
        => _stashOps.CleanupTempStashAsync(repoPath, cancellationToken);

    #endregion

    #region Tag Operations

    public Task<List<TagInfo>> GetTagsAsync(string repoPath, CancellationToken cancellationToken = default)
        => _tagOps.GetTagsAsync(repoPath, cancellationToken);

    public Task CreateTagAsync(string repoPath, string tagName, string? message = null, string? targetSha = null, CancellationToken cancellationToken = default)
        => _tagOps.CreateTagAsync(repoPath, tagName, message, targetSha, cancellationToken);

    public Task DeleteTagAsync(string repoPath, string tagName, CancellationToken cancellationToken = default)
        => _tagOps.DeleteTagAsync(repoPath, tagName, cancellationToken);

    public Task PushTagAsync(string repoPath, string tagName, string remoteName = "origin", string? credentialKey = null, CancellationToken cancellationToken = default)
        => _tagOps.PushTagAsync(repoPath, tagName, remoteName, credentialKey, cancellationToken);

    public Task DeleteRemoteTagAsync(string repoPath, string tagName, string remoteName = "origin", string? credentialKey = null, CancellationToken cancellationToken = default)
        => _tagOps.DeleteRemoteTagAsync(repoPath, tagName, remoteName, credentialKey, cancellationToken);

    #endregion

    #region Hunk Operations

    public Task RevertHunkAsync(string repoPath, string patchContent, CancellationToken cancellationToken = default)
        => _hunkOps.RevertHunkAsync(repoPath, patchContent, cancellationToken);

    public Task StageHunkAsync(string repoPath, string patchContent, CancellationToken cancellationToken = default)
        => _hunkOps.StageHunkAsync(repoPath, patchContent, cancellationToken);

    public Task UnstageHunkAsync(string repoPath, string patchContent, CancellationToken cancellationToken = default)
        => _hunkOps.UnstageHunkAsync(repoPath, patchContent, cancellationToken);

    #endregion

    #region Config Operations

    public Task SetConfigAsync(string repoPath, string key, string value, GitConfigScope scope = GitConfigScope.Local, CancellationToken cancellationToken = default)
        => _configOps.SetConfigAsync(repoPath, key, value, scope, cancellationToken);

    public Task<string?> GetConfigAsync(string repoPath, string key, GitConfigScope scope = GitConfigScope.Local, CancellationToken cancellationToken = default)
        => _configOps.GetConfigAsync(repoPath, key, scope, cancellationToken);

    public Task UnsetConfigAsync(string repoPath, string key, GitConfigScope scope = GitConfigScope.Local, CancellationToken cancellationToken = default)
        => _configOps.UnsetConfigAsync(repoPath, key, scope, cancellationToken);

    #endregion

    #region Worktree Operations

    public Task<List<WorktreeInfo>> GetWorktreesAsync(string repoPath, CancellationToken cancellationToken = default)
        => _worktreeOps.GetWorktreesAsync(repoPath, cancellationToken);

    public Task CreateWorktreeAsync(string repoPath, string worktreePath, string branchName, CancellationToken cancellationToken = default)
        => _worktreeOps.CreateWorktreeAsync(repoPath, worktreePath, branchName, cancellationToken);

    public Task CreateWorktreeWithNewBranchAsync(string repoPath, string worktreePath, string newBranchName, string? startPoint = null, CancellationToken cancellationToken = default)
        => _worktreeOps.CreateWorktreeWithNewBranchAsync(repoPath, worktreePath, newBranchName, startPoint, cancellationToken);

    public Task CreateWorktreeDetachedAsync(string repoPath, string worktreePath, string commitSha, CancellationToken cancellationToken = default)
        => _worktreeOps.CreateWorktreeDetachedAsync(repoPath, worktreePath, commitSha, cancellationToken);

    public Task RemoveWorktreeAsync(string repoPath, string worktreePath, bool force = false, CancellationToken cancellationToken = default)
        => _worktreeOps.RemoveWorktreeAsync(repoPath, worktreePath, force, cancellationToken);

    public Task LockWorktreeAsync(string repoPath, string worktreePath, string? reason = null, CancellationToken cancellationToken = default)
        => _worktreeOps.LockWorktreeAsync(repoPath, worktreePath, reason, cancellationToken);

    public Task UnlockWorktreeAsync(string repoPath, string worktreePath, CancellationToken cancellationToken = default)
        => _worktreeOps.UnlockWorktreeAsync(repoPath, worktreePath, cancellationToken);

    public Task PruneWorktreesAsync(string repoPath, CancellationToken cancellationToken = default)
        => _worktreeOps.PruneWorktreesAsync(repoPath, cancellationToken);

    #endregion

    #region Submodule Operations

    public Task<List<SubmoduleInfo>> GetSubmodulesAsync(string repoPath, CancellationToken cancellationToken = default)
        => _submoduleOps.GetSubmodulesAsync(repoPath, cancellationToken);

    public Task InitAndUpdateSubmodulesAsync(string repoPath, IReadOnlyList<string> paths, bool recursive, CancellationToken cancellationToken = default)
        => _submoduleOps.InitAndUpdateAsync(repoPath, paths, recursive, cancellationToken);

    public Task SyncSubmodulesAsync(string repoPath, IReadOnlyList<string> paths, bool recursive, CancellationToken cancellationToken = default)
        => _submoduleOps.SyncAsync(repoPath, paths, recursive, cancellationToken);

    public Task DeinitSubmoduleAsync(string repoPath, string path, bool force, CancellationToken cancellationToken = default)
        => _submoduleOps.DeinitAsync(repoPath, path, force, cancellationToken);

    public Task AddSubmoduleAsync(string repoPath, string url, string path, string? branch, CancellationToken cancellationToken = default)
        => _submoduleOps.AddAsync(repoPath, url, path, branch, cancellationToken);

    public Task UpdateSubmoduleToRemoteAsync(string repoPath, string path, CancellationToken cancellationToken = default)
        => _submoduleOps.UpdateToRemoteAsync(repoPath, path, cancellationToken);

    public Task RemoveSubmoduleAsync(string repoPath, SubmoduleInfo submodule, CancellationToken cancellationToken = default)
        => _submoduleOps.RemoveAsync(repoPath, submodule, cancellationToken);

    #endregion

    #region Reflog Operations

    public Task<List<ReflogEntry>> GetReflogAsync(string repoPath, CancellationToken cancellationToken = default)
        => _reflogOps.GetReflogAsync(repoPath, cancellationToken);

    #endregion
}
