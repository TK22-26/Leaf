using System;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Views;
using Microsoft.Win32;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial - Repository management operations.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Load repositories from persistent storage.
    /// </summary>
    private async void LoadSavedRepositories()
    {
        // Load UI state from settings
        var settings = _settingsService.LoadSettings();
        IsRepoPaneCollapsed = settings.IsRepoPaneCollapsed;
        RepoPaneWidth = settings.RepoPaneWidth > 0 ? settings.RepoPaneWidth : 220;
        IsTerminalVisible = settings.IsTerminalVisible;
        TerminalHeight = settings.TerminalHeight > 0 ? settings.TerminalHeight : 220;

        // Load repositories via service
        var lastSelectedPath = await _repositoryService.LoadRepositoriesAsync();

        // Eagerly load worktrees for all repositories so they appear in the sidebar
        await LoadWorktreesForAllReposAsync();

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
    /// Handles discovery of new repositories from watched folders.
    /// </summary>
    private async void OnRepositoryDiscovered(object? sender, string repoPath)
    {
        await _dispatcherService.InvokeAsync(async () =>
        {
            if (_repositoryService.ContainsRepository(repoPath))
                return;

            if (await _gitService.IsValidRepositoryAsync(repoPath))
            {
                var repoInfo = await _gitService.GetRepositoryInfoAsync(repoPath);
                _repositoryService.AddRepository(repoInfo);

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

                if (await _gitService.IsValidRepositoryAsync(repoPath))
                {
                    var repoInfo = await _gitService.GetRepositoryInfoAsync(repoPath);
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
            StatusMessage = $"Loading {repository.Name}...";

            // Load branches BEFORE setting SelectedRepository to avoid UI flash
            // (UI binds to SelectedRepository.BranchCategories, so data should be ready)
            await LoadBranchesForRepoAsync(repository, forceReload: true);

            // Now set SelectedRepository - UI will see populated BranchCategories
            SelectedRepository = repository;

            // Mark as recently accessed (updates quick access sections)
            _repositoryService.MarkAsRecentlyAccessed(repository);

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
            repository.IsDetachedHead = info.IsDetachedHead;
            repository.DetachedHeadSha = info.DetachedHeadSha;

            // Load worktrees for sidebar display
            await LoadWorktreesForRepoAsync(repository);

            ApplyBranchFiltersForRepo(repository);

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
            DeleteRepository(repo);
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
        StatusMessage = $"Now watching {group.Name} for new repositories";
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
        StatusMessage = $"Stopped watching {group.Name}";
    }
}
