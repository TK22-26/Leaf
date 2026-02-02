using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for tag operations.
/// </summary>
/// <remarks>
/// This service is stateless - receives IRepositorySession for each operation.
/// All methods are safe to call concurrently for different sessions.
/// </remarks>
public interface ITagService
{
    /// <summary>
    /// Gets all tags in the repository.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <returns>List of tag information.</returns>
    Task<IReadOnlyList<TagInfo>> GetTagsAsync(IRepositorySession session);

    /// <summary>
    /// Creates a new tag.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="tagName">Name for the tag.</param>
    /// <param name="message">Optional message (creates annotated tag if provided).</param>
    /// <param name="targetSha">Optional target commit (defaults to HEAD).</param>
    Task CreateTagAsync(
        IRepositorySession session,
        string tagName,
        string? message = null,
        string? targetSha = null);

    /// <summary>
    /// Deletes a local tag.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="tagName">Name of the tag to delete.</param>
    Task DeleteTagAsync(IRepositorySession session, string tagName);

    /// <summary>
    /// Pushes a tag to a remote.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="tagName">Name of the tag to push.</param>
    /// <param name="remoteName">Remote name (default: origin).</param>
    /// <param name="username">Optional username for authentication.</param>
    /// <param name="password">Optional password/token for authentication.</param>
    Task PushTagAsync(
        IRepositorySession session,
        string tagName,
        string remoteName = "origin",
        string? username = null,
        string? password = null);

    /// <summary>
    /// Deletes a tag from a remote.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="tagName">Name of the tag to delete.</param>
    /// <param name="remoteName">Remote name (default: origin).</param>
    /// <param name="username">Optional username for authentication.</param>
    /// <param name="password">Optional password/token for authentication.</param>
    Task DeleteRemoteTagAsync(
        IRepositorySession session,
        string tagName,
        string remoteName = "origin",
        string? username = null,
        string? password = null);
}
