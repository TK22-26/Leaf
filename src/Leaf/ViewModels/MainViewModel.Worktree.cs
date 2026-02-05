using System;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services.Git.Operations;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial - Worktree operations.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Load worktrees for a repository (for sidebar display).
    /// </summary>
    public async Task LoadWorktreesForRepoAsync(RepositoryInfo repo, bool forceReload = false)
    {
        if (repo.WorktreesLoaded && !forceReload) return;

        try
        {
            var worktrees = await _gitService.GetWorktreesAsync(repo.Path);

            // Mark the current worktree
            var normalizedRepoPath = Path.GetFullPath(repo.Path);
            foreach (var wt in worktrees)
            {
                var normalizedWtPath = Path.GetFullPath(wt.Path);
                wt.IsCurrent = string.Equals(normalizedWtPath, normalizedRepoPath, StringComparison.OrdinalIgnoreCase);
            }

            // Update the collection
            repo.Worktrees.Clear();
            foreach (var wt in worktrees.OrderBy(w => w.IsMainWorktree ? 0 : 1).ThenBy(w => w.DisplayName))
            {
                repo.Worktrees.Add(wt);
            }

            repo.WorktreesLoaded = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load worktrees: {ex.Message}");
        }
    }

    /// <summary>
    /// Load worktrees for all repositories (called on startup for sidebar display).
    /// </summary>
    private async Task LoadWorktreesForAllReposAsync()
    {
        var allRepos = RepositoryGroups
            .SelectMany(g => g.Repositories)
            .ToList();

        foreach (var repo in allRepos)
        {
            await LoadWorktreesForRepoAsync(repo);
        }
    }

    /// <summary>
    /// Switch to a different worktree.
    /// </summary>
    [RelayCommand]
    public async Task SwitchToWorktreeAsync(WorktreeInfo worktree)
    {
        if (worktree == null || !worktree.Exists || worktree.IsCurrent)
            return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Switching to worktree {worktree.DisplayName}...";

            // Find existing repo entry for this worktree path, or add it
            var existingRepo = _repositoryService.FindRepository(worktree.Path);
            if (existingRepo != null)
            {
                await SelectRepositoryAsync(existingRepo);
                StatusMessage = $"Switched to {worktree.DisplayName}";
                return;
            }

            // Add worktree as a repository
            var repoInfo = await _gitService.GetRepositoryInfoAsync(worktree.Path);
            _repositoryService.AddRepository(repoInfo);
            await SelectRepositoryAsync(repoInfo);
            StatusMessage = $"Switched to {worktree.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Switch to worktree failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Create a worktree for an existing branch.
    /// </summary>
    [RelayCommand]
    public async Task CreateWorktreeForBranchAsync(BranchInfo branch)
    {
        if (SelectedRepository == null || branch == null || branch.IsRemote)
            return;

        try
        {
            IsBusy = true;
            var defaultPath = WorktreeOperations.GenerateDefaultWorktreePath(SelectedRepository.Path, branch.Name);

            StatusMessage = $"Creating worktree for {branch.Name}...";

            try
            {
                await _gitService.CreateWorktreeAsync(SelectedRepository.Path, defaultPath, branch.Name);
                StatusMessage = $"Created worktree at {defaultPath}";

                // Reload branches to show new worktree
                SelectedRepository.BranchesLoaded = false;
                await LoadBranchesForRepoAsync(SelectedRepository);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already checked out"))
            {
                // Branch is already checked out in another worktree - offer alternatives
                var result = await _dialogService.ShowMessageAsync(
                    $"Branch '{branch.Name}' is already checked out in another worktree.\n\n" +
                    "Would you like to create a detached worktree at this branch's tip instead?",
                    "Branch Already Checked Out",
                    MessageBoxButton.YesNo);

                if (result == MessageBoxResult.Yes)
                {
                    // Create detached worktree at the branch tip
                    var tipSha = branch.TipSha;
                    if (string.IsNullOrEmpty(tipSha))
                    {
                        StatusMessage = "Cannot determine branch tip SHA";
                        return;
                    }

                    await _gitService.CreateWorktreeDetachedAsync(SelectedRepository.Path, defaultPath, tipSha);
                    StatusMessage = $"Created detached worktree at {defaultPath}";

                    SelectedRepository.BranchesLoaded = false;
                    await LoadBranchesForRepoAsync(SelectedRepository);
                }
                else
                {
                    StatusMessage = "Worktree creation cancelled";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Create worktree failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Create a worktree with a new branch.
    /// </summary>
    public async Task CreateWorktreeWithNewBranchAsync(string newBranchName, string? startPoint = null)
    {
        if (SelectedRepository == null || string.IsNullOrWhiteSpace(newBranchName))
            return;

        try
        {
            IsBusy = true;
            var defaultPath = WorktreeOperations.GenerateDefaultWorktreePath(SelectedRepository.Path, newBranchName);

            StatusMessage = $"Creating worktree with new branch {newBranchName}...";
            await _gitService.CreateWorktreeWithNewBranchAsync(SelectedRepository.Path, defaultPath, newBranchName, startPoint);
            StatusMessage = $"Created worktree at {defaultPath}";

            // Reload branches to show new worktree
            SelectedRepository.BranchesLoaded = false;
            await LoadBranchesForRepoAsync(SelectedRepository);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Create worktree failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Create a worktree in detached HEAD state at a specific commit.
    /// </summary>
    [RelayCommand]
    public async Task CreateWorktreeDetachedAsync(CommitInfo commit)
    {
        if (SelectedRepository == null || commit == null)
            return;

        try
        {
            IsBusy = true;
            var shortSha = commit.Sha.Length >= 7 ? commit.Sha[..7] : commit.Sha;
            var defaultPath = WorktreeOperations.GenerateDefaultWorktreePath(SelectedRepository.Path, shortSha);

            StatusMessage = $"Creating detached worktree at {shortSha}...";
            await _gitService.CreateWorktreeDetachedAsync(SelectedRepository.Path, defaultPath, commit.Sha);
            StatusMessage = $"Created detached worktree at {defaultPath}";

            // Reload branches to show new worktree
            SelectedRepository.BranchesLoaded = false;
            await LoadBranchesForRepoAsync(SelectedRepository);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Create worktree failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Remove a worktree.
    /// </summary>
    [RelayCommand]
    public async Task RemoveWorktreeAsync(WorktreeInfo worktree)
    {
        if (SelectedRepository == null || worktree == null || worktree.IsMainWorktree || worktree.IsCurrent)
            return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            $"Remove worktree '{worktree.DisplayName}'?\n\nThis will delete the worktree directory at:\n{worktree.Path}",
            "Remove Worktree");

        if (!confirmed)
            return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Removing worktree {worktree.DisplayName}...";

            try
            {
                await _gitService.RemoveWorktreeAsync(SelectedRepository.Path, worktree.Path, force: false);
            }
            catch (InvalidOperationException)
            {
                // If normal removal fails, offer force removal
                var forceConfirmed = await _dialogService.ShowConfirmationAsync(
                    $"Worktree has uncommitted changes or is locked.\n\nForce remove anyway?",
                    "Force Remove Worktree");

                if (forceConfirmed)
                {
                    await _gitService.RemoveWorktreeAsync(SelectedRepository.Path, worktree.Path, force: true);
                }
                else
                {
                    StatusMessage = "Remove worktree cancelled";
                    return;
                }
            }

            StatusMessage = $"Removed worktree {worktree.DisplayName}";

            // Also remove from repo list if it was added
            var repoInList = _repositoryService.FindRepository(worktree.Path);
            if (repoInList != null)
            {
                _repositoryService.RemoveRepository(repoInList);
            }

            // Reload branches to update worktree list
            SelectedRepository.BranchesLoaded = false;
            await LoadBranchesForRepoAsync(SelectedRepository);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Remove worktree failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Lock a worktree.
    /// </summary>
    [RelayCommand]
    public async Task LockWorktreeAsync(WorktreeInfo worktree)
    {
        if (SelectedRepository == null || worktree == null || worktree.IsMainWorktree || worktree.IsLocked)
            return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Locking worktree {worktree.DisplayName}...";

            await _gitService.LockWorktreeAsync(SelectedRepository.Path, worktree.Path);
            worktree.IsLocked = true;

            StatusMessage = $"Locked worktree {worktree.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lock worktree failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Unlock a worktree.
    /// </summary>
    [RelayCommand]
    public async Task UnlockWorktreeAsync(WorktreeInfo worktree)
    {
        if (SelectedRepository == null || worktree == null || !worktree.IsLocked)
            return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Unlocking worktree {worktree.DisplayName}...";

            await _gitService.UnlockWorktreeAsync(SelectedRepository.Path, worktree.Path);
            worktree.IsLocked = false;

            StatusMessage = $"Unlocked worktree {worktree.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unlock worktree failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Prune stale worktree references.
    /// </summary>
    [RelayCommand]
    public async Task PruneWorktreesAsync()
    {
        if (SelectedRepository == null)
            return;

        try
        {
            IsBusy = true;
            StatusMessage = "Pruning stale worktree references...";

            await _gitService.PruneWorktreesAsync(SelectedRepository.Path);

            StatusMessage = "Pruned stale worktree references";

            // Reload branches to update worktree list
            SelectedRepository.BranchesLoaded = false;
            await LoadBranchesForRepoAsync(SelectedRepository);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Prune worktrees failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Show create worktree dialog (creates worktree with new branch).
    /// </summary>
    [RelayCommand]
    public async Task ShowCreateWorktreeDialogAsync()
    {
        if (SelectedRepository == null)
            return;

        var branchName = await _dialogService.ShowInputAsync(
            "Enter name for the new branch:",
            "Create Worktree",
            "new-feature");

        if (!string.IsNullOrWhiteSpace(branchName))
        {
            await CreateWorktreeWithNewBranchAsync(branchName.Trim());
        }
    }
}
