using System;
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
    private readonly IRepositoryManagementService _repositoryService;
    private readonly IAutoFetchService _autoFetchService;
    private readonly Window _ownerWindow;
    private readonly FileWatcherService _fileWatcherService;
    private string? _pendingBranchBaseSha;

    /// <summary>
    /// Auto-fetch timer interval (10 minutes).
    /// </summary>
    private static readonly TimeSpan AutoFetchInterval = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Event raised when a repository should be visually selected in the TreeView.
    /// </summary>
    public event EventHandler<RepositoryInfo>? RequestRepositorySelection;

    /// <summary>
    /// Last fetch time - delegated to AutoFetchService.
    /// </summary>
    public DateTime? LastFetchTime => _autoFetchService.LastFetchTime;

    /// <summary>
    /// Repository groups - delegated to RepositoryManagementService.
    /// </summary>
    public ObservableCollection<RepositoryGroup> RepositoryGroups => _repositoryService.RepositoryGroups;

    [ObservableProperty]
    private RepositoryInfo? _selectedRepository;

    [ObservableProperty]
    private GitGraphViewModel? _gitGraphViewModel;

    [ObservableProperty]
    private CommitDetailViewModel? _commitDetailViewModel;

    [ObservableProperty]
    private WorkingChangesViewModel? _workingChangesViewModel;

    [ObservableProperty]
    private DiffViewerViewModel? _diffViewerViewModel;

    [ObservableProperty]
    private ConflictResolutionViewModel? _mergeConflictResolutionViewModel;

    [ObservableProperty]
    private bool _isCommitDetailVisible = true;

    [ObservableProperty]
    private bool _isWorkingChangesSelected;

    [ObservableProperty]
    private bool _isDiffViewerVisible;

    [ObservableProperty]
    private bool _isRepoPaneCollapsed;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _commitSearchText = string.Empty;

    /// <summary>
    /// Pinned repositories - delegated to RepositoryManagementService.
    /// </summary>
    public ObservableCollection<RepositoryInfo> PinnedRepositories => _repositoryService.PinnedRepositories;

    /// <summary>
    /// Recent repositories - delegated to RepositoryManagementService.
    /// </summary>
    public ObservableCollection<RepositoryInfo> RecentRepositories => _repositoryService.RecentRepositories;

    /// <summary>
    /// Repository root items for tree view - delegated to RepositoryManagementService.
    /// </summary>
    public ObservableCollection<object> RepositoryRootItems => _repositoryService.RepositoryRootItems;

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

    public MainViewModel(
        IGitService gitService,
        CredentialService credentialService,
        SettingsService settingsService,
        IGitFlowService gitFlowService,
        IRepositoryManagementService repositoryService,
        IAutoFetchService autoFetchService,
        Window ownerWindow)
    {
        _gitService = gitService;
        _gitFlowService = gitFlowService;
        _credentialService = credentialService;
        _settingsService = settingsService;
        _repositoryService = repositoryService;
        _autoFetchService = autoFetchService;
        _ownerWindow = ownerWindow;
        _fileWatcherService = new FileWatcherService();

        // Subscribe to auto-fetch completion
        _autoFetchService.FetchCompleted += OnAutoFetchCompleted;

        _gitGraphViewModel = new GitGraphViewModel(gitService);
        _commitDetailViewModel = new CommitDetailViewModel(gitService);
        _workingChangesViewModel = new WorkingChangesViewModel(gitService, settingsService);
        _diffViewerViewModel = new DiffViewerViewModel();
        _diffViewerViewModel.CloseRequested += (s, e) => CloseDiffViewer();

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
        _autoFetchService.Start(AutoFetchInterval, () => SelectedRepository?.Path);
    }

    /// <summary>
    /// Stop the auto-fetch timer.
    /// </summary>
    public void StopAutoFetchTimer()
    {
        _autoFetchService.Stop();
    }

    /// <summary>
    /// Handle auto-fetch completion - update UI state.
    /// </summary>
    private void OnAutoFetchCompleted(object? sender, AutoFetchCompletedEventArgs e)
    {
        if (SelectedRepository == null)
            return;

        // Update ahead/behind counts
        SelectedRepository.AheadBy = e.AheadBy;
        SelectedRepository.BehindBy = e.BehindBy;

        // Update status
        StatusMessage = $"Auto-fetched at {e.FetchTime:HH:mm}" +
                       (e.AheadBy > 0 ? $" | ↑{e.AheadBy}" : "") +
                       (e.BehindBy > 0 ? $" | ↓{e.BehindBy}" : "");

        // Notify that LastFetchTime changed (property delegates to service)
        OnPropertyChanged(nameof(LastFetchTime));
    }

    /// <summary>
    /// Load repositories from persistent storage.
    /// </summary>
    private async void LoadSavedRepositories()
    {
        // Load UI state from settings
        var settings = _settingsService.LoadSettings();
        IsRepoPaneCollapsed = settings.IsRepoPaneCollapsed;

        // Load repositories via service
        var lastSelectedPath = await _repositoryService.LoadRepositoriesAsync();

        // Restore last selected repository
        if (!string.IsNullOrEmpty(lastSelectedPath))
        {
            var lastRepo = _repositoryService.FindRepository(lastSelectedPath);
            if (lastRepo != null)
            {
                await SelectRepositoryAsync(lastRepo);
                // Request the View to visually select the repository in the TreeView
                RequestRepositorySelection?.Invoke(this, lastRepo);
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

            if (!await _gitService.IsValidRepositoryAsync(path))
            {
                StatusMessage = "Selected folder is not a valid Git repository";
                return;
            }

            var repoInfo = await _gitService.GetRepositoryInfoAsync(path);
            _repositoryService.AddRepository(repoInfo);
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
                    if (_repositoryService.ContainsRepository(repoPath))
                        continue;

                    if (await _gitService.IsValidRepositoryAsync(repoPath))
                    {
                        var repoInfo = await _gitService.GetRepositoryInfoAsync(repoPath);
                        _repositoryService.AddRepository(repoInfo);
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
            _repositoryService.AddRepository(repoInfo);
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

        // Close diff viewer when switching repositories
        IsDiffViewerVisible = false;

        try
        {
            IsBusy = true;
            SelectedRepository = repository;

            // Mark as recently accessed (updates quick access sections)
            _repositoryService.MarkAsRecentlyAccessed(repository);

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

                // Sync working changes to the staging view if it's currently selected
                if (IsWorkingChangesSelected && WorkingChangesViewModel != null)
                {
                    WorkingChangesViewModel.SetWorkingChanges(repository.Path, GitGraphViewModel.WorkingChanges);
                }
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
        _pendingBranchBaseSha = null;
        NewBranchName = string.Empty;
        IsBranchInputVisible = true;
    }

    [RelayCommand]
    public void CreateBranchAtCommit(CommitInfo commit)
    {
        if (SelectedRepository == null || commit == null)
            return;

        _pendingBranchBaseSha = commit.Sha;
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
            if (!string.IsNullOrWhiteSpace(_pendingBranchBaseSha))
            {
                StatusMessage = $"Creating branch '{branchName}' at {_pendingBranchBaseSha[..7]}...";
                await _gitService.CreateBranchAtCommitAsync(SelectedRepository.Path, branchName, _pendingBranchBaseSha);
                StatusMessage = $"Created and checked out branch '{branchName}'";
            }
            else
            {
                StatusMessage = $"Creating branch '{branchName}'...";
                await _gitService.CreateBranchAsync(SelectedRepository.Path, branchName);
                StatusMessage = $"Created and checked out branch '{branchName}'";
            }
            SelectedRepository.BranchesLoaded = false;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Create branch failed: {ex.Message}";
        }
        finally
        {
            _pendingBranchBaseSha = null;
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void CancelBranchInput()
    {
        IsBranchInputVisible = false;
        NewBranchName = string.Empty;
        _pendingBranchBaseSha = null;
    }

    /// <summary>
    /// Delete a branch.
    /// </summary>
    [RelayCommand]
    public async Task DeleteBranchAsync(BranchInfo branch)
    {
        if (SelectedRepository == null || branch == null)
            return;

        if (!await ConfirmBranchDeletionAsync(branch))
            return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Deleting branch {branch.Name}...";

            if (branch.IsRemote)
            {
                var remoteName = branch.RemoteName ?? "origin";
                var branchName = GetRemoteBranchShortName(branch.Name, remoteName);
                await _gitService.DeleteRemoteBranchAsync(SelectedRepository.Path, remoteName, branchName);
            }
            else
            {
                await _gitService.DeleteBranchAsync(SelectedRepository.Path, branch.Name, force: false);
            }

            StatusMessage = $"Deleted branch {branch.Name}";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            if (!branch.IsRemote && await ConfirmForceDeleteAsync(branch, ex.Message))
            {
                try
                {
                    await _gitService.DeleteBranchAsync(SelectedRepository.Path, branch.Name, force: true);
                    StatusMessage = $"Force deleted branch {branch.Name}";
                    await RefreshAsync();
                    return;
                }
                catch (Exception forceEx)
                {
                    StatusMessage = $"Delete branch failed: {forceEx.Message}";
                }
            }
            else
            {
                StatusMessage = $"Delete branch failed: {ex.Message}";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string GetRemoteBranchShortName(string branchName, string remoteName)
    {
        var prefix = remoteName + "/";
        return branchName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? branchName[prefix.Length..]
            : branchName;
    }

    private Task<bool> ConfirmBranchDeletionAsync(BranchInfo branch)
    {
        return Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (branch.IsCurrent)
            {
                MessageBox.Show("Cannot delete the currently checked out branch.",
                    "Delete Branch", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var scope = branch.IsRemote ? "remote" : "local";
            var result = MessageBox.Show(
                $"Delete {scope} branch '{branch.Name}'?\n\nThis cannot be undone.",
                "Delete Branch",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            return result == MessageBoxResult.Yes;
        }).Task;
    }

    private Task<bool> ConfirmForceDeleteAsync(BranchInfo branch, string error)
    {
        return Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var result = MessageBox.Show(
                $"Failed to delete branch '{branch.Name}'.\n\n{error}\n\nForce delete this branch?",
                "Force Delete Branch",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            return result == MessageBoxResult.Yes;
        }).Task;
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

    /// <summary>
    /// Show the diff viewer for a file in a commit.
    /// </summary>
    public async Task ShowFileDiffAsync(Models.FileChangeInfo file, string commitSha)
    {
        if (SelectedRepository == null || DiffViewerViewModel == null)
            return;

        DiffViewerViewModel.IsLoading = true;
        IsDiffViewerVisible = true;

        try
        {
            // Get the file content from the commit
            var (oldContent, newContent) = await _gitService.GetFileDiffAsync(
                SelectedRepository.Path, commitSha, file.Path);

            // Compute the diff
            var diffService = new Services.DiffService();
            var result = diffService.ComputeDiff(oldContent, newContent, file.FileName, file.Path);

            // Load into the view model
            DiffViewerViewModel.LoadDiff(result);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load diff: {ex.Message}";
            IsDiffViewerVisible = false;
        }
        finally
        {
            DiffViewerViewModel.IsLoading = false;
        }
    }

    /// <summary>
    /// Close the diff viewer.
    /// </summary>
    public void CloseDiffViewer()
    {
        IsDiffViewerVisible = false;
        DiffViewerViewModel?.Clear();
    }

    private static FileDiffResult BuildUnifiedDiffResult(string diffText, string title)
    {
        var result = new FileDiffResult
        {
            FileName = title,
            FilePath = title,
            InlineContent = diffText
        };

        int linesAdded = 0;
        int linesDeleted = 0;

        foreach (var rawLine in diffText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var type = DiffLineType.Unchanged;

            if (line.StartsWith("+") && !line.StartsWith("+++"))
            {
                type = DiffLineType.Added;
                linesAdded++;
            }
            else if (line.StartsWith("-") && !line.StartsWith("---"))
            {
                type = DiffLineType.Deleted;
                linesDeleted++;
            }
            else if (line.StartsWith("@@"))
            {
                type = DiffLineType.Modified;
            }

            result.Lines.Add(new DiffLine
            {
                Text = line,
                Type = type
            });
        }

        result.LinesAddedCount = linesAdded;
        result.LinesDeletedCount = linesDeleted;

        return result;
    }

    /// <summary>
    /// Show diff for an unstaged file (working directory vs index).
    /// </summary>
    public async Task ShowUnstagedFileDiffAsync(Models.FileStatusInfo file)
    {
        if (SelectedRepository == null || DiffViewerViewModel == null)
            return;

        DiffViewerViewModel.IsLoading = true;
        IsDiffViewerVisible = true;

        try
        {
            var (oldContent, newContent) = await _gitService.GetUnstagedFileDiffAsync(
                SelectedRepository.Path, file.Path);

            var diffService = new Services.DiffService();
            var result = diffService.ComputeDiff(oldContent, newContent, file.FileName, file.Path);

            DiffViewerViewModel.LoadDiff(result);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load diff: {ex.Message}";
            IsDiffViewerVisible = false;
        }
        finally
        {
            DiffViewerViewModel.IsLoading = false;
        }
    }

    /// <summary>
    /// Show diff for a staged file (index vs HEAD).
    /// </summary>
    public async Task ShowStagedFileDiffAsync(Models.FileStatusInfo file)
    {
        if (SelectedRepository == null || DiffViewerViewModel == null)
            return;

        DiffViewerViewModel.IsLoading = true;
        IsDiffViewerVisible = true;

        try
        {
            var (oldContent, newContent) = await _gitService.GetStagedFileDiffAsync(
                SelectedRepository.Path, file.Path);

            var diffService = new Services.DiffService();
            var result = diffService.ComputeDiff(oldContent, newContent, file.FileName, file.Path);

            DiffViewerViewModel.LoadDiff(result);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load diff: {ex.Message}";
            IsDiffViewerVisible = false;
        }
        finally
        {
            DiffViewerViewModel.IsLoading = false;
        }
    }

    [RelayCommand]
    public void TogglePinRepository(RepositoryInfo repo)
    {
        _repositoryService.TogglePinRepository(repo);
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

        _repositoryService.RemoveRepository(repo);
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

            // Build all categories first, then assign as a new collection (atomic operation)
            var categories = new ObservableCollection<BranchCategory>();

            // GITFLOW category (if initialized - always show when GitFlow is active)
            var gitFlowConfig = await _gitFlowService.GetConfigAsync(repo.Path);
            if (gitFlowConfig?.IsInitialized == true)
            {
                // Classify all branches by GitFlow type for proper coloring
                ClassifyBranchesByGitFlowType(localBranches, gitFlowConfig);

                var gitFlowBranches = localBranches
                    .Where(b => b.GitFlowType is GitFlowBranchType.Feature or GitFlowBranchType.Release
                                                 or GitFlowBranchType.Hotfix or GitFlowBranchType.Support)
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
                categories.Add(gitFlowCategory);
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
            categories.Add(localCategory);

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
            categories.Add(remoteCategory);

            // Assign new collection (replaces entire collection atomically)
            repo.BranchCategories = categories;

            // Auto-select the current branch
            var currentBranch = localBranches.FirstOrDefault(b => b.IsCurrent);
            if (currentBranch != null)
            {
                repo.ClearBranchSelection();
                currentBranch.IsSelected = true;
                repo.SelectedBranches.Add(currentBranch);
            }

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
    public async Task CheckoutCommitAsync(CommitInfo commit)
    {
        if (commit == null || SelectedRepository == null)
            return;

        IsBusy = true;
        StatusMessage = $"Checking out commit {commit.ShortSha}...";

        try
        {
            await _gitService.CheckoutCommitAsync(SelectedRepository.Path, commit.Sha);
            StatusMessage = $"Checked out commit {commit.ShortSha} (detached HEAD)";
            await RefreshAsync();
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

    [RelayCommand]
    public void CopyCommitSha(CommitInfo commit)
    {
        if (commit == null)
            return;

        Clipboard.SetText(commit.Sha);
        StatusMessage = $"Copied {commit.ShortSha} to clipboard";
    }

    [RelayCommand]
    public async Task CherryPickCommitAsync(CommitInfo commit)
    {
        if (commit == null || SelectedRepository == null)
            return;

        IsBusy = true;
        StatusMessage = $"Cherry-picking {commit.ShortSha}...";

        try
        {
            var result = await _gitService.CherryPickAsync(SelectedRepository.Path, commit.Sha);
            if (result.Success)
            {
                StatusMessage = $"Cherry-picked {commit.ShortSha}";
                await RefreshAsync();
            }
            else if (result.HasConflicts)
            {
                StatusMessage = $"Cherry-pick has conflicts: {commit.ShortSha}";
                await RefreshAsync();
            }
            else
            {
                StatusMessage = $"Cherry-pick failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Cherry-pick failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task CompareCommitToWorkingDirectoryAsync(CommitInfo commit)
    {
        if (commit == null || SelectedRepository == null || DiffViewerViewModel == null)
            return;

        DiffViewerViewModel.IsLoading = true;
        IsDiffViewerVisible = true;

        try
        {
            var diffText = await _gitService.GetCommitToWorkingTreeDiffAsync(SelectedRepository.Path, commit.Sha);
            if (string.IsNullOrWhiteSpace(diffText))
            {
                StatusMessage = "No differences between commit and working directory";
                IsDiffViewerVisible = false;
                return;
            }

            var diffResult = BuildUnifiedDiffResult(diffText, $"Working Directory vs {commit.ShortSha}");
            DiffViewerViewModel.LoadDiff(diffResult);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Compare failed: {ex.Message}";
            IsDiffViewerVisible = false;
        }
        finally
        {
            DiffViewerViewModel.IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task CreateTagAtCommitAsync(CommitInfo commit)
    {
        if (commit == null || SelectedRepository == null)
            return;

        var dialog = new CreateTagDialog
        {
            Owner = _ownerWindow
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Creating tag '{dialog.TagName}'...";
            await _gitService.CreateTagAsync(SelectedRepository.Path, dialog.TagName, dialog.TagMessage, commit.Sha);
            StatusMessage = $"Created tag '{dialog.TagName}'";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Create tag failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
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

        var dialog = new Views.StartBranchDialog(_gitFlowService, _gitService, SelectedRepository.Path, Models.GitFlowBranchType.Feature)
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

        var dialog = new Views.StartBranchDialog(_gitFlowService, _gitService, SelectedRepository.Path, Models.GitFlowBranchType.Release)
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

        var dialog = new Views.StartBranchDialog(_gitFlowService, _gitService, SelectedRepository.Path, Models.GitFlowBranchType.Hotfix)
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

    /// <summary>
    /// Get GitFlow configuration for the selected repository.
    /// </summary>
    public async Task<GitFlowConfig?> GetGitFlowConfigAsync()
    {
        if (SelectedRepository == null) return null;
        return await _gitFlowService.GetConfigAsync(SelectedRepository.Path);
    }

    /// <summary>
    /// Get GitFlow status for the selected repository.
    /// </summary>
    public async Task<GitFlowStatus?> GetGitFlowStatusAsync()
    {
        if (SelectedRepository == null) return null;
        return await _gitFlowService.GetStatusAsync(SelectedRepository.Path);
    }

    /// <summary>
    /// Get suggested version for release or hotfix.
    /// </summary>
    public async Task<SemanticVersion?> GetSuggestedVersionAsync(GitFlowBranchType branchType)
    {
        if (SelectedRepository == null) return null;
        try
        {
            return await _gitFlowService.SuggestNextVersionAsync(SelectedRepository.Path, branchType);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get repository info for the selected repository.
    /// </summary>
    public async Task<RepositoryInfo?> GetRepositoryInfoAsync()
    {
        if (SelectedRepository == null) return null;
        return await _gitService.GetRepositoryInfoAsync(SelectedRepository.Path);
    }

    /// <summary>
    /// Stash changes with a message.
    /// </summary>
    public async Task StashChangesAsync(string message)
    {
        if (SelectedRepository == null) return;
        await _gitService.StashAsync(SelectedRepository.Path, message);
    }

    /// <summary>
    /// Create a GitFlow branch (feature, release, or hotfix).
    /// </summary>
    public async Task CreateGitFlowBranchAsync(GitFlowBranchType branchType, string name)
    {
        if (SelectedRepository == null)
            throw new InvalidOperationException("No repository selected.");

        var isInitialized = await _gitFlowService.IsInitializedAsync(SelectedRepository.Path);
        if (!isInitialized)
            throw new InvalidOperationException("GitFlow is not initialized in this repository.");

        var progress = new Progress<string>(msg => StatusMessage = msg);

        switch (branchType)
        {
            case GitFlowBranchType.Feature:
                await _gitFlowService.StartFeatureAsync(SelectedRepository.Path, name, progress);
                StatusMessage = $"Started feature '{name}'";
                break;
            case GitFlowBranchType.Release:
                await _gitFlowService.StartReleaseAsync(SelectedRepository.Path, name, progress);
                StatusMessage = $"Started release '{name}'";
                break;
            case GitFlowBranchType.Hotfix:
                await _gitFlowService.StartHotfixAsync(SelectedRepository.Path, name, progress);
                StatusMessage = $"Started hotfix '{name}'";
                break;
            default:
                throw new ArgumentException($"Unsupported branch type: {branchType}");
        }

        await RefreshAsync();
    }

    /// <summary>
    /// Classifies branches by their GitFlow type based on the GitFlow configuration.
    /// Sets the GitFlowType property on each branch for proper coloring.
    /// </summary>
    private static void ClassifyBranchesByGitFlowType(IEnumerable<BranchInfo> branches, GitFlowConfig config)
    {
        foreach (var branch in branches)
        {
            branch.GitFlowType = GetGitFlowBranchType(branch.Name, config);
        }
    }

    /// <summary>
    /// Determines the GitFlow branch type for a branch name.
    /// </summary>
    private static GitFlowBranchType GetGitFlowBranchType(string branchName, GitFlowConfig config)
    {
        // Check for exact matches first (main/develop)
        if (branchName.Equals(config.MainBranch, StringComparison.OrdinalIgnoreCase))
            return GitFlowBranchType.Main;

        if (branchName.Equals(config.DevelopBranch, StringComparison.OrdinalIgnoreCase))
            return GitFlowBranchType.Develop;

        // Check for prefixed branches
        if (branchName.StartsWith(config.FeaturePrefix, StringComparison.OrdinalIgnoreCase))
            return GitFlowBranchType.Feature;

        if (branchName.StartsWith(config.ReleasePrefix, StringComparison.OrdinalIgnoreCase))
            return GitFlowBranchType.Release;

        if (branchName.StartsWith(config.HotfixPrefix, StringComparison.OrdinalIgnoreCase))
            return GitFlowBranchType.Hotfix;

        if (branchName.StartsWith(config.SupportPrefix, StringComparison.OrdinalIgnoreCase))
            return GitFlowBranchType.Support;

        return GitFlowBranchType.None;
    }

    #endregion
}
