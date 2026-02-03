namespace Leaf.Services;

/// <summary>
/// Service for monitoring folders and detecting new Git repositories.
/// </summary>
public interface IFolderWatcherService : IDisposable
{
    /// <summary>
    /// Raised when a new Git repository is discovered in a watched folder.
    /// The event args contain the repository path.
    /// </summary>
    event EventHandler<string>? RepositoryDiscovered;

    /// <summary>
    /// Start watching the specified folders for new repositories.
    /// </summary>
    void StartWatching(IEnumerable<string> folderPaths);

    /// <summary>
    /// Add a folder to watch for new repositories.
    /// </summary>
    void AddWatchedFolder(string folderPath);

    /// <summary>
    /// Remove a folder from the watch list.
    /// </summary>
    void RemoveWatchedFolder(string folderPath);

    /// <summary>
    /// Stop all folder watchers.
    /// </summary>
    void StopAll();

    /// <summary>
    /// Scan a folder for existing repositories (used on startup).
    /// Returns paths to discovered repositories.
    /// </summary>
    Task<IEnumerable<string>> ScanFolderAsync(string folderPath);
}
