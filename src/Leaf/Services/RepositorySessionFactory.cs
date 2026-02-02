using System.IO;
using LibGit2Sharp;

namespace Leaf.Services;

/// <summary>
/// Factory for creating <see cref="IRepositorySession"/> instances.
/// Handles path normalization, validation, and discovery of git directory.
/// </summary>
public class RepositorySessionFactory : IRepositorySessionFactory
{
    private long _generation;

    /// <inheritdoc />
    public long CurrentGeneration => Interlocked.Read(ref _generation);

    /// <inheritdoc />
    public bool IsValidRepository(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            // Use Discover - it handles subfolders, worktrees, etc.
            return Repository.Discover(path) != null;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public IRepositorySession Create(string userSelectedPath)
    {
        if (string.IsNullOrWhiteSpace(userSelectedPath))
            throw new ArgumentException("Repository path cannot be empty", nameof(userSelectedPath));

        // Normalize path first (handles symlinks, relative paths)
        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(userSelectedPath);
        }
        catch (Exception ex)
        {
            throw new ArgumentException(
                $"Invalid path format: '{userSelectedPath}'", nameof(userSelectedPath), ex);
        }

        // Use Discover as PRIMARY validation (handles subfolders, worktrees, etc.)
        string? gitDir;
        try
        {
            gitDir = Repository.Discover(normalizedPath);
        }
        catch (Exception ex)
        {
            throw new ArgumentException(
                $"Failed to discover repository at '{userSelectedPath}'", nameof(userSelectedPath), ex);
        }

        if (gitDir == null)
        {
            throw new ArgumentException(
                $"'{userSelectedPath}' is not inside a git repository", nameof(userSelectedPath));
        }

        // Normalize gitDir for comparison
        var normalizedGitDir = Path.GetFullPath(gitDir);

        // Validate the discovered .git directory
        if (!Repository.IsValid(gitDir))
        {
            throw new ArgumentException(
                $"Discovered git directory '{gitDir}' is not valid", nameof(userSelectedPath));
        }

        // Get normalized paths and check if bare
        string workingDirectory;
        bool isBare;
        try
        {
            using var tempRepo = new Repository(gitDir);
            isBare = tempRepo.Info.IsBare;
            workingDirectory = isBare
                ? tempRepo.Info.Path
                : tempRepo.Info.WorkingDirectory ?? tempRepo.Info.Path;
        }
        catch (Exception ex)
        {
            throw new ArgumentException(
                $"Failed to open repository at '{gitDir}'", nameof(userSelectedPath), ex);
        }

        // REJECT: Path inside .git directory (only for non-bare repos)
        // For bare repos, normalizedPath == normalizedGitDir is valid (that's how you open them)
        if (!isBare)
        {
            // Check if the user-selected path is inside the .git directory
            if (normalizedPath.StartsWith(normalizedGitDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"'{userSelectedPath}' is inside the .git directory, which is not supported",
                    nameof(userSelectedPath));
            }
        }

        var generation = Interlocked.Increment(ref _generation);
        return new RepositorySession(
            Path.GetFullPath(workingDirectory),
            normalizedGitDir,
            isBare,
            generation);
    }
}
