using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for managing git stash operations.
/// </summary>
/// <remarks>
/// This service is stateless - receives IRepositorySession for each operation.
/// All methods are safe to call concurrently for different sessions.
/// </remarks>
public interface IStashService
{
    /// <summary>
    /// Creates a stash of all uncommitted changes.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="message">Optional stash message.</param>
    Task StashAsync(IRepositorySession session, string? message = null);

    /// <summary>
    /// Creates a stash of only staged changes (requires Git 2.35+).
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="message">Optional stash message.</param>
    Task StashStagedAsync(IRepositorySession session, string? message = null);

    /// <summary>
    /// Pops the most recent stash.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <returns>Merge result indicating success or conflicts.</returns>
    Task<MergeResult> PopStashAsync(IRepositorySession session);

    /// <summary>
    /// Pops a specific stash by index.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="stashIndex">Zero-based stash index.</param>
    /// <returns>Merge result indicating success or conflicts.</returns>
    Task<MergeResult> PopStashAsync(IRepositorySession session, int stashIndex);

    /// <summary>
    /// Gets all stashes in the repository.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <returns>List of stash information.</returns>
    Task<IReadOnlyList<StashInfo>> GetStashesAsync(IRepositorySession session);

    /// <summary>
    /// Deletes a specific stash by index.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="stashIndex">Zero-based stash index.</param>
    Task DeleteStashAsync(IRepositorySession session, int stashIndex);

    /// <summary>
    /// Cleans up any temporary stashes created by Leaf (e.g., during interrupted operations).
    /// </summary>
    /// <param name="session">Repository session.</param>
    Task CleanupTempStashAsync(IRepositorySession session);
}
