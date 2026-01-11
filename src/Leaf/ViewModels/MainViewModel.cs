using System.Collections.ObjectModel;
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
    private bool _isCommitDetailVisible = true;

    [ObservableProperty]
    private bool _isWorkingChangesSelected;

    [ObservableProperty]
    private bool _isRepoPaneCollapsed = true;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _commitSearchText = string.Empty;

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

    public MainViewModel(IGitService gitService, CredentialService credentialService, SettingsService settingsService, Window ownerWindow)
    {
        _gitService = gitService;
        _credentialService = credentialService;
        _settingsService = settingsService;
        _ownerWindow = ownerWindow;
        _fileWatcherService = new FileWatcherService();

        _gitGraphViewModel = new GitGraphViewModel(gitService);
        _commitDetailViewModel = new CommitDetailViewModel(gitService);
        _workingChangesViewModel = new WorkingChangesViewModel(gitService);

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

            // Load branches for the branch panel
            await LoadBranchesForRepoAsync(repository);

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
    }

    /// <summary>
    /// Open settings.
    /// </summary>
    [RelayCommand]
    public void OpenSettings()
    {
        var dialog = new SettingsDialog(_credentialService, _settingsService)
        {
            Owner = _ownerWindow
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
    public async Task CreateBranchAsync()
    {
        if (SelectedRepository == null) return;

        // Simple input dialog - in a real app you'd use a proper dialog
        var branchName = Microsoft.VisualBasic.Interaction.InputBox(
            "Enter branch name:",
            "Create Branch",
            "");

        if (string.IsNullOrWhiteSpace(branchName)) return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Creating branch '{branchName}'...";

            await _gitService.CreateBranchAsync(SelectedRepository.Path, branchName);

            StatusMessage = $"Created and checked out branch '{branchName}'";
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
    /// Pop stashed changes.
    /// </summary>
    [RelayCommand]
    public async Task PopStashAsync()
    {
        if (SelectedRepository == null) return;

        try
        {
            IsBusy = true;
            StatusMessage = "Popping stash...";

            await _gitService.PopStashAsync(SelectedRepository.Path);

            StatusMessage = "Stash popped";
            await RefreshAsync();
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
                .Select(g => new RemoteBranchGroup
                {
                    Name = g.Key,
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
                })
                .OrderBy(g => g.Name)
                .ToList();

            // Set up branch categories for display
            repo.BranchCategories.Clear();

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

            await _gitService.CheckoutAsync(SelectedRepository.Path, branchName);

            // Refresh the repo info
            var info = await _gitService.GetRepositoryInfoAsync(SelectedRepository.Path);
            SelectedRepository.CurrentBranch = info.CurrentBranch;

            // Reload branches to update current indicator
            SelectedRepository.BranchesLoaded = false;
            await LoadBranchesForRepoAsync(SelectedRepository);

            // Refresh git graph
            if (GitGraphViewModel != null)
            {
                await GitGraphViewModel.LoadRepositoryAsync(SelectedRepository.Path);
            }

            StatusMessage = $"Checked out {branchName}";
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
}
