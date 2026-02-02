using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for merge operations.
/// Delegates to GitService for backward compatibility.
/// </summary>
public class MergeService : IMergeService
{
    private readonly IGitService _gitService;
    private readonly IRepositoryEventHub _eventHub;

    public MergeService(IGitService gitService, IRepositoryEventHub eventHub)
    {
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
    }

    /// <inheritdoc />
    public async Task<MergeResult> MergeBranchAsync(
        IRepositorySession session,
        string branchName,
        bool allowUnrelatedHistories = false)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        var result = await _gitService.MergeBranchAsync(
            session.RepositoryPath, branchName, allowUnrelatedHistories);

        _eventHub.NotifyCommitHistoryChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
        _eventHub.NotifyBranchesChanged();

        if (result.HasConflicts)
        {
            _eventHub.NotifyConflictStateChanged();
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<MergeResult> FastForwardAsync(IRepositorySession session, string targetBranchName)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        var result = await _gitService.FastForwardAsync(session.RepositoryPath, targetBranchName);

        _eventHub.NotifyCommitHistoryChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
        _eventHub.NotifyBranchesChanged();

        return result;
    }

    /// <inheritdoc />
    public async Task<MergeResult> SquashMergeAsync(IRepositorySession session, string branchName)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        var result = await _gitService.SquashMergeAsync(session.RepositoryPath, branchName);

        _eventHub.NotifyWorkingDirectoryChanged();

        if (result.HasConflicts)
        {
            _eventHub.NotifyConflictStateChanged();
        }

        return result;
    }

    /// <inheritdoc />
    public async Task CompleteMergeAsync(IRepositorySession session, string commitMessage)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.CompleteMergeAsync(session.RepositoryPath, commitMessage);

        _eventHub.NotifyCommitHistoryChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
        _eventHub.NotifyConflictStateChanged();
        _eventHub.NotifyBranchesChanged();
    }

    /// <inheritdoc />
    public async Task AbortMergeAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.AbortMergeAsync(session.RepositoryPath);

        _eventHub.NotifyWorkingDirectoryChanged();
        _eventHub.NotifyConflictStateChanged();
    }
}
