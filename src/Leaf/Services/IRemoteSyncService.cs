using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for remote synchronization operations (clone, fetch, pull, push).
/// </summary>
/// <remarks>
/// This service is stateless - receives IRepositorySession for each operation.
/// Clone is a special case that doesn't require an existing session.
/// </remarks>
public interface IRemoteSyncService
{
    /// <summary>
    /// Clones a remote repository.
    /// </summary>
    /// <param name="url">Repository URL.</param>
    /// <param name="localPath">Local path to clone to.</param>
    /// <param name="username">Optional username for authentication.</param>
    /// <param name="password">Optional password/token for authentication.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <returns>Path to the cloned repository.</returns>
    Task<string> CloneAsync(
        string url,
        string localPath,
        string? username = null,
        string? password = null,
        IProgress<string>? progress = null);

    /// <summary>
    /// Fetches from a remote.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="remoteName">Remote name (default: origin).</param>
    /// <param name="username">Optional username for authentication.</param>
    /// <param name="password">Optional password/token for authentication.</param>
    /// <param name="progress">Optional progress reporter.</param>
    Task FetchAsync(
        IRepositorySession session,
        string remoteName = "origin",
        string? username = null,
        string? password = null,
        IProgress<string>? progress = null);

    /// <summary>
    /// Pulls from the tracking remote.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="username">Optional username for authentication.</param>
    /// <param name="password">Optional password/token for authentication.</param>
    /// <param name="progress">Optional progress reporter.</param>
    Task PullAsync(
        IRepositorySession session,
        string? username = null,
        string? password = null,
        IProgress<string>? progress = null);

    /// <summary>
    /// Pushes to the tracking remote.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="username">Optional username for authentication.</param>
    /// <param name="password">Optional password/token for authentication.</param>
    /// <param name="progress">Optional progress reporter.</param>
    Task PushAsync(
        IRepositorySession session,
        string? username = null,
        string? password = null,
        IProgress<string>? progress = null);

    /// <summary>
    /// Gets all configured remotes.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <returns>List of remote information.</returns>
    Task<IReadOnlyList<RemoteInfo>> GetRemotesAsync(IRepositorySession session);

    /// <summary>
    /// Checks if HEAD has been pushed to the tracking remote.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <returns>True if HEAD is pushed.</returns>
    Task<bool> IsHeadPushedAsync(IRepositorySession session);
}
