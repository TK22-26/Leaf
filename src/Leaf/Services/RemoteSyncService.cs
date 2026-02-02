using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for remote synchronization operations.
/// Delegates to GitService for backward compatibility.
/// </summary>
public class RemoteSyncService : IRemoteSyncService
{
    private readonly IGitService _gitService;
    private readonly IRepositoryEventHub _eventHub;

    public RemoteSyncService(IGitService gitService, IRepositoryEventHub eventHub)
    {
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
    }

    /// <inheritdoc />
    public async Task<string> CloneAsync(
        string url,
        string localPath,
        string? username = null,
        string? password = null,
        IProgress<string>? progress = null)
    {
        return await _gitService.CloneAsync(url, localPath, username, password, progress);
    }

    /// <inheritdoc />
    public async Task FetchAsync(
        IRepositorySession session,
        string remoteName = "origin",
        string? username = null,
        string? password = null,
        IProgress<string>? progress = null)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.FetchAsync(session.RepositoryPath, remoteName, username, password, progress);
        _eventHub.NotifyBranchesChanged();
        _eventHub.NotifyCommitHistoryChanged();
    }

    /// <inheritdoc />
    public async Task PullAsync(
        IRepositorySession session,
        string? username = null,
        string? password = null,
        IProgress<string>? progress = null)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.PullAsync(session.RepositoryPath, username, password, progress);
        _eventHub.NotifyBranchesChanged();
        _eventHub.NotifyCommitHistoryChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
    }

    /// <inheritdoc />
    public async Task PushAsync(
        IRepositorySession session,
        string? username = null,
        string? password = null,
        IProgress<string>? progress = null)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.PushAsync(session.RepositoryPath, username, password, progress);
        _eventHub.NotifyBranchesChanged();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RemoteInfo>> GetRemotesAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.GetRemotesAsync(session.RepositoryPath);
    }

    /// <inheritdoc />
    public async Task<bool> IsHeadPushedAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.IsHeadPushedAsync(session.RepositoryPath);
    }
}
