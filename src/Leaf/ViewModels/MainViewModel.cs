using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;
using Leaf.Views;
using Microsoft.Win32;

namespace Leaf.ViewModels;

/// <summary>
/// Main application ViewModel - manages navigation and overall app state.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IGitService _gitService;
    private readonly IGitFlowService _gitFlowService;
    private readonly CredentialService _credentialService;
    private readonly SettingsService _settingsService;
    private readonly Window _ownerWindow;
    private readonly FileWatcherService _fileWatcherService;

    /// <summary>
    /// Auto-fetch timer interval (10 minutes).
    /// </summary>
    private static readonly TimeSpan AutoFetchInterval = TimeSpan.FromMinutes(10);

    private DispatcherTimer? _autoFetchTimer;

    [ObservableProperty]
    private DateTime? _lastFetchTime;

    [ObservableProperty]
    private ObservableCollection<RepositoryGroup> _repositoryGroups = [];

    [ObservableProperty]
    private RepositoryInfo? _selectedRepository;

    [ObservableProperty]
    private GitGraphViewModel? _gitGraphViewModel;

    [ObservableProperty]
    private CommitDetailViewModel? _commitDetailViewModel;

    [ObservableProperty]
    private WorkingChangesViewModel? _workingChangesViewModel;

    [ObservableProperty]
    private ConflictResolutionViewModel? _mergeConflictResolutionViewModel;

    [ObservableProperty]
    private bool _isCommitDetailVisible = true;

    [ObservableProperty]
    private bool _isWorkingChangesSelected;

    [ObservableProperty]
    private bool _isRepoPaneCollapsed;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _commitSearchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<RepositoryInfo> _pinnedRepositories = [];

    [ObservableProperty]
    private ObservableCollection<RepositoryInfo> _recentRepositories = [];

    [ObservableProperty]
    private ObservableCollection<object> _repositoryRootItems = [];

    private readonly Models.RepositorySection _pinnedSection = new() { Name = "PINNED" };
    private readonly Models.RepositorySection _recentSection = new() { Name = "MOST RECENT" };

    private string? _mergeConflictRepoPath;

    partial void OnCommitSearchTextChanged(string value)
    {
        // Apply filter to GitGraphViewModel as user types
        if (GitGraphViewModel != null)
        {
            GitGraphViewModel.SearchText = value;
        }
    }

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _canRedo;

    [ObservableProperty]
    private bool _isBranchInputVisible;

    [ObservableProperty]
    private string _newBranchName = string.Empty;

    public MainViewModel(IGitService gitService, CredentialService credentialService, SettingsService settingsService, IGitFlowService gitFlowService, Window ownerWindow)
    {
        _gitService = gitService;
        _gitFlowService = gitFlowService;
        _credentialService = credentialService;
        _settingsService = settingsService;
        _ownerWindow = ownerWindow;
        _fileWatcherService = new FileWatcherService();

        _gitGraphViewModel = new GitGraphViewModel(gitService);
        _commitDetailViewModel = new CommitDetailViewModel(gitService);
        _workingChangesViewModel = new WorkingChangesViewModel(gitService, settingsService);

        // Load UI state from settings
        var settings = settingsService.LoadSettings();
        _isRepoPaneCollapsed = settings.IsRepoPaneCollapsed;

        RepositoryRootItems = new ObservableCollection<object>();

        // Wire up file watcher events
        _fileWatcherService.WorkingDirectoryChanged += async (s, e) =>
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                // Refresh working changes in graph view
                if (_gitGraphViewModel != null)
                {
                    await _gitGraphViewModel.RefreshWorkingChangesAsync();

                    // Sync to staging view if visible
                    if (_workingChangesViewModel != null && SelectedRepository != null && IsWorkingChangesSelected)
                    {
                        _workingChangesViewModel.SetWorkingChanges(
                            SelectedRepository.Path,
                            _gitGraphViewModel.WorkingChanges);
                    }
                }
            });
        };

        _fileWatcherService.GitDirectoryChanged += async (s, e) =>
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                // Full refresh of git graph for commit changes
                if (_gitGraphViewModel != null && SelectedRepository != null)
                {
                    await _gitGraphViewModel.LoadRepositoryAsync(SelectedRepository.Path);
                }

                if (SelectedRepository != null)
                {
                    var info = await _gitService.GetRepositoryInfoAsync(SelectedRepository.Path);
                    SelectedRepository.IsMergeInProgress = info.IsMergeInProgress;
                    SelectedRepository.MergingBranch = info.MergingBranch;
                    SelectedRepository.ConflictCount = info.ConflictCount;

                    await RefreshMergeConflictResolutionAsync();
                }
            });
        };

        // Wire up selection changes
        _gitGraphViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(GitGraphViewModel.SelectedCommit))
            {
                LoadCommitDetails(_gitGraphViewModel.SelectedCommit);
            }
            else if (e.PropertyName == nameof(GitGraphViewModel.IsWorkingChangesSelected))
            {
                IsWorkingChangesSelected = _gitGraphViewModel.IsWorkingChangesSelected;
                if (IsWorkingChangesSelected && SelectedRepository != null)
                {
                    // Defer to avoid reentrancy during PropertyChanged
                    var repoPath = SelectedRepository.Path;
                    var workingChanges = _gitGraphViewModel.WorkingChanges;
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        _workingChangesViewModel.SetWorkingChanges(repoPath, workingChanges);
                    }, DispatcherPriority.Background);
                }
            }
            else if (e.PropertyName == nameof(GitGraphViewModel.WorkingChanges))
            {
                // Update working changes count in commit detail view
                if (_commitDetailViewModel != null && _gitGraphViewModel?.WorkingChanges != null)
                {
                    _commitDetailViewModel.UpdateWorkingChangesCount(_gitGraphViewModel.WorkingChanges.TotalChanges);
                }
            }
            else if (e.PropertyName == nameof(GitGraphViewModel.SelectedStash))
            {
                // Notify that Pop command availability changed
                PopStashCommand.NotifyCanExecuteChanged();

                // Load stash details when a stash is selected
                var selectedStash = _gitGraphViewModel.SelectedStash;
                if (selectedStash != null && SelectedRepository != null)
                {
                    _ = _commitDetailViewModel.LoadStashAsync(SelectedRepository.Path, selectedStash);
                }
            }
        };

        // Wire up commit detail events
        _commitDetailViewModel.NavigateToCommitRequested += (s, sha) =>
        {
            if (_gitGraphViewModel != null)
            {
                _gitGraphViewModel.SelectCommitBySha(sha);
            }
        };

        _commitDetailViewModel.SelectWorkingChangesRequested += (s, e) =>
        {
            if (_gitGraphViewModel != null)
            {
                _gitGraphViewModel.SelectWorkingChanges();
            }
        };

        // Load saved repositories on startup
        LoadSavedRepositories();

        // Start auto-fetch timer
        StartAutoFetchTimer();
    }

    /// <summary>
    /// Start the auto-fetch timer.
    /// </summary>
    private void StartAutoFetchTimer()
    {
        _autoFetchTimer = new DispatcherTimer
        {
            Interval = AutoFetchInterval
        };
        _autoFetchTimer.Tick += async (s, e) => await AutoFetchAsync();
        _autoFetchTimer.Start();
    }

    /// <summary>
    /// Stop the auto-fetch timer.
    /// </summary>
    public void StopAutoFetchTimer()
    {
        _autoFetchTimer?.Stop();
    }

    /// <summary>
    /// Silent auto-fetch from remote (no UI blocking).
    /// </summary>
    private async Task AutoFetchAsync()
    {
        if (SelectedRepository == null)
            return;

        try
        {
            // Try to get credentials from stored PAT
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path);
            var originUrl = remotes.FirstOrDefault(r => r.Name == "origin")?.Url;
            string? pat = null;
            if (!string.IsNullOrEmpty(originUrl))
            {
                try
                {
                    var host = new Uri(originUrl).Host;
                    pat = _credentialService.GetPat(host);
                }
                catch
                {
                    // Invalid URL, skip PAT
                }
            }

            await _gitService.FetchAsync(SelectedRepository.Path, "origin", password: pat);
            LastFetchTime = DateTime.Now;

            // Update ahead/behind counts
            var info = await _gitService.GetRepositoryInfoAsync(SelectedRepository.Path);
            SelectedRepository.AheadBy = info.AheadBy;
            SelectedRepository.BehindBy = info.BehindBy;

            // Update status
            StatusMessage = $"Auto-fetched at {LastFetchTime:HH:mm}" +
                           (SelectedRepository.AheadBy > 0 ? $" | {SelectedRepository.AheadBy}" : "") +
                           (SelectedRepository.BehindBy > 0 ? $" | {SelectedRepository.BehindBy}" : "");
        }
        catch
        {
            // Silent failure for auto-fetch - don't disrupt the user
        }
    }

    /// <summary>
    /// Load repositories from persistent storage.
    /// </summary>
    private async void LoadSavedRepositories()
    {
        var data = _settingsService.LoadRepositories();

        foreach (var repo in data.Repositories)
        {
            // Only add if the repo still exists on disk
            if (repo.Exists)
            {
                AddRepositoryToGroups(repo, save: false);
            }
        }

        // Also load custom groups
        foreach (var group in data.CustomGroups)
        {
            if (!RepositoryGroups.Any(g => g.Id == group.Id))
            {
                RepositoryGroups.Add(group);
            }
        }

        // Restore last selected repository
        var settings = _settingsService.LoadSettings();
        if (!string.IsNullOrEmpty(settings.LastSelectedRepositoryPath))
        {
            var lastRepo = RepositoryGroups
                .SelectMany(g => g.Repositories)
                .FirstOrDefault(r => r.Path == settings.LastSelectedRepositoryPath);

            if (lastRepo != null)
            {
                await SelectRepositoryAsync(lastRepo);
            }
        }

        RefreshQuickAccessRepositories();
    }

    /// <summary>
    /// Save repositories to persistent storage.
    /// </summary>
    private void SaveRepositories()
    {
        var allRepos = RepositoryGroups
            .SelectMany(g => g.Repositories)
            .DistinctBy(r => r.Path)
            .ToList();

        var customGroups = RepositoryGroups
            .Where(g => g.Type == GroupType.Custom)
            .ToList();

        var data = new RepositoryData
        {
            Repositories = allRepos,
            CustomGroups = customGroups
        };

        _settingsService.SaveRepositories(data);
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

            if (!await _gitService.IsValidRepositoryAsync(path))
            {
                StatusMessage = "Selected folder is not a valid Git repository";
                return;
            }

            var repoInfo = await _gitService.GetRepositoryInfoAsync(path);
            AddRepositoryToGroups(repoInfo);
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
                IsBusy = true;
                StatusMessage = "Scanning for repositories...";

                // Find all directories that contain a .git folder
                var gitDirs = Directory.GetDirectories(rootPath, ".git", SearchOption.AllDirectories);

                foreach (var gitDir in gitDirs)
                {
                    var repoPath = Path.GetDirectoryName(gitDir);
                    if (repoPath == null) continue;

                    // Skip if already added
                    if (RepositoryGroups.SelectMany(g => g.Repositories).Any(r => r.Path == repoPath))
                        continue;

                    if (await _gitService.IsValidRepositoryAsync(repoPath))
                    {
                        var repoInfo = await _gitService.GetRepositoryInfoAsync(repoPath);
                        AddRepositoryToGroups(repoInfo);
                        addedCount++;
                    }
                }

                StatusMessage = addedCount > 0
                    ? $"Added {addedCount} repositor{(addedCount == 1 ? "y" : "ies")}"
                    : "No new repositories found";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error scanning: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    /// <summary>
    /// Clone a repository from URL.
    /// </summary>
    [RelayCommand]
    public async Task CloneRepositoryAsync()
    {
        var settings = _settingsService.LoadSettings();
        var dialog = new CloneDialog(_gitService, _credentialService, _settingsService, settings.DefaultClonePath)
        {
            Owner = _ownerWindow
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.ClonedRepositoryPath))
        {
            // Add the cloned repo to the list
            var repoInfo = await _gitService.GetRepositoryInfoAsync(dialog.ClonedRepositoryPath);
            AddRepositoryToGroups(repoInfo);
            await SelectRepositoryAsync(repoInfo);
            StatusMessage = $"Cloned {repoInfo.Name} successfully";
        }
    }

    /// <summary>
    /// Select a repository to view.
    /// </summary>
    [RelayCommand]
    public async Task SelectRepositoryAsync(RepositoryInfo? repository)
    {
        if (repository == null) return;

        try
        {
            IsBusy = true;
            SelectedRepository = repository;
            if (!RecentRepositories.Contains(repository))
            {
                repository.LastAccessed = DateTimeOffset.Now;
                SaveRepositories();
                RefreshQuickAccessRepositories();
            }
            StatusMessage = $"Loading {repository.Name}...";

            // Start watching the new repository for live changes
            _fileWatcherService.WatchRepository(repository.Path);

            // Save as last selected repository
            var settings = _settingsService.LoadSettings();
            settings.LastSelectedRepositoryPath = repository.Path;
            _settingsService.SaveSettings(settings);

            // Load repository into graph view
            if (GitGraphViewModel != null)
            {
                await GitGraphViewModel.LoadRepositoryAsync(repository.Path);
            }

            // Update status
            var info = await _gitService.GetRepositoryInfoAsync(repository.Path);
            repository.CurrentBranch = info.CurrentBranch;
            repository.IsDirty = info.IsDirty;
            repository.AheadBy = info.AheadBy;
            repository.BehindBy = info.BehindBy;
            repository.IsMergeInProgress = info.IsMergeInProgress;
            repository.MergingBranch = info.MergingBranch;
            repository.ConflictCount = info.ConflictCount;

            // Load branches for the branch panel (force reload to pick up pruned branches)
            await LoadBranchesForRepoAsync(repository, forceReload: true);

            await RefreshMergeConflictResolutionAsync();

            StatusMessage = $"{repository.Name} | {repository.CurrentBranch}" +
                           (repository.IsDirty ? " | Modified" : "") +
                           (repository.AheadBy > 0 ? $" | {repository.AheadBy}" : "") +
                           (repository.BehindBy > 0 ? $" | {repository.BehindBy}" : "");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Fetch all repositories.
    /// </summary>
    [RelayCommand]
    public async Task FetchAllAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Fetching all repositories...";

            foreach (var group in RepositoryGroups)
            {
                foreach (var repo in group.Repositories)
                {
                    try
                    {
                        await _gitService.FetchAsync(repo.Path);
                    }
                    catch
                    {
                        // Continue with other repos
                    }
                }
            }

            StatusMessage = "Fetch complete";

            // Refresh current repo if selected
            if (SelectedRepository != null)
            {
                await SelectRepositoryAsync(SelectedRepository);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Fetch from a specific remote.
    /// </summary>
    [RelayCommand]
    public async Task FetchRemoteAsync(string? remoteName)
    {
        if (SelectedRepository == null || string.IsNullOrEmpty(remoteName))
            return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Fetching from {remoteName}...";

            // Get credentials for this remote
            string? pat = null;
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path);
            var remoteUrl = remotes.FirstOrDefault(r => r.Name == remoteName)?.Url;
            if (!string.IsNullOrEmpty(remoteUrl))
            {
                try
                {
                    var host = new Uri(remoteUrl).Host;
                    pat = _credentialService.GetPat(host);
                }
                catch
                {
                    // Invalid URL, skip PAT
                }
            }

            await _gitService.FetchAsync(SelectedRepository.Path, remoteName, password: pat);

            StatusMessage = $"Fetched from {remoteName}";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fetch failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Refresh current repository.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (SelectedRepository != null)
        {
            await SelectRepositoryAsync(SelectedRepository);
        }
    }

    /// <summary>
    /// Toggle commit detail panel visibility.
    /// </summary>
    [RelayCommand]
    public void ToggleCommitDetail()
    {
        IsCommitDetailVisible = !IsCommitDetailVisible;
    }

    /// <summary>
    /// Toggle repo pane collapsed state.
    /// </summary>
    [RelayCommand]
    public void ToggleRepoPane()
    {
        IsRepoPaneCollapsed = !IsRepoPaneCollapsed;

        // Persist the state
        var settings = _settingsService.LoadSettings();
        settings.IsRepoPaneCollapsed = IsRepoPaneCollapsed;
        _settingsService.SaveSettings(settings);
    }

    /// <summary>
    /// Open settings.
    /// </summary>
    [RelayCommand]
    public void OpenSettings()
    {
        var dialog = new SettingsDialog(_credentialService, _settingsService)
        {
            Owner = _ownerWindow,
            Width = 1000,
            Height = 750
        };
        dialog.ShowDialog();
    }

    /// <summary>
    /// Undo last commit (soft reset).
    /// </summary>
    [RelayCommand]
    public async Task UndoAsync()
    {
        if (SelectedRepository == null) return;

        try
        {
            IsBusy = true;
            StatusMessage = "Undoing last commit...";

            var success = await _gitService.UndoCommitAsync(SelectedRepository.Path);
            if (success)
            {
                StatusMessage = "Commit undone (changes preserved in working directory)";
                await RefreshAsync();
            }
            else
            {
                StatusMessage = "Cannot undo: commit already pushed or no parent commit";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Undo failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Redo (not implemented - would need reflog tracking).
    /// </summary>
    [RelayCommand]
    public void Redo()
    {
        StatusMessage = "Redo not yet implemented";
    }

    /// <summary>
    /// Pull from remote.
    /// </summary>
    [RelayCommand]
    public async Task PullAsync()
    {
        if (SelectedRepository == null) return;

        try
        {
            IsBusy = true;
            StatusMessage = "Pulling changes...";

            // Try to get credentials from stored PAT (uses remote URL hostname)
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path);
            var originUrl = remotes.FirstOrDefault(r => r.Name == "origin")?.Url;
            string? pat = null;
            if (!string.IsNullOrEmpty(originUrl))
            {
                var host = new Uri(originUrl).Host;
                pat = _credentialService.GetPat(host);
            }

            await _gitService.PullAsync(SelectedRepository.Path, null, pat);

            StatusMessage = "Pull complete";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pull failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Push to remote.
    /// </summary>
    [RelayCommand]
    public async Task PushAsync()
    {
        if (SelectedRepository == null) return;

        try
        {
            IsBusy = true;
            StatusMessage = "Pushing changes...";

            // Try to get credentials from stored PAT (uses remote URL hostname)
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path);
            var originUrl = remotes.FirstOrDefault(r => r.Name == "origin")?.Url;
            string? pat = null;
            if (!string.IsNullOrEmpty(originUrl))
            {
                var host = new Uri(originUrl).Host;
                pat = _credentialService.GetPat(host);
            }

            await _gitService.PushAsync(SelectedRepository.Path, null, pat);

            StatusMessage = "Push complete";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Push failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Create a new branch.
    /// </summary>
    [RelayCommand]
    public void CreateBranch()
    {
        if (SelectedRepository == null) return;

        // Show floating branch input
        NewBranchName = string.Empty;
        IsBranchInputVisible = true;
    }

    [RelayCommand]
    public async Task ConfirmCreateBranchAsync()
    {
        if (SelectedRepository == null || string.IsNullOrWhiteSpace(NewBranchName))
            return;

        var branchName = NewBranchName.Trim();
        IsBranchInputVisible = false;
        NewBranchName = string.Empty;

        try
        {
            IsBusy = true;
            StatusMessage = $"Creating branch '{branchName}'...";

            await _gitService.CreateBranchAsync(SelectedRepository.Path, branchName);

            StatusMessage = $"Created and checked out branch '{branchName}'";
            SelectedRepository.BranchesLoaded = false;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Create branch failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void CancelBranchInput()
    {
        IsBranchInputVisible = false;
        NewBranchName = string.Empty;
    }

    /// <summary>
    /// Delete a branch.
    /// </summary>
    [RelayCommand]
    public async Task DeleteBranchAsync(BranchInfo branch)
    {
        if (SelectedRepository == null) return;

        // TODO: Implement actual deletion logic with safety checks (merged/unmerged)
        // For now, just show the placeholder message that was previously in the View
        await Application.Current.Dispatcher.InvokeAsync(() => 
        {
             MessageBox.Show($"Delete branch '{branch.Name}' - not yet implemented in Service",
                    "Delete Branch", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    /// <summary>
    /// Stash changes.
    /// </summary>
    [RelayCommand]
    public async Task StashAsync()
    {
        if (SelectedRepository == null) return;

        try
        {
            IsBusy = true;
            StatusMessage = "Stashing changes...";

            await _gitService.StashAsync(SelectedRepository.Path);

            StatusMessage = "Changes stashed";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Stash failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Check if pop stash can be executed.
    /// </summary>
    private bool CanPopStash() => SelectedRepository != null && GitGraphViewModel?.SelectedStash != null;

    /// <summary>
    /// Pop the selected stashed changes.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPopStash))]
    public async Task PopStashAsync()
    {
        if (SelectedRepository == null) return;
        var selectedStash = GitGraphViewModel?.SelectedStash;
        if (selectedStash == null) return;

        System.Diagnostics.Debug.WriteLine($"[MainVM.PopStash] Starting pop for stash index {selectedStash.Index}");

        try
        {
            IsBusy = true;
            StatusMessage = "Popping stash...";

            var result = await _gitService.PopStashAsync(SelectedRepository.Path, selectedStash.Index);

            System.Diagnostics.Debug.WriteLine($"[MainVM.PopStash] Service returned: Success={result.Success}, HasConflicts={result.HasConflicts}, Error={result.ErrorMessage}");

            // Clear stash selection before refresh so preservation logic doesn't re-select
            GitGraphViewModel?.SelectStash(null);

            if (result.Success)
            {
                System.Diagnostics.Debug.WriteLine("[MainVM.PopStash] SUCCESS branch - refreshing");
                StatusMessage = "Stash popped";
                await RefreshAsync();
            }
            else if (result.HasConflicts)
            {
                System.Diagnostics.Debug.WriteLine("[MainVM.PopStash] CONFLICTS branch - checking for actual conflicts");
                // Load conflicts first to check if there are actually any
                var conflicts = await _gitService.GetConflictsAsync(SelectedRepository.Path);
                System.Diagnostics.Debug.WriteLine($"[MainVM.PopStash] Actual conflicts found: {conflicts.Count}");

                if (conflicts.Count == 0)
                {
                    // No actual conflicts found - stash may have failed for another reason
                    StatusMessage = result.ErrorMessage ?? "Stash pop completed with warnings";
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = "Stash applied with conflicts - resolve to complete";
                    await RefreshAsync();

                    // Show conflict resolution UI with friendly stash name
                    var stashName = !string.IsNullOrEmpty(selectedStash.MessageShort)
                        ? $"Stash: {selectedStash.MessageShort}"
                        : "Stashed changes";
                    var conflictViewModel = new ConflictResolutionViewModel(_gitService, SelectedRepository.Path)
                    {
                        SourceBranch = stashName,
                        TargetBranch = SelectedRepository.CurrentBranch ?? "HEAD"
                    };
                    await conflictViewModel.LoadConflictsAsync();

                    var conflictView = new Views.ConflictResolutionView
                    {
                        DataContext = conflictViewModel,
                        Owner = _ownerWindow
                    };

                    conflictViewModel.MergeCompleted += async (s, success) =>
                    {
                        conflictView.Close();
                        if (success)
                        {
                            // Clean up any leftover temp stash from smart pop
                            await _gitService.CleanupTempStashAsync(SelectedRepository.Path);
                            StatusMessage = "Stash applied successfully";
                        }
                        else
                        {
                            StatusMessage = "Stash pop aborted";
                        }
                        await RefreshAsync();
                    };

                    conflictView.ShowDialog();
                }
            }
            else
            {
                StatusMessage = $"Pop stash failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pop stash failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanDeleteStash() => SelectedRepository != null && GitGraphViewModel?.SelectedStash != null;

    /// <summary>
    /// Delete the selected stash without applying it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteStash))]
    public async Task DeleteStashAsync()
    {
        if (SelectedRepository == null) return;
        var selectedStash = GitGraphViewModel?.SelectedStash;
        if (selectedStash == null) return;

        try
        {
            IsBusy = true;
            StatusMessage = "Deleting stash...";

            await _gitService.DeleteStashAsync(SelectedRepository.Path, selectedStash.Index);

            // Clear stash selection before refresh
            GitGraphViewModel?.SelectStash(null);

            StatusMessage = "Stash deleted";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Delete stash failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadCommitDetails(CommitInfo? commit)
    {
        if (CommitDetailViewModel != null && SelectedRepository != null && commit != null)
        {
            _ = CommitDetailViewModel.LoadCommitAsync(SelectedRepository.Path, commit.Sha);
        }
    }

    private void AddRepositoryToGroups(RepositoryInfo repo, bool save = true)
    {
        // Find or create folder-based group
        var folderGroup = RepositoryGroups.FirstOrDefault(g =>
            g.Type == GroupType.Folder && g.Name == repo.FolderGroup);

        if (folderGroup == null)
        {
            folderGroup = new RepositoryGroup
            {
                Name = repo.FolderGroup,
                Type = GroupType.Folder
            };
            RepositoryGroups.Add(folderGroup);
        }

        // Add repo if not already present
        if (!folderGroup.Repositories.Any(r => r.Path == repo.Path))
        {
            folderGroup.Repositories.Add(repo);

            // Save to persistent storage
            if (save)
            {
                SaveRepositories();
            }
        }

        RefreshQuickAccessRepositories();
    }

    private void RemoveRepositoryFromGroups(RepositoryInfo repo)
    {
        var emptyGroups = new List<RepositoryGroup>();

        foreach (var group in RepositoryGroups)
        {
            var existing = group.Repositories.FirstOrDefault(r => r.Path == repo.Path);
            if (existing != null)
            {
                group.Repositories.Remove(existing);
            }

            if (group.Repositories.Count == 0)
            {
                emptyGroups.Add(group);
            }
        }

        foreach (var group in emptyGroups)
        {
            RepositoryGroups.Remove(group);
        }

        RefreshQuickAccessRepositories();
        SaveRepositories();
    }

    private void RefreshQuickAccessRepositories()
    {
        var allRepos = RepositoryGroups
            .SelectMany(g => g.Repositories)
            .DistinctBy(r => r.Path)
            .ToList();

        PinnedRepositories.Clear();
        foreach (var repo in allRepos.Where(r => r.IsPinned))
        {
            PinnedRepositories.Add(repo);
        }

        RecentRepositories.Clear();
        foreach (var repo in allRepos
                     .OrderByDescending(r => r.LastAccessed)
                     .Take(5))
        {
            RecentRepositories.Add(repo);
        }

        _pinnedSection.Repositories.Clear();
        foreach (var repo in PinnedRepositories)
        {
            _pinnedSection.Repositories.Add(repo);
        }

        _recentSection.Repositories.Clear();
        foreach (var repo in RecentRepositories)
        {
            _recentSection.Repositories.Add(repo);
        }

        int insertIndex = 0;
        if (_pinnedSection.Repositories.Count > 0)
        {
            if (!RepositoryRootItems.Contains(_pinnedSection))
            {
                RepositoryRootItems.Insert(insertIndex, _pinnedSection);
            }
            insertIndex = RepositoryRootItems.IndexOf(_pinnedSection) + 1;
        }
        else if (RepositoryRootItems.Contains(_pinnedSection))
        {
            RepositoryRootItems.Remove(_pinnedSection);
        }

        if (_recentSection.Repositories.Count > 0)
        {
            if (!RepositoryRootItems.Contains(_recentSection))
            {
                RepositoryRootItems.Insert(insertIndex, _recentSection);
            }
            insertIndex = RepositoryRootItems.IndexOf(_recentSection) + 1;
        }
        else if (RepositoryRootItems.Contains(_recentSection))
        {
            RepositoryRootItems.Remove(_recentSection);
        }

        foreach (var group in RepositoryGroups)
        {
            if (!RepositoryRootItems.Contains(group))
            {
                RepositoryRootItems.Add(group);
            }
        }

        for (int i = RepositoryRootItems.Count - 1; i >= 0; i--)
        {
            if (RepositoryRootItems[i] is RepositoryGroup group && !RepositoryGroups.Contains(group))
            {
                RepositoryRootItems.RemoveAt(i);
            }
        }
    }

    [RelayCommand]
    public void TogglePinRepository(RepositoryInfo repo)
    {
        repo.IsPinned = !repo.IsPinned;
        SaveRepositories();
        RefreshQuickAccessRepositories();
    }

    [RelayCommand]
    public void DeleteRepository(RepositoryInfo repo)
    {
        if (SelectedRepository != null && SelectedRepository.Path == repo.Path)
        {
            SelectedRepository = null;
            var settings = _settingsService.LoadSettings();
            settings.LastSelectedRepositoryPath = null;
            _settingsService.SaveSettings(settings);
        }

        RemoveRepositoryFromGroups(repo);
    }

    /// <summary>
    /// Load branches for a repository.
    /// </summary>
    public async Task LoadBranchesForRepoAsync(RepositoryInfo repo, bool forceReload = false)
    {
        if (repo.BranchesLoaded && !forceReload) return;

        try
        {
            var branches = await _gitService.GetBranchesAsync(repo.Path);

            // Get remote URLs for determining remote type (GitHub, Azure DevOps, etc.)
            var remotes = await _gitService.GetRemotesAsync(repo.Path);
            var remoteUrlLookup = remotes.ToDictionary(r => r.Name, r => r.Url, StringComparer.OrdinalIgnoreCase);

            var localBranches = branches.Where(b => !b.IsRemote).OrderBy(b => b.Name).ToList();
            // Filter out HEAD from remote branches (it's a symbolic reference, not a real branch)
            var remoteBranches = branches
                .Where(b => b.IsRemote && !b.Name.EndsWith("/HEAD", StringComparison.OrdinalIgnoreCase))
                .OrderBy(b => b.Name)
                .ToList();

            repo.LocalBranches.Clear();
            foreach (var branch in localBranches)
            {
                repo.LocalBranches.Add(branch);
            }

            repo.RemoteBranches.Clear();
            foreach (var branch in remoteBranches)
            {
                repo.RemoteBranches.Add(branch);
            }

            // Group remote branches by remote name (origin, upstream, etc.)
            var remoteGroups = remoteBranches
                .GroupBy(b => b.RemoteName ?? "origin")
                .Select(g =>
                {
                    var remoteUrl = remoteUrlLookup.GetValueOrDefault(g.Key, string.Empty);
                    return new RemoteBranchGroup
                    {
                        Name = g.Key,
                        Url = remoteUrl,
                        RemoteType = RemoteBranchGroup.GetRemoteTypeFromUrl(remoteUrl),
                        Branches = new System.Collections.ObjectModel.ObservableCollection<BranchInfo>(
                            g.Select(b => new BranchInfo
                            {
                                // Strip the remote prefix from the display name
                                Name = b.Name.StartsWith($"{g.Key}/") ? b.Name[($"{g.Key}/".Length)..] : b.Name,
                                FullName = b.FullName,
                                IsCurrent = b.IsCurrent,
                                IsRemote = b.IsRemote,
                                RemoteName = b.RemoteName,
                                TrackingBranchName = b.TrackingBranchName,
                                TipSha = b.TipSha,
                                AheadBy = b.AheadBy,
                                BehindBy = b.BehindBy
                            }).OrderBy(b => b.Name)),
                        IsExpanded = true
                    };
                })
                .OrderBy(g => g.Name)
                .ToList();

            // Set up branch categories for display
            repo.BranchCategories.Clear();

            // GITFLOW category (if initialized - always show when GitFlow is active)
            var gitFlowConfig = await _gitFlowService.GetConfigAsync(repo.Path);
            if (gitFlowConfig?.IsInitialized == true)
            {
                var gitFlowBranches = localBranches
                    .Where(b => b.Name.StartsWith(gitFlowConfig.FeaturePrefix, StringComparison.OrdinalIgnoreCase) ||
                                b.Name.StartsWith(gitFlowConfig.ReleasePrefix, StringComparison.OrdinalIgnoreCase) ||
                                b.Name.StartsWith(gitFlowConfig.HotfixPrefix, StringComparison.OrdinalIgnoreCase) ||
                                b.Name.StartsWith(gitFlowConfig.SupportPrefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var gitFlowCategory = new BranchCategory
                {
                    Name = "GITFLOW",
                    Icon = "\uE8A3", // Flow icon
                    BranchCount = gitFlowBranches.Count,
                    IsExpanded = true
                };
                foreach (var branch in gitFlowBranches)
                {
                    gitFlowCategory.Branches.Add(branch);
                }
                repo.BranchCategories.Add(gitFlowCategory);
            }

            // LOCAL category
            var localCategory = new BranchCategory
            {
                Name = "LOCAL",
                Icon = "\uE8A3", // Branch icon
                BranchCount = localBranches.Count,
                IsExpanded = true
            };
            foreach (var branch in localBranches)
            {
                localCategory.Branches.Add(branch);
            }
            repo.BranchCategories.Add(localCategory);

            // REMOTE category
            var remoteCategory = new BranchCategory
            {
                Name = "REMOTE",
                Icon = "\uE774", // Cloud icon
                BranchCount = remoteBranches.Count,
                IsExpanded = true
            };
            foreach (var group in remoteGroups)
            {
                remoteCategory.RemoteGroups.Add(group);
            }
            repo.BranchCategories.Add(remoteCategory);

            repo.BranchesLoaded = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load branches: {ex.Message}";
        }
    }

    /// <summary>
    /// Checkout a branch.
    /// </summary>
    [RelayCommand]
    public async Task CheckoutBranchAsync(BranchInfo branch)
    {
        if (SelectedRepository == null) return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Checking out {branch.Name}...";

            // For remote branches, extract the name after origin/
            var branchName = branch.IsRemote && branch.Name.StartsWith("origin/")
                ? branch.Name["origin/".Length..]
                : branch.Name;

            await _gitService.CheckoutAsync(SelectedRepository.Path, branchName, allowConflicts: true);

            // Refresh the repo info
            var info = await _gitService.GetRepositoryInfoAsync(SelectedRepository.Path);
            SelectedRepository.CurrentBranch = info.CurrentBranch;
            SelectedRepository.IsMergeInProgress = info.IsMergeInProgress;
            SelectedRepository.MergingBranch = info.MergingBranch;
            SelectedRepository.ConflictCount = info.ConflictCount;

            // Reload branches to update current indicator
            SelectedRepository.BranchesLoaded = false;
            await LoadBranchesForRepoAsync(SelectedRepository);

            // Refresh git graph
            if (GitGraphViewModel != null)
            {
                await GitGraphViewModel.LoadRepositoryAsync(SelectedRepository.Path);
            }

            if (SelectedRepository.ConflictCount > 0)
            {
                if (string.IsNullOrEmpty(SelectedRepository.MergingBranch))
                {
                    SelectedRepository.MergingBranch = branchName;
                }

                StatusMessage = "Checkout has conflicts - resolve to complete";
                await RefreshMergeConflictResolutionAsync();
            }
            else
            {
                StatusMessage = $"Checked out {branchName}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Checkout failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Merge a branch into the current branch.
    /// </summary>
    [RelayCommand]
    public async Task MergeBranchAsync(BranchInfo branch)
    {
        System.Diagnostics.Debug.WriteLine($"[MainVM] MergeBranchAsync called for {branch.Name}");
        if (SelectedRepository == null)
        {
            System.Diagnostics.Debug.WriteLine("[MainVM] SelectedRepository is null");
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"Merging {branch.Name} into {SelectedRepository.CurrentBranch}...";
            System.Diagnostics.Debug.WriteLine($"[MainVM] Starting merge: {branch.Name} -> {SelectedRepository.CurrentBranch}");

            // Initial attempt
            var result = await _gitService.MergeBranchAsync(SelectedRepository.Path, branch.Name);
            System.Diagnostics.Debug.WriteLine($"[MainVM] Merge result: Success={result.Success}, Conflicts={result.HasConflicts}, UnrelatedHistories={result.HasUnrelatedHistories}");

            // Handle unrelated histories - prompt and retry
            if (!result.Success && result.HasUnrelatedHistories)
            {
                var dialogResult = System.Windows.MessageBox.Show(
                    "The branches have unrelated histories (no common ancestor).\n\n" +
                    "This can happen when branches were created independently or the repository was recreated.\n\n" +
                    "Do you want to merge anyway? This will combine the histories and may result in merge conflicts.",
                    "Unrelated Histories",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (dialogResult != System.Windows.MessageBoxResult.Yes)
                {
                    StatusMessage = "Merge cancelled";
                    return;
                }

                // Retry with flag - result variable is updated
                StatusMessage = $"Merging {branch.Name} (allowing unrelated histories)...";
                result = await _gitService.MergeBranchAsync(SelectedRepository.Path, branch.Name, allowUnrelatedHistories: true);
                System.Diagnostics.Debug.WriteLine($"[MainVM] Retry merge result: Success={result.Success}, Conflicts={result.HasConflicts}");
            }

            // Standard result handling (works for both initial and retry)
            if (result.Success)
            {
                StatusMessage = $"Successfully merged {branch.Name}";

                // Refresh git graph
                if (GitGraphViewModel != null)
                {
                    await GitGraphViewModel.LoadRepositoryAsync(SelectedRepository.Path);
                }
            }
            else if (result.HasConflicts)
            {
                StatusMessage = "Merge has conflicts - resolve to complete";

                // Refresh repo info to update merge banner and conflicts immediately
                var info = await _gitService.GetRepositoryInfoAsync(SelectedRepository.Path);
                SelectedRepository.IsMergeInProgress = info.IsMergeInProgress;
                SelectedRepository.MergingBranch = info.MergingBranch;
                SelectedRepository.ConflictCount = info.ConflictCount;

                await RefreshMergeConflictResolutionAsync();
                await RefreshAsync();
            }
            else
            {
                StatusMessage = $"Merge failed: {result.ErrorMessage}";
                System.Diagnostics.Debug.WriteLine($"[MainVM] Merge failure error: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Merge failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[MainVM] Merge exception: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task MergeBranchLabelAsync(BranchLabel label)
    {
        if (label == null)
            return;

        var name = label.IsRemote && !label.IsLocal && label.RemoteName != null
            ? $"{label.RemoteName}/{label.Name}"
            : label.Name;

        var branch = new BranchInfo
        {
            Name = name,
            IsRemote = label.IsRemote,
            RemoteName = label.RemoteName,
            IsCurrent = label.IsCurrent
        };

        await MergeBranchAsync(branch);
    }

    [RelayCommand]
    public async Task FastForwardBranchLabelAsync(BranchLabel label)
    {
        if (label == null || SelectedRepository == null)
            return;

        var targetName = label.IsRemote && label.RemoteName != null
            ? $"{label.RemoteName}/{label.Name}"
            : label.Name;

        IsBusy = true;
        StatusMessage = $"Fast-forwarding to {targetName}...";

        try
        {
            var result = await _gitService.FastForwardAsync(SelectedRepository.Path, targetName);

            if (result.Success)
            {
                StatusMessage = $"Fast-forwarded to {targetName}";
                await RefreshAsync();
            }
            else
            {
                StatusMessage = result.ErrorMessage ?? "Fast-forward failed";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fast-forward failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Continue an in-progress merge (open conflict resolution UI).
    /// </summary>
    [RelayCommand]
    public async Task ContinueMergeAsync()
    {
        if (SelectedRepository == null) return;

        await RefreshMergeConflictResolutionAsync();

        if (MergeConflictResolutionViewModel == null) return;

        var conflictWindow = new Views.ConflictResolutionView
        {
            DataContext = MergeConflictResolutionViewModel,
            Owner = System.Windows.Application.Current.MainWindow
        };

        conflictWindow.ShowDialog();
    }

    /// <summary>
    /// Open the first unresolved conflict in VS Code.
    /// </summary>
    [RelayCommand]
    public async Task OpenInVsCodeAsync()
    {
        if (SelectedRepository == null) return;

        try
        {
            IsBusy = true;
            StatusMessage = "Opening VS Code for merge...";

            var conflicts = await _gitService.GetConflictsAsync(SelectedRepository.Path);
            var firstConflict = conflicts.FirstOrDefault();

            if (firstConflict != null)
            {
                await _gitService.OpenConflictInVsCodeAsync(SelectedRepository.Path, firstConflict.FilePath);
                
                // Refresh to check if resolved
                await RefreshAsync();
                
                // If there are more conflicts, we could prompt to open the next one, 
                // but let's just refresh for now.
                var remaining = await _gitService.GetConflictsAsync(SelectedRepository.Path);
                if (remaining.Count == 0)
                {
                    StatusMessage = "All conflicts resolved in VS Code.";
                }
                else
                {
                    StatusMessage = $"Conflict resolved. {remaining.Count} remaining.";
                }
            }
            else
            {
                StatusMessage = "No conflicts found to open.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to open VS Code: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Abort the current in-progress merge.
    /// </summary>
    [RelayCommand]
    public async Task AbortMergeAsync()
    {
        if (SelectedRepository == null) return;

        try
        {
            IsBusy = true;
            StatusMessage = "Aborting merge...";

            await _gitService.AbortMergeAsync(SelectedRepository.Path);

            StatusMessage = "Merge aborted";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Abort merge failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task OpenConflictInVsCodeAsync(ConflictInfo? conflict)
    {
        if (SelectedRepository == null || conflict == null) return;

        try
        {
            IsBusy = true;
            StatusMessage = "Opening VS Code for merge...";

            await _gitService.OpenConflictInVsCodeAsync(SelectedRepository.Path, conflict.FilePath);

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to open VS Code: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task OpenConflictInLeafAsync(ConflictInfo? conflict)
    {
        if (SelectedRepository == null || conflict == null) return;

        await RefreshMergeConflictResolutionAsync();
        if (MergeConflictResolutionViewModel == null) return;

        MergeConflictResolutionViewModel.SelectedConflict = conflict;

        var conflictWindow = new Views.ConflictResolutionView
        {
            DataContext = MergeConflictResolutionViewModel,
            Owner = System.Windows.Application.Current.MainWindow
        };

        conflictWindow.ShowDialog();
    }

    [RelayCommand]
    public async Task UnresolveMergeConflictAsync(ConflictInfo? conflict)
    {
        if (MergeConflictResolutionViewModel == null || conflict == null)
            return;

        await MergeConflictResolutionViewModel.UnresolveConflictCommand.ExecuteAsync(conflict);
        await RefreshMergeConflictResolutionAsync();
    }

    private async Task RefreshMergeConflictResolutionAsync(bool showInline = false)
    {
        if (SelectedRepository == null)
        {
            return;
        }

        var hasMergeConflicts = SelectedRepository.IsMergeInProgress || SelectedRepository.ConflictCount > 0;
        System.Diagnostics.Debug.WriteLine($"[MainVM] RefreshMergeConflictResolutionAsync merge={SelectedRepository.IsMergeInProgress} conflictCount={SelectedRepository.ConflictCount}");
        if (!hasMergeConflicts)
        {
            if (MergeConflictResolutionViewModel != null)
            {
                MergeConflictResolutionViewModel.MergeCompleted -= OnMergeConflictResolutionCompleted;
            }

            MergeConflictResolutionViewModel = null;
            _mergeConflictRepoPath = null;
            await _gitService.ClearStoredMergeConflictFilesAsync(SelectedRepository.Path);
            return;
        }

        if (string.IsNullOrEmpty(SelectedRepository.MergingBranch))
        {
            var info = await _gitService.GetRepositoryInfoAsync(SelectedRepository.Path);
            SelectedRepository.MergingBranch = info.MergingBranch;
        }

        var isNewViewModel = MergeConflictResolutionViewModel == null ||
            !string.Equals(_mergeConflictRepoPath, SelectedRepository.Path, StringComparison.OrdinalIgnoreCase);

        if (isNewViewModel)
        {
            if (MergeConflictResolutionViewModel != null)
            {
                MergeConflictResolutionViewModel.MergeCompleted -= OnMergeConflictResolutionCompleted;
            }

            var conflictViewModel = new ConflictResolutionViewModel(_gitService, SelectedRepository.Path);
            conflictViewModel.MergeCompleted += OnMergeConflictResolutionCompleted;
            MergeConflictResolutionViewModel = conflictViewModel;
            _mergeConflictRepoPath = SelectedRepository.Path;
        }

        if (MergeConflictResolutionViewModel == null)
        {
            return;
        }

        MergeConflictResolutionViewModel.SourceBranch = !string.IsNullOrEmpty(SelectedRepository.MergingBranch)
            ? SelectedRepository.MergingBranch
            : "Incoming";
        MergeConflictResolutionViewModel.TargetBranch = SelectedRepository.CurrentBranch ?? "HEAD";

        await MergeConflictResolutionViewModel.LoadConflictsAsync(showLoading: isNewViewModel);
    }

    private async void OnMergeConflictResolutionCompleted(object? sender, bool success)
    {
        StatusMessage = success ? "Merge completed successfully" : "Merge aborted";
        await RefreshAsync();
    }

    #region GitFlow Commands

    /// <summary>
    /// Initialize GitFlow in the current repository.
    /// </summary>
    [RelayCommand]
    public async Task InitializeGitFlowAsync()
    {
        if (SelectedRepository == null) return;

        var dialog = new Views.GitFlowInitDialog(_gitFlowService, _settingsService, SelectedRepository.Path)
        {
            Owner = _ownerWindow
        };

        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            StatusMessage = "GitFlow initialized successfully";
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Start a new GitFlow feature branch.
    /// </summary>
    [RelayCommand]
    public async Task StartFeatureAsync()
    {
        if (SelectedRepository == null) return;

        var isInitialized = await _gitFlowService.IsInitializedAsync(SelectedRepository.Path);
        if (!isInitialized)
        {
            MessageBox.Show("GitFlow is not initialized in this repository.\n\nPlease initialize GitFlow first.",
                "GitFlow Not Initialized", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Views.StartBranchDialog(_gitFlowService, SelectedRepository.Path, Models.GitFlowBranchType.Feature)
        {
            Owner = _ownerWindow
        };

        if (dialog.ShowDialog() == true)
        {
            StatusMessage = $"Started feature {dialog.BranchName}";
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Start a new GitFlow release branch.
    /// </summary>
    [RelayCommand]
    public async Task StartReleaseAsync()
    {
        if (SelectedRepository == null) return;

        var isInitialized = await _gitFlowService.IsInitializedAsync(SelectedRepository.Path);
        if (!isInitialized)
        {
            MessageBox.Show("GitFlow is not initialized in this repository.\n\nPlease initialize GitFlow first.",
                "GitFlow Not Initialized", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Views.StartBranchDialog(_gitFlowService, SelectedRepository.Path, Models.GitFlowBranchType.Release)
        {
            Owner = _ownerWindow
        };

        if (dialog.ShowDialog() == true)
        {
            StatusMessage = $"Started release {dialog.BranchName}";
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Start a new GitFlow hotfix branch.
    /// </summary>
    [RelayCommand]
    public async Task StartHotfixAsync()
    {
        if (SelectedRepository == null) return;

        var isInitialized = await _gitFlowService.IsInitializedAsync(SelectedRepository.Path);
        if (!isInitialized)
        {
            MessageBox.Show("GitFlow is not initialized in this repository.\n\nPlease initialize GitFlow first.",
                "GitFlow Not Initialized", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Views.StartBranchDialog(_gitFlowService, SelectedRepository.Path, Models.GitFlowBranchType.Hotfix)
        {
            Owner = _ownerWindow
        };

        if (dialog.ShowDialog() == true)
        {
            StatusMessage = $"Started hotfix {dialog.BranchName}";
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Finish a GitFlow branch (feature, release, or hotfix).
    /// </summary>
    [RelayCommand]
    public async Task FinishGitFlowBranchAsync(BranchInfo branch)
    {
        if (SelectedRepository == null || branch == null) return;

        var config = await _gitFlowService.GetConfigAsync(SelectedRepository.Path);
        if (config == null)
        {
            MessageBox.Show("GitFlow is not initialized in this repository.",
                "GitFlow Not Initialized", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var branchType = _gitFlowService.GetBranchType(branch.Name, config);
        var flowName = _gitFlowService.GetFlowName(branch.Name, config);

        if (branchType == Models.GitFlowBranchType.None || string.IsNullOrEmpty(flowName))
        {
            MessageBox.Show("This branch is not a GitFlow branch (feature, release, or hotfix).",
                "Not a GitFlow Branch", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Views.FinishBranchDialog(_gitFlowService, SelectedRepository.Path, branch.Name, branchType, flowName)
        {
            Owner = _ownerWindow
        };

        if (dialog.ShowDialog() == true)
        {
            StatusMessage = $"Finished {branchType.ToString().ToLower()} {flowName}";
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Publish a GitFlow branch to remote.
    /// </summary>
    [RelayCommand]
    public async Task PublishGitFlowBranchAsync(BranchInfo branch)
    {
        if (SelectedRepository == null || branch == null) return;

        var config = await _gitFlowService.GetConfigAsync(SelectedRepository.Path);
        if (config == null)
        {
            MessageBox.Show("GitFlow is not initialized in this repository.",
                "GitFlow Not Initialized", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var branchType = _gitFlowService.GetBranchType(branch.Name, config);
        var flowName = _gitFlowService.GetFlowName(branch.Name, config);

        if (branchType == Models.GitFlowBranchType.None || string.IsNullOrEmpty(flowName))
        {
            MessageBox.Show("This branch is not a GitFlow branch.",
                "Not a GitFlow Branch", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"Publishing {branchType.ToString().ToLower()} {flowName}...";

            var progress = new Progress<string>(msg => StatusMessage = msg);

            switch (branchType)
            {
                case Models.GitFlowBranchType.Feature:
                    await _gitFlowService.PublishFeatureAsync(SelectedRepository.Path, flowName, progress);
                    break;
                case Models.GitFlowBranchType.Release:
                    await _gitFlowService.PublishReleaseAsync(SelectedRepository.Path, flowName, progress);
                    break;
                case Models.GitFlowBranchType.Hotfix:
                    await _gitFlowService.PublishHotfixAsync(SelectedRepository.Path, flowName, progress);
                    break;
            }

            StatusMessage = $"Published {branchType.ToString().ToLower()} {flowName}";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Publish failed: {ex.Message}";
            MessageBox.Show($"Failed to publish branch:\n\n{ex.Message}",
                "Publish Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion
}
