using System.IO;
using System.Timers;

namespace Leaf.Services;

/// <summary>
/// Service for monitoring file system changes in a Git repository.
/// Watches both the working directory (for file changes) and .git directory (for history changes).
/// Uses debouncing to prevent excessive refresh calls.
/// </summary>
public class FileWatcherService : IDisposable
{
    private FileSystemWatcher? _workingDirWatcher;
    private FileSystemWatcher? _gitDirWatcher;
    private System.Timers.Timer? _workingDirDebounceTimer;
    private System.Timers.Timer? _gitDirDebounceTimer;
    private string? _currentRepoPath;
    private bool _disposed;

    // Debounce intervals in milliseconds
    private const int WorkingDirDebounceMs = 200;
    private const int GitDirDebounceMs = 500;

    /// <summary>
    /// Raised when files in the working directory change (staged/unstaged changes).
    /// </summary>
    public event EventHandler? WorkingDirectoryChanged;

    /// <summary>
    /// Raised when the git directory changes (commits, branches, etc.).
    /// </summary>
    public event EventHandler? GitDirectoryChanged;

    /// <summary>
    /// Start watching a repository for changes.
    /// </summary>
    public void WatchRepository(string repoPath)
    {
        if (string.IsNullOrEmpty(repoPath) || !Directory.Exists(repoPath))
            return;

        // Stop any existing watchers
        StopWatching();

        _currentRepoPath = repoPath;

        // Watch working directory for file changes
        try
        {
            _workingDirWatcher = new FileSystemWatcher(repoPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite |
                              NotifyFilters.FileName |
                              NotifyFilters.DirectoryName |
                              NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _workingDirWatcher.Changed += OnWorkingDirChanged;
            _workingDirWatcher.Created += OnWorkingDirChanged;
            _workingDirWatcher.Deleted += OnWorkingDirChanged;
            _workingDirWatcher.Renamed += OnWorkingDirRenamed;
        }
        catch (Exception)
        {
            // Silently fail if we can't watch the directory
        }

        // Watch .git directory for history changes
        var gitDir = Path.Combine(repoPath, ".git");
        if (Directory.Exists(gitDir))
        {
            try
            {
                _gitDirWatcher = new FileSystemWatcher(gitDir)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite |
                                  NotifyFilters.FileName |
                                  NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true
                };

                _gitDirWatcher.Changed += OnGitDirChanged;
                _gitDirWatcher.Created += OnGitDirChanged;
                _gitDirWatcher.Deleted += OnGitDirChanged;
                _gitDirWatcher.Renamed += OnGitDirRenamed;
            }
            catch (Exception)
            {
                // Silently fail if we can't watch the directory
            }
        }

        // Initialize debounce timers
        _workingDirDebounceTimer = new System.Timers.Timer(WorkingDirDebounceMs)
        {
            AutoReset = false
        };
        _workingDirDebounceTimer.Elapsed += OnWorkingDirDebounceElapsed;

        _gitDirDebounceTimer = new System.Timers.Timer(GitDirDebounceMs)
        {
            AutoReset = false
        };
        _gitDirDebounceTimer.Elapsed += OnGitDirDebounceElapsed;
    }

    /// <summary>
    /// Stop watching the current repository.
    /// </summary>
    public void StopWatching()
    {
        if (_workingDirWatcher != null)
        {
            _workingDirWatcher.EnableRaisingEvents = false;
            _workingDirWatcher.Changed -= OnWorkingDirChanged;
            _workingDirWatcher.Created -= OnWorkingDirChanged;
            _workingDirWatcher.Deleted -= OnWorkingDirChanged;
            _workingDirWatcher.Renamed -= OnWorkingDirRenamed;
            _workingDirWatcher.Dispose();
            _workingDirWatcher = null;
        }

        if (_gitDirWatcher != null)
        {
            _gitDirWatcher.EnableRaisingEvents = false;
            _gitDirWatcher.Changed -= OnGitDirChanged;
            _gitDirWatcher.Created -= OnGitDirChanged;
            _gitDirWatcher.Deleted -= OnGitDirChanged;
            _gitDirWatcher.Renamed -= OnGitDirRenamed;
            _gitDirWatcher.Dispose();
            _gitDirWatcher = null;
        }

        _workingDirDebounceTimer?.Stop();
        _workingDirDebounceTimer?.Dispose();
        _workingDirDebounceTimer = null;

        _gitDirDebounceTimer?.Stop();
        _gitDirDebounceTimer?.Dispose();
        _gitDirDebounceTimer = null;

        _currentRepoPath = null;
    }

    private void OnWorkingDirChanged(object sender, FileSystemEventArgs e)
    {
        // Ignore .git directory changes (handled by git watcher)
        if (IsInGitDirectory(e.FullPath))
            return;

        // Ignore common temporary/build files
        if (ShouldIgnoreFile(e.FullPath))
            return;

        // Restart debounce timer
        _workingDirDebounceTimer?.Stop();
        _workingDirDebounceTimer?.Start();
    }

    private void OnWorkingDirRenamed(object sender, RenamedEventArgs e)
    {
        if (IsInGitDirectory(e.FullPath) || IsInGitDirectory(e.OldFullPath))
            return;

        if (ShouldIgnoreFile(e.FullPath) && ShouldIgnoreFile(e.OldFullPath))
            return;

        _workingDirDebounceTimer?.Stop();
        _workingDirDebounceTimer?.Start();
    }

    private void OnGitDirChanged(object sender, FileSystemEventArgs e)
    {
        // Only care about certain git files that indicate real changes
        if (!IsRelevantGitChange(e.FullPath))
            return;

        _gitDirDebounceTimer?.Stop();
        _gitDirDebounceTimer?.Start();
    }

    private void OnGitDirRenamed(object sender, RenamedEventArgs e)
    {
        if (!IsRelevantGitChange(e.FullPath) && !IsRelevantGitChange(e.OldFullPath))
            return;

        _gitDirDebounceTimer?.Stop();
        _gitDirDebounceTimer?.Start();
    }

    private void OnWorkingDirDebounceElapsed(object? sender, ElapsedEventArgs e)
    {
        WorkingDirectoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnGitDirDebounceElapsed(object? sender, ElapsedEventArgs e)
    {
        GitDirectoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool IsInGitDirectory(string path)
    {
        if (string.IsNullOrEmpty(_currentRepoPath))
            return false;

        var gitDir = Path.Combine(_currentRepoPath, ".git");
        return path.StartsWith(gitDir, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldIgnoreFile(string path)
    {
        var fileName = Path.GetFileName(path);
        var extension = Path.GetExtension(path).ToLowerInvariant();

        // Ignore common temporary/build artifacts
        return fileName.EndsWith("~") ||
               fileName.StartsWith(".") ||
               extension == ".tmp" ||
               extension == ".swp" ||
               extension == ".bak" ||
               path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("\\node_modules\\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("\\.vs\\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRelevantGitChange(string path)
    {
        // Only trigger on changes to files that indicate real git state changes
        var relativePath = path.ToLowerInvariant();

        if (relativePath.EndsWith("\\leaf-merge-conflicts.txt") || relativePath.EndsWith("/leaf-merge-conflicts.txt"))
            return false;

        // HEAD changes (checkout, commit)
        if (relativePath.EndsWith("\\head") || relativePath.EndsWith("/head"))
            return true;

        // Index changes (staging)
        if (relativePath.EndsWith("\\index") || relativePath.EndsWith("/index"))
            return true;

        // Branch ref changes
        if (relativePath.Contains("\\refs\\heads\\") || relativePath.Contains("/refs/heads/"))
            return true;

        // Remote ref changes (fetch)
        if (relativePath.Contains("\\refs\\remotes\\") || relativePath.Contains("/refs/remotes/"))
            return true;

        // Tag changes
        if (relativePath.Contains("\\refs\\tags\\") || relativePath.Contains("/refs/tags/"))
            return true;

        // FETCH_HEAD, ORIG_HEAD, etc.
        if (relativePath.EndsWith("_head"))
            return true;

        // Merge state
        if (relativePath.EndsWith("\\merge_head") || relativePath.EndsWith("/merge_head"))
            return true;
        if (relativePath.EndsWith("\\merge_msg") || relativePath.EndsWith("/merge_msg"))
            return true;

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        StopWatching();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
