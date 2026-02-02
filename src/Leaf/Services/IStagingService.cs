using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for managing the git staging area (index).
/// </summary>
/// <remarks>
/// This service is stateless - receives IRepositorySession for each operation.
/// All methods are safe to call concurrently for different sessions.
/// </remarks>
public interface IStagingService
{
    /// <summary>
    /// Gets all working directory changes (staged and unstaged).
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <returns>Working changes information.</returns>
    Task<WorkingChangesInfo> GetWorkingChangesAsync(IRepositorySession session);

    /// <summary>
    /// Gets a patch representing all working directory changes.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <returns>Unified diff patch string.</returns>
    Task<string> GetWorkingChangesPatchAsync(IRepositorySession session);

    /// <summary>
    /// Gets a summary of staged changes for commit preview.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="maxFiles">Maximum number of files to include.</param>
    /// <param name="maxDiffChars">Maximum characters for diff content.</param>
    /// <returns>Summary string.</returns>
    Task<string> GetStagedSummaryAsync(IRepositorySession session, int maxFiles = 100, int maxDiffChars = 50000);

    /// <summary>
    /// Stages a single file for commit.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="filePath">Relative path to the file.</param>
    Task StageFileAsync(IRepositorySession session, string filePath);

    /// <summary>
    /// Unstages a single file (removes from staging area).
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="filePath">Relative path to the file.</param>
    Task UnstageFileAsync(IRepositorySession session, string filePath);

    /// <summary>
    /// Untracks a file (removes from version control but keeps the file).
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="filePath">Relative path to the file.</param>
    Task UntrackFileAsync(IRepositorySession session, string filePath);

    /// <summary>
    /// Stages all modified files for commit.
    /// </summary>
    /// <param name="session">Repository session.</param>
    Task StageAllAsync(IRepositorySession session);

    /// <summary>
    /// Unstages all files (removes all from staging area).
    /// </summary>
    /// <param name="session">Repository session.</param>
    Task UnstageAllAsync(IRepositorySession session);

    /// <summary>
    /// Discards all working directory changes (destructive - cannot be undone).
    /// </summary>
    /// <param name="session">Repository session.</param>
    Task DiscardAllChangesAsync(IRepositorySession session);

    /// <summary>
    /// Discards changes to a single file (destructive - cannot be undone).
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="filePath">Relative path to the file.</param>
    Task DiscardFileChangesAsync(IRepositorySession session, string filePath);
}
