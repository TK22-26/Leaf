using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for accessing git commit history.
/// </summary>
/// <remarks>
/// This service is stateless - receives IRepositorySession for each operation.
/// All methods are safe to call concurrently for different sessions.
/// </remarks>
public interface ICommitHistoryService
{
    /// <summary>
    /// Gets the commit history.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="count">Maximum number of commits to retrieve.</param>
    /// <param name="branchName">Optional branch name to get history for.</param>
    /// <param name="skip">Number of commits to skip (for lazy loading).</param>
    /// <returns>List of commit information.</returns>
    Task<IReadOnlyList<CommitInfo>> GetCommitHistoryAsync(
        IRepositorySession session,
        int count = 500,
        string? branchName = null,
        int skip = 0);

    /// <summary>
    /// Gets a specific commit by SHA.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="sha">Commit SHA.</param>
    /// <returns>Commit information, or null if not found.</returns>
    Task<CommitInfo?> GetCommitAsync(IRepositorySession session, string sha);

    /// <summary>
    /// Gets the files changed in a commit.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="sha">Commit SHA.</param>
    /// <returns>List of file changes.</returns>
    Task<IReadOnlyList<FileChangeInfo>> GetCommitChangesAsync(IRepositorySession session, string sha);

    /// <summary>
    /// Gets the diff for a file in a specific commit.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="sha">Commit SHA.</param>
    /// <param name="filePath">Relative path to the file.</param>
    /// <returns>Tuple of (old content, new content).</returns>
    Task<(string oldContent, string newContent)> GetFileDiffAsync(
        IRepositorySession session,
        string sha,
        string filePath);

    /// <summary>
    /// Gets the diff between a commit and the working tree.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="commitSha">Commit SHA to compare from.</param>
    /// <returns>Diff string.</returns>
    Task<string> GetCommitToWorkingTreeDiffAsync(IRepositorySession session, string commitSha);

    /// <summary>
    /// Searches commits by message, author, or SHA.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="searchText">Search query.</param>
    /// <param name="maxResults">Maximum number of results.</param>
    /// <returns>List of matching commits.</returns>
    Task<IReadOnlyList<CommitInfo>> SearchCommitsAsync(
        IRepositorySession session,
        string searchText,
        int maxResults = 100);

    /// <summary>
    /// Gets commits between two refs.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="fromRef">Starting reference (exclusive).</param>
    /// <param name="toRef">Ending reference (inclusive). Defaults to HEAD.</param>
    /// <returns>List of commits in range.</returns>
    Task<IReadOnlyList<CommitInfo>> GetCommitsBetweenAsync(
        IRepositorySession session,
        string fromRef,
        string? toRef = null);

    /// <summary>
    /// Gets merge commits for a specific merge.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="mergeSha">SHA of the merge commit.</param>
    /// <returns>List of commits that were merged.</returns>
    Task<IReadOnlyList<CommitInfo>> GetMergeCommitsAsync(IRepositorySession session, string mergeSha);

    /// <summary>
    /// Gets the history of a specific file.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="filePath">Relative path to the file.</param>
    /// <param name="maxCount">Maximum number of commits to retrieve.</param>
    /// <returns>List of commits that modified the file.</returns>
    Task<IReadOnlyList<CommitInfo>> GetFileHistoryAsync(
        IRepositorySession session,
        string filePath,
        int maxCount = 200);

    /// <summary>
    /// Gets blame information for a file.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="filePath">Relative path to the file.</param>
    /// <returns>List of blame lines.</returns>
    Task<IReadOnlyList<FileBlameLine>> GetFileBlameAsync(IRepositorySession session, string filePath);
}
