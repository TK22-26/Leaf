using System.Collections.Concurrent;
using System.IO;
using System.Timers;

namespace Leaf.Services;

/// <summary>
/// Service for monitoring folders and detecting new Git repositories.
/// Uses FileSystemWatcher to detect new directories and checks for .git folders.
/// </summary>
public class FolderWatcherService : IFolderWatcherService
{
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, System.Timers.Timer> _debounceTimers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    private const int DebounceMs = 500;

    public event EventHandler<string>? RepositoryDiscovered;

    public void StartWatching(IEnumerable<string> folderPaths)
    {
        foreach (var path in folderPaths)
        {
            AddWatchedFolder(path);
        }
    }

    public void AddWatchedFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return;

        if (_watchers.ContainsKey(folderPath))
            return;

        try
        {
            var watcher = new FileSystemWatcher(folderPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };

            watcher.Created += OnDirectoryCreated;
            watcher.Renamed += OnDirectoryRenamed;

            if (_watchers.TryAdd(folderPath, watcher))
            {
                var timer = new System.Timers.Timer(DebounceMs)
                {
                    AutoReset = false
                };
                timer.Elapsed += (s, e) => OnDebounceElapsed(folderPath);
                _debounceTimers.TryAdd(folderPath, timer);
            }
            else
            {
                watcher.Dispose();
            }
        }
        catch (Exception)
        {
            // Silently fail if we can't watch the directory
        }
    }

    public void RemoveWatchedFolder(string folderPath)
    {
        if (_watchers.TryRemove(folderPath, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnDirectoryCreated;
            watcher.Renamed -= OnDirectoryRenamed;
            watcher.Dispose();
        }

        if (_debounceTimers.TryRemove(folderPath, out var timer))
        {
            timer.Stop();
            timer.Dispose();
        }

        _pendingPaths.TryRemove(folderPath, out _);
    }

    public void StopAll()
    {
        foreach (var path in _watchers.Keys.ToList())
        {
            RemoveWatchedFolder(path);
        }
    }

    public Task<IEnumerable<string>> ScanFolderAsync(string folderPath)
    {
        return Task.Run(() => ScanFolder(folderPath));
    }

    private IEnumerable<string> ScanFolder(string folderPath)
    {
        var repos = new List<string>();

        if (!Directory.Exists(folderPath))
            return repos;

        try
        {
            var gitDirs = Directory.GetDirectories(folderPath, ".git", SearchOption.AllDirectories);
            foreach (var gitDir in gitDirs)
            {
                var repoPath = Path.GetDirectoryName(gitDir);
                if (repoPath != null)
                {
                    repos.Add(repoPath);
                }
            }
        }
        catch (Exception)
        {
            // Ignore access errors during scan
        }

        return repos;
    }

    private void OnDirectoryCreated(object sender, FileSystemEventArgs e)
    {
        HandlePotentialRepo(e.FullPath);
    }

    private void OnDirectoryRenamed(object sender, RenamedEventArgs e)
    {
        HandlePotentialRepo(e.FullPath);
    }

    private void HandlePotentialRepo(string path)
    {
        // Check if this is a .git directory being created
        if (Path.GetFileName(path).Equals(".git", StringComparison.OrdinalIgnoreCase))
        {
            var repoPath = Path.GetDirectoryName(path);
            if (repoPath != null)
            {
                ScheduleRepoCheck(repoPath);
            }
            return;
        }

        // Check if this new directory contains a .git folder (e.g., cloned repo)
        var gitPath = Path.Combine(path, ".git");
        if (Directory.Exists(gitPath))
        {
            ScheduleRepoCheck(path);
            return;
        }

        // For new directories, watch for .git to appear shortly after (git clone creates dir first)
        ScheduleRepoCheck(path);
    }

    private void ScheduleRepoCheck(string repoPath)
    {
        // Find which watched folder this path belongs to
        var watchedFolder = _watchers.Keys
            .FirstOrDefault(f => repoPath.StartsWith(f, StringComparison.OrdinalIgnoreCase));

        if (watchedFolder == null)
            return;

        // Store the path to check and restart debounce timer
        _pendingPaths.AddOrUpdate(repoPath, repoPath, (_, _) => repoPath);

        if (_debounceTimers.TryGetValue(watchedFolder, out var timer))
        {
            timer.Stop();
            timer.Start();
        }
    }

    private void OnDebounceElapsed(string watchedFolder)
    {
        // Get all pending paths for this watched folder
        var pathsToCheck = _pendingPaths.Keys
            .Where(p => p.StartsWith(watchedFolder, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var path in pathsToCheck)
        {
            _pendingPaths.TryRemove(path, out _);

            // Verify this is actually a git repository
            var gitPath = Path.Combine(path, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                RepositoryDiscovered?.Invoke(this, path);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        StopAll();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
