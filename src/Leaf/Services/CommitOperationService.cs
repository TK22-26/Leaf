using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for commit operations.
/// Delegates to GitService for backward compatibility.
/// </summary>
public class CommitOperationService : ICommitOperationService
{
    private readonly IGitService _gitService;
    private readonly IRepositoryEventHub _eventHub;

    public CommitOperationService(IGitService gitService, IRepositoryEventHub eventHub)
    {
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
    }

    /// <inheritdoc />
    public async Task CommitAsync(IRepositorySession session, string message)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.CommitAsync(session.RepositoryPath, message);
        _eventHub.NotifyCommitHistoryChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
    }

    /// <inheritdoc />
    public async Task<MergeResult> CherryPickAsync(IRepositorySession session, string commitSha)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        var result = await _gitService.CherryPickAsync(session.RepositoryPath, commitSha);
        _eventHub.NotifyCommitHistoryChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
        if (result.HasConflicts)
        {
            _eventHub.NotifyConflictStateChanged();
        }
        return result;
    }

    /// <inheritdoc />
    public async Task RevertCommitAsync(IRepositorySession session, string commitSha)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.RevertCommitAsync(session.RepositoryPath, commitSha);
        _eventHub.NotifyCommitHistoryChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
    }

    /// <inheritdoc />
    public async Task RevertMergeCommitAsync(IRepositorySession session, string commitSha, int parentIndex)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.RevertMergeCommitAsync(session.RepositoryPath, commitSha, parentIndex);
        _eventHub.NotifyCommitHistoryChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
    }

    /// <inheritdoc />
    public async Task<bool> UndoCommitAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        var success = await _gitService.UndoCommitAsync(session.RepositoryPath);
        if (success)
        {
            _eventHub.NotifyCommitHistoryChanged();
            _eventHub.NotifyWorkingDirectoryChanged();
        }
        return success;
    }

    /// <inheritdoc />
    public async Task<bool> RedoCommitAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        var success = await _gitService.RedoCommitAsync(session.RepositoryPath);
        if (success)
        {
            _eventHub.NotifyCommitHistoryChanged();
            _eventHub.NotifyWorkingDirectoryChanged();
        }
        return success;
    }
}
