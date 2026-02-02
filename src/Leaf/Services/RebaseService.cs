using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for rebase operations.
/// Delegates to GitService for backward compatibility.
/// </summary>
public class RebaseService : IRebaseService
{
    private readonly IGitService _gitService;
    private readonly IRepositoryEventHub _eventHub;

    public RebaseService(IGitService gitService, IRepositoryEventHub eventHub)
    {
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
    }

    /// <inheritdoc />
    public async Task<MergeResult> RebaseAsync(
        IRepositorySession session,
        string ontoBranch,
        IProgress<string>? progress = null)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        var result = await _gitService.RebaseAsync(session.RepositoryPath, ontoBranch, progress);

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
    public async Task<MergeResult> ContinueRebaseAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        var result = await _gitService.ContinueRebaseAsync(session.RepositoryPath);

        _eventHub.NotifyCommitHistoryChanged();
        _eventHub.NotifyWorkingDirectoryChanged();

        if (result.HasConflicts)
        {
            _eventHub.NotifyConflictStateChanged();
        }
        else
        {
            _eventHub.NotifyBranchesChanged();
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<MergeResult> SkipRebaseCommitAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        var result = await _gitService.SkipRebaseCommitAsync(session.RepositoryPath);

        _eventHub.NotifyCommitHistoryChanged();
        _eventHub.NotifyWorkingDirectoryChanged();

        if (result.HasConflicts)
        {
            _eventHub.NotifyConflictStateChanged();
        }
        else
        {
            _eventHub.NotifyBranchesChanged();
        }

        return result;
    }

    /// <inheritdoc />
    public async Task AbortRebaseAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.AbortRebaseAsync(session.RepositoryPath);

        _eventHub.NotifyCommitHistoryChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
        _eventHub.NotifyBranchesChanged();
        _eventHub.NotifyConflictStateChanged();
    }

    /// <inheritdoc />
    public async Task<bool> IsRebaseInProgressAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.IsRebaseInProgressAsync(session.RepositoryPath);
    }
}
