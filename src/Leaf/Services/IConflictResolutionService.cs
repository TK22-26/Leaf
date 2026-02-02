using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for managing merge conflict detection and resolution.
/// </summary>
/// <remarks>
/// This service is stateless - receives IRepositorySession for each operation.
/// All methods are safe to call concurrently for different sessions.
/// </remarks>
public interface IConflictResolutionService
{
    /// <summary>
    /// Gets all files currently in conflict.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <returns>List of conflict information for each conflicted file.</returns>
    Task<IReadOnlyList<ConflictInfo>> GetConflictsAsync(IRepositorySession session);

    /// <summary>
    /// Resolves a conflict by keeping the current branch version (ours).
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="filePath">Relative path to the conflicted file.</param>
    Task ResolveWithOursAsync(IRepositorySession session, string filePath);

    /// <summary>
    /// Resolves a conflict by using the incoming branch version (theirs).
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="filePath">Relative path to the conflicted file.</param>
    Task ResolveWithTheirsAsync(IRepositorySession session, string filePath);

    /// <summary>
    /// Marks a conflict as resolved (file has been manually edited).
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="filePath">Relative path to the resolved file.</param>
    Task MarkResolvedAsync(IRepositorySession session, string filePath);

    /// <summary>
    /// Reopens a resolved conflict for further editing.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="filePath">Relative path to the file.</param>
    /// <param name="baseContent">Base version content.</param>
    /// <param name="oursContent">Our version content.</param>
    /// <param name="theirsContent">Their version content.</param>
    Task ReopenConflictAsync(
        IRepositorySession session,
        string filePath,
        string baseContent,
        string oursContent,
        string theirsContent);

    /// <summary>
    /// Gets files that were resolved during the current merge.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <returns>List of resolved conflict information.</returns>
    Task<IReadOnlyList<ConflictInfo>> GetResolvedFilesAsync(IRepositorySession session);

    /// <summary>
    /// Gets stored list of files that were involved in a merge conflict.
    /// Used to track which files to show in the conflict resolution UI.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <returns>List of file paths.</returns>
    Task<IReadOnlyList<string>> GetStoredConflictFilesAsync(IRepositorySession session);

    /// <summary>
    /// Saves the list of files involved in a merge conflict.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="files">File paths to store.</param>
    Task SaveStoredConflictFilesAsync(IRepositorySession session, IEnumerable<string> files);

    /// <summary>
    /// Clears the stored conflict file list.
    /// </summary>
    /// <param name="session">Repository session.</param>
    Task ClearStoredConflictFilesAsync(IRepositorySession session);

    /// <summary>
    /// Opens a conflicted file in VS Code's merge editor.
    /// </summary>
    /// <param name="session">Repository session.</param>
    /// <param name="filePath">Relative path to the conflicted file.</param>
    Task OpenInVsCodeAsync(IRepositorySession session, string filePath);
}
