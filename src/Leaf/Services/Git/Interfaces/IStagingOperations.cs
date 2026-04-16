namespace Leaf.Services.Git.Interfaces;

/// <summary>
/// Internal interface for staging-related operations.
/// Used to prevent circular dependencies between operations.
/// </summary>
internal interface IStagingOperations
{
    /// <summary>
    /// Stage a file for commit.
    /// </summary>
    Task StageFileAsync(string repoPath, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unstage a file (remove from staging area).
    /// </summary>
    Task UnstageFileAsync(string repoPath, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stage all changes.
    /// </summary>
    Task StageAllAsync(string repoPath, CancellationToken cancellationToken = default);
}
