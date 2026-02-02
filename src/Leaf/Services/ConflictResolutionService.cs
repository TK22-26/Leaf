using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for managing merge conflict detection and resolution.
/// Delegates to GitService for backward compatibility.
/// </summary>
public class ConflictResolutionService : IConflictResolutionService
{
    private readonly IGitService _gitService;
    private readonly IRepositoryEventHub _eventHub;

    public ConflictResolutionService(IGitService gitService, IRepositoryEventHub eventHub)
    {
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConflictInfo>> GetConflictsAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.GetConflictsAsync(session.RepositoryPath);
    }

    /// <inheritdoc />
    public async Task ResolveWithOursAsync(IRepositorySession session, string filePath)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.ResolveConflictWithOursAsync(session.RepositoryPath, filePath);
        _eventHub.NotifyConflictStateChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
    }

    /// <inheritdoc />
    public async Task ResolveWithTheirsAsync(IRepositorySession session, string filePath)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.ResolveConflictWithTheirsAsync(session.RepositoryPath, filePath);
        _eventHub.NotifyConflictStateChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
    }

    /// <inheritdoc />
    public async Task MarkResolvedAsync(IRepositorySession session, string filePath)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.MarkConflictResolvedAsync(session.RepositoryPath, filePath);
        _eventHub.NotifyConflictStateChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
    }

    /// <inheritdoc />
    public async Task ReopenConflictAsync(
        IRepositorySession session,
        string filePath,
        string baseContent,
        string oursContent,
        string theirsContent)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.ReopenConflictAsync(
            session.RepositoryPath, filePath, baseContent, oursContent, theirsContent);
        _eventHub.NotifyConflictStateChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConflictInfo>> GetResolvedFilesAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.GetResolvedMergeFilesAsync(session.RepositoryPath);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetStoredConflictFilesAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.GetStoredMergeConflictFilesAsync(session.RepositoryPath);
    }

    /// <inheritdoc />
    public async Task SaveStoredConflictFilesAsync(IRepositorySession session, IEnumerable<string> files)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.SaveStoredMergeConflictFilesAsync(session.RepositoryPath, files);
    }

    /// <inheritdoc />
    public async Task ClearStoredConflictFilesAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.ClearStoredMergeConflictFilesAsync(session.RepositoryPath);
    }

    /// <inheritdoc />
    public async Task OpenInVsCodeAsync(IRepositorySession session, string filePath)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.OpenConflictInVsCodeAsync(session.RepositoryPath, filePath);
    }
}
