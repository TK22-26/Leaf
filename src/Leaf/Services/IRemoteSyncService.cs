using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for remote synchronization operations (clone, fetch, pull, push).
/// </summary>
/// <remarks>
/// This service is stateless - receives IRepositorySession for each operation.
/// Clone is a special case that doesn't require an existing session.
/// Credential parameters are storage keys (e.g. "GitHub:microsoft"); the PAT
/// itself is resolved inside Leaf.AskPass.exe and never enters this process.
/// </remarks>
public interface IRemoteSyncService
{
    /// <summary>
    /// Clones a remote repository.
    /// </summary>
    /// <param name="url">Repository URL.</param>
    /// <param name="localPath">Local path to clone to.</param>
    /// <param name="credentialKey">Optional credential storage key for GIT_ASKPASS auth.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <returns>Path to the cloned repository.</returns>
    Task<string> CloneAsync(
        string url,
        string localPath,
        string? credentialKey = null,
        IProgress<string>? progress = null);

    /// <summary>
    /// Fetches from a remote.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="remoteName">Remote name (default: origin).</param>
    /// <param name="credentialKey">Optional credential storage key for GIT_ASKPASS auth.</param>
    /// <param name="progress">Optional progress reporter.</param>
    Task FetchAsync(
        IRepositorySession session,
        string remoteName = "origin",
        string? credentialKey = null,
        IProgress<string>? progress = null);

    /// <summary>
    /// Pulls from the tracking remote.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="credentialKey">Optional credential storage key for GIT_ASKPASS auth.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="rebase">
    /// Force the strategy: <c>true</c> rebases incoming work, <c>false</c> merges it,
    /// <c>null</c> defers to the user's <c>pull.rebase</c> git config.
    /// </param>
    Task PullAsync(
        IRepositorySession session,
        string? credentialKey = null,
        IProgress<string>? progress = null,
        bool? rebase = null);

    /// <summary>
    /// Pushes to a remote.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="remoteName">Remote name (null uses tracking branch's remote or default).</param>
    /// <param name="credentialKey">Optional credential storage key for GIT_ASKPASS auth.</param>
    /// <param name="progress">Optional progress reporter.</param>
    Task PushAsync(
        IRepositorySession session,
        string? remoteName = null,
        string? credentialKey = null,
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
