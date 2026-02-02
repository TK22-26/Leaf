using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for tag operations.
/// Delegates to GitService for backward compatibility.
/// </summary>
public class TagService : ITagService
{
    private readonly IGitService _gitService;
    private readonly IRepositoryEventHub _eventHub;

    public TagService(IGitService gitService, IRepositoryEventHub eventHub)
    {
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TagInfo>> GetTagsAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.GetTagsAsync(session.RepositoryPath);
    }

    /// <inheritdoc />
    public async Task CreateTagAsync(
        IRepositorySession session,
        string tagName,
        string? message = null,
        string? targetSha = null)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.CreateTagAsync(session.RepositoryPath, tagName, message, targetSha);
        _eventHub.NotifyCommitHistoryChanged();
    }

    /// <inheritdoc />
    public async Task DeleteTagAsync(IRepositorySession session, string tagName)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.DeleteTagAsync(session.RepositoryPath, tagName);
        _eventHub.NotifyCommitHistoryChanged();
    }

    /// <inheritdoc />
    public async Task PushTagAsync(
        IRepositorySession session,
        string tagName,
        string remoteName = "origin",
        string? username = null,
        string? password = null)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.PushTagAsync(session.RepositoryPath, tagName, remoteName, username, password);
    }

    /// <inheritdoc />
    public async Task DeleteRemoteTagAsync(
        IRepositorySession session,
        string tagName,
        string remoteName = "origin",
        string? username = null,
        string? password = null)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.DeleteRemoteTagAsync(session.RepositoryPath, tagName, remoteName, username, password);
    }
}
