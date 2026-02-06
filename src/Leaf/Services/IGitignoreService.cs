using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for managing .gitignore entries and untracking files.
/// </summary>
public interface IGitignoreService
{
    /// <summary>
    /// Adds the file path to .gitignore and untracks if currently tracked.
    /// </summary>
    /// <param name="repoPath">Repository root path</param>
    /// <param name="file">File to ignore</param>
    Task IgnoreFileAsync(string repoPath, FileStatusInfo file);

    /// <summary>
    /// Adds all files with the file's extension to .gitignore and untracks the file if tracked.
    /// </summary>
    /// <param name="repoPath">Repository root path</param>
    /// <param name="file">File whose extension should be ignored</param>
    Task IgnoreExtensionAsync(string repoPath, FileStatusInfo file);

    /// <summary>
    /// Adds all files in the file's directory to .gitignore and untracks the file if tracked.
    /// </summary>
    /// <param name="repoPath">Repository root path</param>
    /// <param name="file">File whose directory should be ignored</param>
    Task IgnoreDirectoryAsync(string repoPath, FileStatusInfo file);

    /// <summary>
    /// Adds a directory path to .gitignore and untracks all tracked files within it.
    /// </summary>
    /// <param name="repoPath">Repository root path</param>
    /// <param name="directoryPath">Relative directory path to ignore (forward slash separated)</param>
    /// <param name="trackedFiles">Files within the directory that need to be untracked</param>
    Task IgnoreDirectoryPathAsync(string repoPath, string directoryPath, IEnumerable<FileStatusInfo> trackedFiles);
}
