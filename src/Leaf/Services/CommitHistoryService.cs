using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for accessing git commit history.
/// Delegates to GitService for backward compatibility.
/// </summary>
public class CommitHistoryService : ICommitHistoryService
{
    private readonly IGitService _gitService;

    public CommitHistoryService(IGitService gitService)
    {
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommitInfo>> GetCommitHistoryAsync(
        IRepositorySession session,
        int count = 500,
        string? branchName = null)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.GetCommitHistoryAsync(session.RepositoryPath, count, branchName);
    }

    /// <inheritdoc />
    public async Task<CommitInfo?> GetCommitAsync(IRepositorySession session, string sha)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.GetCommitAsync(session.RepositoryPath, sha);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FileChangeInfo>> GetCommitChangesAsync(IRepositorySession session, string sha)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.GetCommitChangesAsync(session.RepositoryPath, sha);
    }

    /// <inheritdoc />
    public async Task<(string oldContent, string newContent)> GetFileDiffAsync(
        IRepositorySession session,
        string sha,
        string filePath)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.GetFileDiffAsync(session.RepositoryPath, sha, filePath);
    }

    /// <inheritdoc />
    public async Task<string> GetCommitToWorkingTreeDiffAsync(IRepositorySession session, string commitSha)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.GetCommitToWorkingTreeDiffAsync(session.RepositoryPath, commitSha);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommitInfo>> SearchCommitsAsync(
        IRepositorySession session,
        string searchText,
        int maxResults = 100)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.SearchCommitsAsync(session.RepositoryPath, searchText, maxResults);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommitInfo>> GetCommitsBetweenAsync(
        IRepositorySession session,
        string fromRef,
        string? toRef = null)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.GetCommitsBetweenAsync(session.RepositoryPath, fromRef, toRef);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommitInfo>> GetMergeCommitsAsync(IRepositorySession session, string mergeSha)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.GetMergeCommitsAsync(session.RepositoryPath, mergeSha);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommitInfo>> GetFileHistoryAsync(
        IRepositorySession session,
        string filePath,
        int maxCount = 200)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.GetFileHistoryAsync(session.RepositoryPath, filePath, maxCount);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FileBlameLine>> GetFileBlameAsync(IRepositorySession session, string filePath)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.GetFileBlameAsync(session.RepositoryPath, filePath);
    }
}
