namespace Leaf.Services;

/// <summary>
/// Factory for creating <see cref="IRepositorySession"/> instances.
/// Validates that paths are valid git repositories before creating sessions.
/// </summary>
public interface IRepositorySessionFactory
{
    /// <summary>
    /// Creates a session for the given path.
    /// Validates path is a git repository BEFORE returning.
    /// </summary>
    /// <param name="repositoryPath">
    /// Path to open. Can be:
    /// - Repository root (contains .git folder)
    /// - Any subfolder within a repository (will discover root)
    /// - Path to a worktree
    /// - Path to a bare repository
    /// </param>
    /// <returns>A new session bound to the repository.</returns>
    /// <exception cref="ArgumentException">Path is not a valid git repository.</exception>
    IRepositorySession Create(string repositoryPath);

    /// <summary>
    /// Checks if path is a valid git repository without creating a session.
    /// </summary>
    /// <param name="path">Path to check.</param>
    /// <returns>True if path is inside a valid git repository.</returns>
    bool IsValidRepository(string path);

    /// <summary>
    /// Current generation number. Increments each time a session is created.
    /// Used for stale result detection.
    /// </summary>
    long CurrentGeneration { get; }
}
