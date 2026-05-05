using System.Collections.Concurrent;
using System.IO;
using System.Timers;

namespace Leaf.Services;

/// <summary>
/// Payload for <see cref="FileWatcherService.WorkingDirectoryChanged"/>.
/// Carries the (deduplicated) absolute paths that fired the underlying
/// FileSystemWatcher events between the previous debounce tick and this
/// one, so handlers can route updates to specific subsystems (e.g. the
/// per-submodule dirtiness dispatch) instead of wholesale-refreshing.
/// </summary>
public sealed class WorkingDirectoryChangedEventArgs : EventArgs
{
    /// <summary>
    /// Absolute paths that changed in the working directory since the
    /// last fire. Empty (but never null) on watcher restart / overflow,
    /// in which case handlers should treat the event as a wholesale
    /// refresh signal.
    /// </summary>
    public required IReadOnlyCollection<string> ChangedPaths { get; init; }
}

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

    // Accumulator of changed paths between debounce ticks. Concurrent
    // because FileSystemWatcher events fire on background threads.
    // Drained inside the debounce-elapsed handler.
    private readonly ConcurrentDictionary<string, byte> _pendingPaths =
        new(StringComparer.OrdinalIgnoreCase);

    // Debounce intervals in milliseconds
    private const int WorkingDirDebounceMs = 200;
    private const int GitDirDebounceMs = 2000;

    /// <summary>
    /// Raised when files in the working directory change (staged/unstaged changes).
    /// The args carry the deduplicated paths that changed between debounce
    /// ticks so handlers can dispatch surgically (e.g. only the submodules
    /// whose working tree saw an edit get re-statused).
    /// </summary>
    public event EventHandler<WorkingDirectoryChangedEventArgs>? WorkingDirectoryChanged;

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

        // Drop any paths the previous watcher (or a re-entry from the
        // overflow-restart path) accumulated. Without this, the next
        // debounce tick fires WorkingDirectoryChangedEventArgs containing
        // paths from the *previous* repo, which the dispatch helpers
        // would then prefix-match against the *new* repo's submodule
        // roots — usually a no-match, but a real footgun when sibling
        // clones share a parent path or when the same repo is rebuilt.
        _pendingPaths.Clear();

        _currentRepoPath = repoPath;
        Log.Info("FileWatcher", $"Starting watch on {repoPath}");

        // Watch working directory for file changes
        try
        {
            _workingDirWatcher = new FileSystemWatcher(repoPath)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = 65536, // 64KB — default 8KB overflows on rapid changes
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
            _workingDirWatcher.Error += OnWatcherError;
        }
        catch (Exception ex)
        {
            Log.Error("FileWatcher", $"Failed to create working directory watcher for {repoPath}", ex);
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
                    InternalBufferSize = 65536,
                    NotifyFilter = NotifyFilters.LastWrite |
                                  NotifyFilters.FileName |
                                  NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true
                };

                _gitDirWatcher.Changed += OnGitDirChanged;
                _gitDirWatcher.Created += OnGitDirChanged;
                _gitDirWatcher.Deleted += OnGitDirChanged;
                _gitDirWatcher.Renamed += OnGitDirRenamed;
                _gitDirWatcher.Error += OnWatcherError;
            }
            catch (Exception ex)
            {
                Log.Error("FileWatcher", $"Failed to create .git directory watcher for {repoPath}", ex);
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
        if (_currentRepoPath != null)
            Log.Info("FileWatcher", $"Stopping watch on {_currentRepoPath}");
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

        // Clear here too so paths that arrived between the last debounce
        // tick and the stop call don't survive into the next watcher.
        _pendingPaths.Clear();

        _currentRepoPath = null;
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        var ex = e.GetException();
        Log.Error("FileWatcher", $"Watcher error (buffer overflow?): {ex?.Message}", ex);

        // Restart the watcher — after a buffer overflow it stops raising events permanently
        var repoPath = _currentRepoPath;
        if (!string.IsNullOrEmpty(repoPath))
        {
            Log.Info("FileWatcher", $"Restarting watcher for {repoPath}");
            WatchRepository(repoPath);

            // Buffer overflow means we silently lost an unknown set of
            // change events. Fire a wholesale signal (empty ChangedPaths)
            // so handlers refresh everything they care about — matches
            // the contract documented on WorkingDirectoryChangedEventArgs.
            // Without this the dispatch helpers stay stale until the next
            // organic file event.
            WorkingDirectoryChanged?.Invoke(this, new WorkingDirectoryChangedEventArgs
            {
                ChangedPaths = Array.Empty<string>(),
            });
        }
    }

    private void OnWorkingDirChanged(object sender, FileSystemEventArgs e)
    {
        // Ignore .git directory changes (handled by git watcher)
        if (IsInGitDirectory(e.FullPath))
            return;

        // Ignore common temporary/build files
        if (ShouldIgnoreFile(e.FullPath))
            return;

        _pendingPaths[e.FullPath] = 0;

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

        if (!ShouldIgnoreFile(e.FullPath)) _pendingPaths[e.FullPath] = 0;
        if (!ShouldIgnoreFile(e.OldFullPath)) _pendingPaths[e.OldFullPath] = 0;

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
        // Snapshot + clear atomically so paths arriving between the
        // snapshot and the next event don't get dropped.
        var paths = new List<string>(_pendingPaths.Count);
        foreach (var key in _pendingPaths.Keys)
        {
            if (_pendingPaths.TryRemove(key, out _))
                paths.Add(key);
        }

        WorkingDirectoryChanged?.Invoke(this, new WorkingDirectoryChangedEventArgs
        {
            ChangedPaths = paths,
        });
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

        // Ignore common editor temp / build artifacts. We deliberately do
        // NOT filter "anything starting with a dot" — that would drop
        // legitimate tracked files like .gitignore, .gitattributes,
        // .editorconfig, .env, .dockerignore, .npmrc, .babelrc, etc.,
        // making edits to them invisible until the user manually refreshes.
        return fileName.EndsWith("~") ||
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

        // Ignore lock files — transient files created during git operations
        if (relativePath.EndsWith(".lock"))
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

        // Packed refs — branches/tags that have been packed into a single
        // file. `git branch -D <name>` rewrites this file when the ref
        // exists only in packed form (no loose ref under refs/heads/),
        // so without this watch the deletion fires no events at all and
        // the branch stays in Leaf's list as a phantom until the next
        // manual refresh or repo switch.
        if (relativePath.EndsWith("\\packed-refs") || relativePath.EndsWith("/packed-refs"))
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
