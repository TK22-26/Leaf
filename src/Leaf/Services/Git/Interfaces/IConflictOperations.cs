namespace Leaf.Services.Git.Interfaces;

/// <summary>
/// Internal interface for conflict-related operations.
/// Used to prevent circular dependencies between operations.
/// </summary>
internal interface IConflictOperations
{
    /// <summary>
    /// Get list of files currently in conflict.
    /// </summary>
    Task<List<string>> GetConflictFilesAsync(string repoPath);

    /// <summary>
    /// Get the count of conflicting files.
    /// </summary>
    Task<int> GetConflictCountAsync(string repoPath);
}
