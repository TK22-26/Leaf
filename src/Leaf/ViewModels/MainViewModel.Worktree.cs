using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;
using Leaf.Services.Git.Operations;
using Leaf.Utils;

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

        var sw = Log.StartTimer();
        try
        {
            // No session token: this method is callable for any repo
            // (LoadWorktreesForAllReposAsync iterates all) — a repo-switch
            // cancellation would drop worktree data for unrelated repos.
            var worktrees = await _gitService.GetWorktreesAsync(repo.Path).ConfigureAwait(false);

            // Mark the current worktree
            var normalizedRepoPath = Path.GetFullPath(repo.Path);
            foreach (var wt in worktrees)
            {
                var normalizedWtPath = Path.GetFullPath(wt.Path);
                wt.IsCurrent = string.Equals(normalizedWtPath, normalizedRepoPath, StringComparison.OrdinalIgnoreCase);
            }

            var orderedWorktrees = worktrees
                .OrderBy(w => w.IsMainWorktree ? 0 : 1)
                .ThenBy(w => w.DisplayName)
                .ToList();

            var sidebarWorktrees = orderedWorktrees.Count > 1
                ? orderedWorktrees
                : [];

            await _dispatcherService.InvokeAsync(() =>
            {
                if (sidebarWorktrees.Count > 0 || repo.Worktrees.Count > 0)
                {
                    repo.Worktrees = new ObservableCollection<WorktreeInfo>(sidebarWorktrees);
                }

                repo.WorktreesLoaded = true;
            });
        }
        catch (Exception ex)
        {
            Log.Error("Worktree", "Failed to load worktrees", ex);
        }
        finally
        {
            Log.Perf("Worktree", $"LoadWorktreesForRepoAsync for {repo.Name}", sw.ElapsedMilliseconds);
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
            await LoadWorktreesForRepoAsync(repo).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Switch to a different worktree.
    /// </summary>
    [RelayCommand]
    public async Task SwitchToWorktreeAsync(WorktreeInfo worktree)
    {
        if (worktree == null || !worktree.Exists)
            return;

        // Already viewing this exact worktree path?
        if (SelectedRepository != null &&
            string.Equals(
                Path.GetFullPath(SelectedRepository.Path),
                Path.GetFullPath(worktree.Path),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await BeginBusyAsync($"Switching to worktree {worktree.DisplayName}...");

            // Capture category expanded states from the current repo before switching
            Dictionary<string, bool>? previousCategoryStates = null;
            if (SelectedRepository?.BranchCategories.Count > 0)
            {
                previousCategoryStates = SelectedRepository.BranchCategories
                    .ToDictionary(c => c.Name, c => c.IsExpanded);
            }

            // Find existing repo entry for this worktree path, or add it
            var existingRepo = _repositoryService.FindRepository(worktree.Path);
            RepositoryInfo targetRepo;
            if (existingRepo != null)
            {
                targetRepo = existingRepo;
            }
            else
            {
                // Add worktree as a repository. No session token: we're about
                // to SelectRepositoryAsync this target, which creates its own
                // session. Using the current-repo token would cancel this
                // probe the moment SelectRepositoryAsync rotates the session.
                targetRepo = await _gitService.GetRepositoryInfoFastAsync(worktree.Path);
                _repositoryService.AddRepository(targetRepo);
            }

            // Transfer category states to the target repo so LoadBranchesForRepoAsync can preserve them
            if (previousCategoryStates != null && targetRepo.BranchCategories.Count == 0)
            {
                foreach (var kvp in previousCategoryStates)
                {
                    targetRepo.BranchCategories.Add(new BranchCategory { Name = kvp.Key, IsExpanded = kvp.Value });
                }
            }

            await SelectRepositoryAsync(targetRepo);

            // Update IsCurrent flags on all worktree collections so the checkmark moves
            UpdateWorktreeCurrentFlags(worktree.Path);

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
    /// Update IsCurrent on all worktree items across all repos to reflect
    /// which worktree path is now being viewed.
    /// </summary>
    private void UpdateWorktreeCurrentFlags(string selectedWorktreePath)
    {
        var normalizedSelected = Path.GetFullPath(selectedWorktreePath);

        foreach (var rootItem in RepositoryRootItems)
        {
            IEnumerable<RepositoryInfo> repos = rootItem switch
            {
                RepositorySection section => section.Items.Select(qi => qi.Repository),
                RepositoryGroup group => group.Repositories,
                _ => []
            };

            foreach (var repo in repos)
            {
                foreach (var wt in repo.Worktrees)
                {
                    wt.IsCurrent = string.Equals(
                        Path.GetFullPath(wt.Path),
                        normalizedSelected,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
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
            var defaultPath = WorktreeOperations.GenerateDefaultWorktreePath(SelectedRepository.Path, branch.Name);
            await BeginBusyAsync($"Creating worktree for {branch.Name}...");

            try
            {
                // If the branch is currently checked out, detach HEAD first so the branch is free
                if (branch.IsCurrent && !string.IsNullOrEmpty(branch.TipSha))
                {
                    await _gitService.CheckoutCommitAsync(SelectedRepository.Path, branch.TipSha, cancellationToken: CurrentRepositoryToken);
                }

                await _gitService.CreateWorktreeAsync(SelectedRepository.Path, defaultPath, branch.Name, cancellationToken: CurrentRepositoryToken);
                StatusMessage = $"Created worktree at {defaultPath}";

                // Reload branches to show new worktree
                SelectedRepository.BranchesLoaded = false;
                await LoadBranchesForRepoAsync(SelectedRepository);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already checked out") || ex.Message.Contains("already used by worktree"))
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

                    await _gitService.CreateWorktreeDetachedAsync(SelectedRepository.Path, defaultPath, tipSha, cancellationToken: CurrentRepositoryToken);
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
    /// Build a user-facing preview of where a new worktree would be placed
    /// for the given branch name. Reuses the same sanitization rule as
    /// <see cref="WorktreeOperations.GenerateDefaultWorktreePath"/> so the
    /// preview can't drift from what actually lands on disk. Returns the
    /// fallback string when no repo is selected, when the name is invalid,
    /// or when the name is empty.
    /// </summary>
    public string GetWorktreePathPreview(string branchName)
    {
        const string placeholder = "Path: ...";
        if (SelectedRepository == null) return placeholder;

        var trimmed = branchName?.Trim() ?? string.Empty;
        if (!BranchNameValidator.IsValid(trimmed)) return placeholder;

        var repoName = System.IO.Path.GetFileName(SelectedRepository.Path.TrimEnd(
            System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        var safeName = WorktreeOperations.SanitizeBranchNameForPath(trimmed);
        return $"Path: ../{repoName}-{safeName}";
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
            var defaultPath = WorktreeOperations.GenerateDefaultWorktreePath(SelectedRepository.Path, newBranchName);
            await BeginBusyAsync($"Creating worktree with new branch {newBranchName}...");
            await _gitService.CreateWorktreeWithNewBranchAsync(SelectedRepository.Path, defaultPath, newBranchName, startPoint, cancellationToken: CurrentRepositoryToken);
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
            var shortSha = commit.Sha.Length >= 7 ? commit.Sha[..7] : commit.Sha;
            var defaultPath = WorktreeOperations.GenerateDefaultWorktreePath(SelectedRepository.Path, shortSha);
            await BeginBusyAsync($"Creating detached worktree at {shortSha}...");
            await _gitService.CreateWorktreeDetachedAsync(SelectedRepository.Path, defaultPath, commit.Sha, cancellationToken: CurrentRepositoryToken);
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

        // If worktree is locked, go directly to force confirmation
        if (worktree.IsLocked)
        {
            var forceConfirmed = await _dialogService.ShowConfirmationAsync(
                $"Worktree '{worktree.DisplayName}' is locked.\n\nForce remove anyway?\n\nThis will delete the worktree directory at:\n{worktree.Path}",
                "Force Remove Worktree");

            if (!forceConfirmed)
                return;

            try
            {
                await BeginBusyAsync($"Force removing worktree {worktree.DisplayName}...");
                await _gitService.RemoveWorktreeAsync(SelectedRepository.Path, worktree.Path, force: true, cancellationToken: CurrentRepositoryToken);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Remove worktree failed: {ex.Message}";
                return;
            }
        }
        else
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                $"Remove worktree '{worktree.DisplayName}'?\n\nThis will delete the worktree directory at:\n{worktree.Path}",
                "Remove Worktree");

            if (!confirmed)
                return;

            try
            {
                await BeginBusyAsync($"Removing worktree {worktree.DisplayName}...");

                try
                {
                    await _gitService.RemoveWorktreeAsync(SelectedRepository.Path, worktree.Path, force: false, cancellationToken: CurrentRepositoryToken);
                }
                catch (InvalidOperationException)
                {
                    // If normal removal fails (uncommitted changes), offer force removal
                    var forceConfirmed = await _dialogService.ShowConfirmationAsync(
                        $"Worktree has uncommitted changes.\n\nForce remove anyway?",
                        "Force Remove Worktree");

                    if (forceConfirmed)
                    {
                        await _gitService.RemoveWorktreeAsync(SelectedRepository.Path, worktree.Path, force: true, cancellationToken: CurrentRepositoryToken);
                    }
                    else
                    {
                        StatusMessage = "Remove worktree cancelled";
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Remove worktree failed: {ex.Message}";
                return;
            }
        }

        // Cleanup after successful removal
        try
        {
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
            await BeginBusyAsync($"Locking worktree {worktree.DisplayName}...");

            await _gitService.LockWorktreeAsync(SelectedRepository.Path, worktree.Path, cancellationToken: CurrentRepositoryToken);
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
            await BeginBusyAsync($"Unlocking worktree {worktree.DisplayName}...");

            await _gitService.UnlockWorktreeAsync(SelectedRepository.Path, worktree.Path, cancellationToken: CurrentRepositoryToken);
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
            await BeginBusyAsync("Pruning stale worktree references...");

            await _gitService.PruneWorktreesAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);

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
