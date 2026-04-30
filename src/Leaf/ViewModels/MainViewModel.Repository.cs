using System;
using System.IO;
using System.Threading;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Leaf.Composition;
using Leaf.Models;
using Leaf.Services;
using Leaf.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial - Repository management operations.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Starts startup repository restore after the window has rendered once.
    /// </summary>
    public async Task InitializeAfterFirstRenderAsync(bool restoreLastSelection = true)
    {
        if (Interlocked.Exchange(ref _startupInitializationStarted, 1) == 1)
        {
            return;
        }

        // Allow the initial frame and plant animation to start before repo restore work begins.
        await Task.Yield();
        await LoadSavedRepositoriesAsync(restoreLastSelection);
    }

    /// <summary>
    /// Load repositories from persistent storage.
    /// </summary>
    private async Task LoadSavedRepositoriesAsync(bool restoreLastSelection)
    {
        // Load UI state from settings
        var settings = _settingsService.LoadSettings();
        IsRepoPaneCollapsed = settings.IsRepoPaneCollapsed;
        RepoPaneWidth = settings.RepoPaneWidth > 0 ? settings.RepoPaneWidth : 220;
        IsTerminalVisible = settings.IsTerminalVisible;
        TerminalHeight = settings.TerminalHeight > 0 ? settings.TerminalHeight : 220;

        // Load repositories via service
        var lastSelectedPath = await _repositoryService.LoadRepositoriesAsync();

        // Restore last selected repository
        RepositoryInfo? lastRepo = null;
        if (restoreLastSelection && !string.IsNullOrEmpty(lastSelectedPath))
        {
            lastRepo = _repositoryService.FindRepository(lastSelectedPath);

            // If not found, the saved path may be a secondary worktree that was
            // migrated to its main worktree on load — find the parent repo instead
            if (lastRepo == null)
            {
                await LoadWorktreesForAllReposAsync();

                var normalizedPath = Path.GetFullPath(lastSelectedPath);
                lastRepo = RepositoryGroups
                    .SelectMany(g => g.Repositories)
                    .FirstOrDefault(r => r.Worktrees.Any(wt =>
                        string.Equals(Path.GetFullPath(wt.Path), normalizedPath, StringComparison.OrdinalIgnoreCase)));
            }

            if (lastRepo != null)
            {
                Log.Info("Repository", $"Restoring last repo: {lastRepo.Name} ({lastRepo.Path})");
                await SelectRepositoryAsync(lastRepo, fetchInBackground: false);
                // Request the View to visually select the repository in the TreeView
                // Guard: keep _isSwitchingRepository true so TreeView's SelectedItemChanged
                // doesn't re-trigger SelectRepositoryAsync for the same repo
                _isSwitchingRepository = true;
                RequestRepositorySelection?.Invoke(this, lastRepo);
                _isSwitchingRepository = false;
            }
        }
    }

    /// <summary>
    /// Add a repository from folder.
    /// </summary>
    [RelayCommand]
    public async Task AddRepositoryAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Git Repository"
        };

        if (dialog.ShowDialog() == true)
        {
            var path = dialog.FolderName;

            // No session token: the path being validated is not the current
            // repo yet, and cancelling this check on repo switch would drop
            // the user's add-repo action mid-flight.
            if (!await _gitService.IsValidRepositoryAsync(path))
            {
                NotifyWarning("Not a Git repository", "Selected folder is not a valid Git repository.");
                return;
            }

            var repoInfo = await _gitService.GetRepositoryInfoFastAsync(path);
            _repositoryService.AddRepository(repoInfo);
            Log.Info("Repository", $"Added repository: {repoInfo.Name} ({path})");
            await SelectRepositoryAsync(repoInfo);
        }
    }

    /// <summary>
    /// Add all git repositories found in a folder (scans subdirectories).
    /// </summary>
    [RelayCommand]
    public async Task AddAllReposInFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Folder to Scan for Git Repositories"
        };

        if (dialog.ShowDialog() == true)
        {
            var rootPath = dialog.FolderName;
            var addedCount = 0;

            try
            {
                Log.Info("Repository", $"Scanning folder for repos: {rootPath}");
                await BeginBusyAsync("Scanning for repositories...");

                // Find all directories that contain a .git folder
                var gitDirs = Directory.GetDirectories(rootPath, ".git", SearchOption.AllDirectories);

                foreach (var gitDir in gitDirs)
                {
                    var repoPath = Path.GetDirectoryName(gitDir);
                    if (repoPath == null) continue;

                    // Skip if already added
                    if (_repositoryService.ContainsRepository(repoPath))
                        continue;

                    // No session token: scanning other paths is independent
                    // of the current repo session.
                    if (await _gitService.IsValidRepositoryAsync(repoPath))
                    {
                        var repoInfo = await _gitService.GetRepositoryInfoFastAsync(repoPath);
                        _repositoryService.AddRepository(repoInfo);
                        addedCount++;
                    }
                }

                Log.Info("Repository", $"Folder scan complete: added {addedCount} of {gitDirs.Length} found");
                if (addedCount > 0)
                    NotifySuccess("Repositories added", $"Added {addedCount} repositor{(addedCount == 1 ? "y" : "ies")}.");
                else
                    NotifyInfo("Scan complete", "No new repositories found.");
            }
            catch (Exception ex)
            {
                await ReportOperationFailureAsync("Scan folder", ex);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    /// <summary>
    /// Handles discovery of new repositories from watched folders.
    /// </summary>
    private async void OnRepositoryDiscovered(object? sender, string repoPath)
    {
        try
        {
            await _dispatcherService.InvokeAsync(async () =>
            {
                if (_repositoryService.ContainsRepository(repoPath))
                    return;

                // No session token: validating a discovered repo is
                // independent of which repo is currently selected.
                if (await _gitService.IsValidRepositoryAsync(repoPath))
                {
                    var repoInfo = await _gitService.GetRepositoryInfoFastAsync(repoPath);
                    _repositoryService.AddRepository(repoInfo);
                    Log.Info("Repository", $"Discovered repository: {repoInfo.Name} ({repoPath})");

                    // Mark the parent folder group as watched
                    var parentFolder = Path.GetDirectoryName(repoPath);
                    foreach (var group in RepositoryGroups)
                    {
                        if (group.Type == Models.GroupType.Folder &&
                            repoPath.StartsWith(Path.GetDirectoryName(group.Repositories.FirstOrDefault()?.Path ?? "") ?? "", StringComparison.OrdinalIgnoreCase))
                        {
                            group.IsWatched = true;
                            break;
                        }
                    }
                }
            });
        }
        catch (Exception ex)
        {
            // Background discovery — log-only by default, surface only if user opted in.
            AsyncErrorHandler.Handle(ex, nameof(OnRepositoryDiscovered), isUserAction: false);
        }
    }

    /// <summary>
    /// Scans watched folders for repositories that were added while the app was closed.
    /// </summary>
    private async Task ScanWatchedFoldersAsync(IEnumerable<string> watchedFolders)
    {
        foreach (var folder in watchedFolders)
        {
            var repos = await _folderWatcherService.ScanFolderAsync(folder);
            foreach (var repoPath in repos)
            {
                if (_repositoryService.ContainsRepository(repoPath))
                    continue;

                // No session token: scan-watched-folders runs on startup
                // and isn't tied to the current repo.
                if (await _gitService.IsValidRepositoryAsync(repoPath))
                {
                    var repoInfo = await _gitService.GetRepositoryInfoFastAsync(repoPath);
                    await _dispatcherService.InvokeAsync(() => _repositoryService.AddRepository(repoInfo));
                }
            }

            // Mark folder groups as watched
            await _dispatcherService.InvokeAsync(() =>
            {
                foreach (var group in RepositoryGroups)
                {
                    if (group.Type == Models.GroupType.Folder)
                    {
                        var groupFolder = Path.GetDirectoryName(group.Repositories.FirstOrDefault()?.Path ?? "");
                        if (!string.IsNullOrEmpty(groupFolder) && groupFolder.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                        {
                            group.IsWatched = true;
                        }
                    }
                }
            });
        }
    }

    /// <summary>
    /// Clone a repository from URL.
    /// </summary>
    [RelayCommand]
    public async Task CloneRepositoryAsync()
    {
        var settings = _settingsService.LoadSettings();
        var dialog = new CloneDialog(_gitService, _credentialService, _settingsService, _externalToolConfig, _externalToolDetector, settings.DefaultClonePath);

        if (await _dialogService.ShowDialogAsync(dialog) && !string.IsNullOrEmpty(dialog.ClonedRepositoryPath))
        {
            // Add the cloned repo to the list. No session token: the cloned
            // path isn't the current repo yet, and we're about to SelectRepositoryAsync
            // it which creates its own session.
            var repoInfo = await _gitService.GetRepositoryInfoFastAsync(dialog.ClonedRepositoryPath);
            _repositoryService.AddRepository(repoInfo);
            await SelectRepositoryAsync(repoInfo);
            NotifySuccess("Repository cloned", $"Cloned {repoInfo.Name} successfully.");
        }
    }

    /// <summary>
    /// Select a repository to view.
    /// </summary>
    [RelayCommand]
    public Task SelectRepositoryAsync(RepositoryInfo? repository) => SelectRepositoryAsync(repository, fetchInBackground: false);

    public async Task SelectRepositoryAsync(RepositoryInfo? repository, bool fetchInBackground)
    {
        if (repository == null) return;

        // Fix 3: Prevent double invocation from TreeView SelectedItemChanged cascade
        if (_isSwitchingRepository)
        {
            Log.Info("SelectRepo", $"Skipped duplicate SelectRepositoryAsync for {repository.Name}");
            return;
        }

        _isSwitchingRepository = true;
        var totalSw = Log.StartTimer();
        var stepSw = Log.StartTimer();

        // Close diff viewer when switching repositories
        IsDiffViewerVisible = false;

        try
        {
            await BeginBusyAsync($"Loading {repository.Name}...");
            Log.Perf("SelectRepo", "BeginBusyAsync", stepSw.ElapsedMilliseconds);

            var previousRepository = SelectedRepository;
            bool isRepositorySwitch = previousRepository != null &&
                !string.Equals(previousRepository.Path, repository.Path, StringComparison.OrdinalIgnoreCase);

            // Rotate the repository scope: dispose the old one (cascading
            // to IRepositorySession, which cancels its token and any
            // in-flight git operations that received it) and create a
            // fresh scope bound to this repo. Refreshes of the same repo
            // keep the existing scope so callers don't get spuriously
            // cancelled.
            if (_currentScope == null ||
                !string.Equals(_currentScopeRepoPath, repository.Path, StringComparison.OrdinalIgnoreCase))
            {
                var previousScope = _currentScope;
                _currentScope = null;
                _currentSession = null;
                _currentScopeRepoPath = null;

                var newScope = _scopeFactory.CreateScope();
                try
                {
                    newScope.ServiceProvider.GetRequiredService<RepositoryScopeContext>().Path = repository.Path;
                    // Force-resolve now so a bad path throws here rather
                    // than on the first git operation deeper in the UI.
                    // Caching the reference keeps CurrentRepositoryToken
                    // a field read instead of a per-call container lookup.
                    var session = newScope.ServiceProvider.GetRequiredService<IRepositorySession>();

                    _currentScope = newScope;
                    _currentSession = session;
                    _currentScopeRepoPath = repository.Path;
                }
                catch (ArgumentException ex)
                {
                    // Path is no longer a valid git repo — scrub the
                    // half-built scope and let downstream code surface the
                    // error through normal channels.
                    newScope.Dispose();
                    Log.Warn("SelectRepo", $"Scope create failed for {repository.Path}: {ex.Message}");
                }
                finally
                {
                    previousScope?.Dispose();
                }
            }
            if (isRepositorySwitch && HasActivePullRequestScreen())
            {
                ResetPullRequestViewState(previousRepository);
            }

            // Step 1: Set selected repo immediately so the graph view can begin loading.
            stepSw.Restart();
            SelectedRepository = repository;
            Log.Perf("SelectRepo", "Set SelectedRepository", stepSw.ElapsedMilliseconds);

            _repositoryService.MarkAsRecentlyAccessed(repository);
            _fileWatcherService.WatchRepository(repository.Path);

            // Probe the merge-tool config for this repo so the "Resolve
            // in External Tool" button enables/disables correctly.
            // Fire-and-forget: the check is quick and the button stays
            // disabled until the probe lands.
            RefreshExternalMergeToolAvailabilityAsync()
                .FireAndForget(nameof(RefreshExternalMergeToolAvailabilityAsync), isUserAction: false);

            var settings = _settingsService.LoadSettings();
            settings.LastSelectedRepositoryPath = repository.Path;
            _settingsService.SaveSettings(settings);

            // Step 2: Prepare graph color/filter context without waiting for the full sidebar tree load.
            stepSw.Restart();
            await PrepareGraphContextAsync(repository.Path);
            Log.Perf("SelectRepo", "PrepareGraphContextAsync", stepSw.ElapsedMilliseconds);

            var needsBranchFilters = repository.HiddenBranchNames.Count > 0 || repository.SoloBranchNames.Count > 0;
            var needsBranchSidebarLoad = !repository.BranchesLoaded;
            var branchLoadTask = needsBranchFilters && needsBranchSidebarLoad
                ? LoadBranchesForRepoAsync(repository, forceReload: false, skipFilterApplication: true)
                : null;

              // Step 3: Run graph load and worktrees in parallel, but don't let repo-info
              // block startup or repo switches indefinitely. The graph already gives us the
              // visible branch/dirty state we need to make the app usable immediately.
              stepSw.Restart();
              var graphTask = GitGraphViewModel?.LoadRepositoryAsync(repository.Path) ?? Task.CompletedTask;
              var infoTask = _gitService.GetRepositoryInfoFastAsync(repository.Path, cancellationToken: CurrentRepositoryToken);
              var worktreeTask = LoadWorktreesForRepoAsync(repository);

              await Task.WhenAll(graphTask, worktreeTask);
              Log.Perf("SelectRepo", "Parallel: graph + worktrees", stepSw.ElapsedMilliseconds);

              // Apply graph-dependent working changes sync (must happen after graph loads)
              if (GitGraphViewModel != null && IsWorkingChangesSelected && WorkingChangesViewModel != null)
              {
                  WorkingChangesViewModel.SetWorkingChanges(repository.Path, GitGraphViewModel.WorkingChanges);
              }

              // Apply repo info results if they arrive quickly; otherwise fall back to the
              // graph/working-changes snapshot and refresh the remaining fields in background.
              stepSw.Restart();
              if (!await TryApplyRepositoryInfoAsync(repository, infoTask, TimeSpan.FromMilliseconds(750)))
              {
                  ApplyRepositoryInfoFromGraph(repository);
                  ContinueApplyingRepositoryInfoAsync(repository, infoTask)
                      .FireAndForget(nameof(ContinueApplyingRepositoryInfoAsync), isUserAction: false);
              }
              Log.Perf("SelectRepo", "ApplyRepositoryInfo", stepSw.ElapsedMilliseconds);

            // Step 4: Apply filters (depends on graph being loaded)
            stepSw.Restart();
            if (needsBranchFilters)
            {
                if (branchLoadTask != null)
                {
                    await branchLoadTask;
                }

                ApplyBranchFiltersForRepo(repository);
            }
            else
            {
                IsBranchFilterActive = false;
            }
            Log.Perf("SelectRepo", "ApplyBranchFiltersForRepo", stepSw.ElapsedMilliseconds);

            stepSw.Restart();
            await RefreshMergeConflictResolutionAsync();
            Log.Perf("SelectRepo", "RefreshMergeConflictResolutionAsync", stepSw.ElapsedMilliseconds);

            // Pick up bisect state from disk so the bisect banner reflects
            // an in-progress session even after a repo switch / cold open.
            stepSw.Restart();
            await RefreshBisectStateAsync();
            Log.Perf("SelectRepo", "RefreshBisectStateAsync", stepSw.ElapsedMilliseconds);

            if (fetchInBackground)
                _autoFetchService.FetchAsync(repository.Path)
                    .FireAndForget(nameof(_autoFetchService.FetchAsync), isUserAction: false);

            if (!needsBranchFilters && needsBranchSidebarLoad)
            {
                LoadBranchesForRepoAsync(repository, forceReload: false, skipFilterApplication: true)
                    .FireAndForget(nameof(LoadBranchesForRepoAsync), isUserAction: false);
            }

            Log.Perf("SelectRepo", $"TOTAL for {repository.Name}", totalSw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            Log.Error("SelectRepo", $"FAILED after {totalSw.ElapsedMilliseconds}ms", ex);
            await ReportOperationFailureAsync("Select repository", ex);
        }
        finally
        {
            IsBusy = false;
            _isSwitchingRepository = false;
        }
    }

    [RelayCommand]
    public void TogglePinRepository(RepositoryInfo repo)
    {
        _repositoryService.TogglePinRepository(repo);
    }

    private async Task<bool> TryApplyRepositoryInfoAsync(
        RepositoryInfo repository,
        Task<RepositoryInfo> infoTask,
        TimeSpan timeout)
    {
        try
        {
            var info = await infoTask.WaitAsync(timeout);
            ApplyRepositoryInfo(repository, info);
            return true;
        }
        catch (TimeoutException)
        {
            Log.Warn("SelectRepo", $"Timed out waiting for repository info for {repository.Name}; continuing with graph snapshot");
            return false;
        }
    }

    private async Task ContinueApplyingRepositoryInfoAsync(RepositoryInfo repository, Task<RepositoryInfo> infoTask)
    {
        try
        {
            var info = await infoTask.ConfigureAwait(false);
            await _dispatcherService.InvokeAsync(() =>
            {
                if (SelectedRepository == null ||
                    !string.Equals(SelectedRepository.Path, repository.Path, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                ApplyRepositoryInfo(repository, info);
            });
        }
        catch (Exception ex)
        {
            Log.Warn("SelectRepo", $"Deferred repository info refresh failed for {repository.Name}: {ex.Message}");
        }
    }

    private void ApplyRepositoryInfoFromGraph(RepositoryInfo repository)
    {
        var workingChanges = GitGraphViewModel?.WorkingChanges;
        if (workingChanges == null)
        {
            return;
        }

        repository.CurrentBranch = workingChanges.IsDetachedHead
            ? $"HEAD ({workingChanges.DetachedHeadSha?[..7] ?? "detached"})"
            : (string.IsNullOrWhiteSpace(workingChanges.BranchName) ? repository.CurrentBranch : workingChanges.BranchName);
        repository.IsDirty = workingChanges.HasChanges;
        repository.IsDetachedHead = workingChanges.IsDetachedHead;
        repository.DetachedHeadSha = workingChanges.DetachedHeadSha;
    }

    [RelayCommand]
    public async Task DeleteRepositoryAsync(RepositoryInfo repo)
    {
        bool wasSelected = SelectedRepository != null && SelectedRepository.Path == repo.Path;

        if (wasSelected)
        {
            SelectedRepository = null;
            var settings = _settingsService.LoadSettings();
            settings.LastSelectedRepositoryPath = null;
            _settingsService.SaveSettings(settings);
        }

        _repositoryService.RemoveRepository(repo);

        if (wasSelected)
        {
            // Find the first available repository to switch to
            var nextRepo = RepositoryGroups
                .SelectMany(g => g.Repositories)
                .FirstOrDefault();

            if (nextRepo != null)
            {
                await SelectRepositoryAsync(nextRepo);
                RequestRepositorySelection?.Invoke(this, nextRepo);
            }
        }
    }

    [RelayCommand]
    public async Task RemoveAllRepositoriesInGroupAsync(RepositoryGroup group)
    {
        if (group == null || group.Repositories.Count == 0)
        {
            return;
        }

        var confirmed = await _dialogService.ShowConfirmationAsync(
            $"Remove all repositories from '{group.Name}'?\n\nThis only removes them from Leaf. Files on disk are not deleted.",
            "Remove All Repositories");

        if (!confirmed)
        {
            return;
        }

        var repos = group.Repositories.ToList();
        foreach (var repo in repos)
        {
            await DeleteRepositoryAsync(repo);
        }
    }

    /// <summary>
    /// Start watching a folder group for new repositories.
    /// </summary>
    [RelayCommand]
    public void WatchFolderGroup(RepositoryGroup? group)
    {
        if (group == null || group.Repositories.Count == 0)
            return;

        // Get the parent folder path from the first repository
        var firstRepoPath = group.Repositories.FirstOrDefault()?.Path;
        if (string.IsNullOrEmpty(firstRepoPath))
            return;

        var folderPath = Path.GetDirectoryName(firstRepoPath);
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return;

        // Add to watched folders
        var settings = _settingsService.LoadSettings();
        if (!settings.WatchedFolders.Contains(folderPath, StringComparer.OrdinalIgnoreCase))
        {
            settings.WatchedFolders.Add(folderPath);
            _settingsService.SaveSettings(settings);
            _folderWatcherService.AddWatchedFolder(folderPath);
        }

        group.IsWatched = true;
        NotifySuccess("Watching folder", $"Now watching {group.Name} for new repositories.");
    }

    /// <summary>
    /// Stop watching a folder group for new repositories.
    /// </summary>
    [RelayCommand]
    public void UnwatchFolderGroup(RepositoryGroup? group)
    {
        if (group == null || group.Repositories.Count == 0)
            return;

        // Get the parent folder path from the first repository
        var firstRepoPath = group.Repositories.FirstOrDefault()?.Path;
        if (string.IsNullOrEmpty(firstRepoPath))
            return;

        var folderPath = Path.GetDirectoryName(firstRepoPath);
        if (string.IsNullOrEmpty(folderPath))
            return;

        // Remove from watched folders
        var settings = _settingsService.LoadSettings();
        settings.WatchedFolders.RemoveAll(f => f.Equals(folderPath, StringComparison.OrdinalIgnoreCase));
        _settingsService.SaveSettings(settings);
        _folderWatcherService.RemoveWatchedFolder(folderPath);

        group.IsWatched = false;
        NotifyInfo("Watch stopped", $"Stopped watching {group.Name}.");
    }
}
