using System;
using System.Collections.ObjectModel;
using Leaf.Models;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial - Branch loading and helpers.
/// </summary>
public partial class MainViewModel
{
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

            // Get the default remote from config (or "origin" as fallback)
            var defaultRemoteName = await _gitService.GetConfigAsync(repo.Path, "leaf.defaultremote") ?? "origin";

            // Group remote branches by remote name
            var branchesByRemote = remoteBranches
                .GroupBy(b => b.RemoteName ?? "origin")
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            // Create remote groups for ALL remotes (including those without branches yet)
            var remoteGroups = remotes
                .Select(remote =>
                {
                    var remoteBranchList = branchesByRemote.GetValueOrDefault(remote.Name, []);
                    return new RemoteBranchGroup
                    {
                        Name = remote.Name,
                        Url = remote.Url,
                        RemoteType = RemoteBranchGroup.GetRemoteTypeFromUrl(remote.Url),
                        IsDefault = remote.Name.Equals(defaultRemoteName, StringComparison.OrdinalIgnoreCase),
                        Branches = new System.Collections.ObjectModel.ObservableCollection<BranchInfo>(
                            remoteBranchList.Select(b => new BranchInfo
                            {
                                // Strip the remote prefix from the display name
                                Name = b.Name.StartsWith($"{remote.Name}/") ? b.Name[($"{remote.Name}/".Length)..] : b.Name,
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
            GitGraphViewModel?.SetGitFlowContext(gitFlowConfig, remotes.Select(r => r.Name).ToList());
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

            // TAGS category
            var tags = await _gitService.GetTagsAsync(repo.Path);
            if (tags.Count > 0)
            {
                var tagsCategory = new BranchCategory
                {
                    Name = "TAGS",
                    Icon = "\uE8EC", // Tag icon
                    BranchCount = tags.Count,
                    IsExpanded = false // Start collapsed by default
                };
                foreach (var tag in tags.OrderByDescending(t => t.TaggedAt ?? DateTimeOffset.MinValue).ThenBy(t => t.Name))
                {
                    tagsCategory.Tags.Add(tag);
                }
                categories.Add(tagsCategory);
            }

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
            UpdateBranchFilterFlags(repo);

            if (SelectedRepository != null &&
                string.Equals(SelectedRepository.Path, repo.Path, StringComparison.OrdinalIgnoreCase))
            {
                ApplyBranchFiltersForRepo(repo);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load branches: {ex.Message}";
        }
    }

    private static string GetRemoteBranchShortName(string branchName, string remoteName)
    {
        var prefix = remoteName + "/";
        return branchName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? branchName[prefix.Length..]
            : branchName;
    }

    private async Task<(string RemoteName, string RemoteBranchName)> ResolveRemoteTargetAsync(BranchInfo branch)
    {
        string? remoteName = null;
        string? remoteBranchName = null;

        if (!string.IsNullOrWhiteSpace(branch.TrackingBranchName))
        {
            var tracking = branch.TrackingBranchName;
            var slashIndex = tracking.IndexOf('/');
            if (slashIndex > 0 && slashIndex < tracking.Length - 1)
            {
                remoteName = tracking[..slashIndex];
                remoteBranchName = tracking[(slashIndex + 1)..];
            }
        }

        if (SelectedRepository != null && string.IsNullOrWhiteSpace(remoteName))
        {
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path);
            remoteName = remotes.FirstOrDefault(r => r.Name == "origin")?.Name
                         ?? remotes.FirstOrDefault()?.Name
                         ?? "origin";
        }

        remoteBranchName ??= GetRemoteBranchShortName(branch.Name, remoteName ?? "origin");
        return (remoteName ?? "origin", remoteBranchName);
    }

    private async Task<bool> ConfirmBranchDeletionAsync(BranchInfo branch)
    {
        var scope = branch.IsRemote ? "remote" : "local";

        if (branch.IsCurrent)
        {
            var switchTarget = await GetBranchToSwitchToAsync(branch.Name);
            if (switchTarget == null)
            {
                await _dialogService.ShowInformationAsync(
                    "Cannot delete the only branch in the repository.",
                    "Delete Branch");
                return false;
            }

            return await _dialogService.ShowConfirmationAsync(
                $"Delete {scope} branch '{branch.Name}'?\n\nThis will first switch to '{switchTarget}'.\n\nThis cannot be undone.",
                "Delete Branch");
        }

        return await _dialogService.ShowConfirmationAsync(
            $"Delete {scope} branch '{branch.Name}'?\n\nThis cannot be undone.",
            "Delete Branch");
    }

    private async Task<string?> GetBranchToSwitchToAsync(string excludeBranch)
    {
        if (SelectedRepository == null)
            return null;

        var branches = await _gitService.GetBranchesAsync(SelectedRepository.Path);
        var localBranches = branches.Where(b => !b.IsRemote && !b.Name.Equals(excludeBranch, StringComparison.OrdinalIgnoreCase)).ToList();

        if (localBranches.Count == 0)
            return null;

        // Prefer GitFlow develop or main branch
        var config = await GetGitFlowConfigAsync();
        if (config != null)
        {
            var develop = localBranches.FirstOrDefault(b => b.Name.Equals(config.DevelopBranch, StringComparison.OrdinalIgnoreCase));
            if (develop != null)
                return develop.Name;

            var main = localBranches.FirstOrDefault(b => b.Name.Equals(config.MainBranch, StringComparison.OrdinalIgnoreCase));
            if (main != null)
                return main.Name;
        }

        // Fallback to common branch names
        var fallbackNames = new[] { "develop", "main", "master" };
        foreach (var name in fallbackNames)
        {
            var fallback = localBranches.FirstOrDefault(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (fallback != null)
                return fallback.Name;
        }

        // Last resort: first available branch
        return localBranches.First().Name;
    }

    private async Task<bool> ConfirmForceDeleteAsync(BranchInfo branch, string error)
    {
        return await _dialogService.ShowConfirmationAsync(
            $"Failed to delete branch '{branch.Name}'.\n\n{error}\n\nForce delete this branch?",
            "Force Delete Branch");
    }
}
