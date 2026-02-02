using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for managing the git staging area (index).
/// Delegates to GitService for backward compatibility.
/// </summary>
public class StagingService : IStagingService
{
    private readonly IGitService _gitService;
    private readonly IRepositoryEventHub _eventHub;

    public StagingService(IGitService gitService, IRepositoryEventHub eventHub)
    {
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
    }

    /// <inheritdoc />
    public async Task<WorkingChangesInfo> GetWorkingChangesAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.GetWorkingChangesAsync(session.RepositoryPath);
    }

    /// <inheritdoc />
    public async Task<string> GetWorkingChangesPatchAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.GetWorkingChangesPatchAsync(session.RepositoryPath);
    }

    /// <inheritdoc />
    public async Task<string> GetStagedSummaryAsync(
        IRepositorySession session,
        int maxFiles = 100,
        int maxDiffChars = 50000)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.GetStagedSummaryAsync(session.RepositoryPath, maxFiles, maxDiffChars);
    }

    /// <inheritdoc />
    public async Task StageFileAsync(IRepositorySession session, string filePath)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.StageFileAsync(session.RepositoryPath, filePath);
        _eventHub.NotifyWorkingDirectoryChanged();
    }

    /// <inheritdoc />
    public async Task UnstageFileAsync(IRepositorySession session, string filePath)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.UnstageFileAsync(session.RepositoryPath, filePath);
        _eventHub.NotifyWorkingDirectoryChanged();
    }

    /// <inheritdoc />
    public async Task UntrackFileAsync(IRepositorySession session, string filePath)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.UntrackFileAsync(session.RepositoryPath, filePath);
        _eventHub.NotifyWorkingDirectoryChanged();
    }

    /// <inheritdoc />
    public async Task StageAllAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.StageAllAsync(session.RepositoryPath);
        _eventHub.NotifyWorkingDirectoryChanged();
    }

    /// <inheritdoc />
    public async Task UnstageAllAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.UnstageAllAsync(session.RepositoryPath);
        _eventHub.NotifyWorkingDirectoryChanged();
    }

    /// <inheritdoc />
    public async Task DiscardAllChangesAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.DiscardAllChangesAsync(session.RepositoryPath);
        _eventHub.NotifyWorkingDirectoryChanged();
    }

    /// <inheritdoc />
    public async Task DiscardFileChangesAsync(IRepositorySession session, string filePath)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.DiscardFileChangesAsync(session.RepositoryPath, filePath);
        _eventHub.NotifyWorkingDirectoryChanged();
    }
}
