using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for managing git stash operations.
/// Currently delegates to GitService for backward compatibility.
/// </summary>
public class StashService : IStashService
{
    private readonly IGitService _gitService;
    private readonly IRepositoryEventHub _eventHub;

    public StashService(IGitService gitService, IRepositoryEventHub eventHub)
    {
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
    }

    /// <inheritdoc />
    public async Task StashAsync(IRepositorySession session, string? message = null)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.StashAsync(session.RepositoryPath, message);
        _eventHub.NotifyStashesChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
    }

    /// <inheritdoc />
    public async Task StashStagedAsync(IRepositorySession session, string? message = null)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.StashStagedAsync(session.RepositoryPath, message);
        _eventHub.NotifyStashesChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
    }

    /// <inheritdoc />
    public async Task<MergeResult> PopStashAsync(IRepositorySession session)
    {
        return await PopStashAsync(session, 0);
    }

    /// <inheritdoc />
    public async Task<MergeResult> PopStashAsync(IRepositorySession session, int stashIndex)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        var result = await _gitService.PopStashAsync(session.RepositoryPath, stashIndex);
        _eventHub.NotifyStashesChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
        if (result.HasConflicts)
        {
            _eventHub.NotifyConflictStateChanged();
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StashInfo>> GetStashesAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.GetStashesAsync(session.RepositoryPath);
    }

    /// <inheritdoc />
    public async Task DeleteStashAsync(IRepositorySession session, int stashIndex)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.DeleteStashAsync(session.RepositoryPath, stashIndex);
        _eventHub.NotifyStashesChanged();
    }

    /// <inheritdoc />
    public async Task CleanupTempStashAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.CleanupTempStashAsync(session.RepositoryPath);
        _eventHub.NotifyStashesChanged();
    }
}
