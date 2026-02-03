using System;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial - Branch merge and label operations.
/// </summary>
public partial class MainViewModel
{
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
                var confirmed = await _dialogService.ShowConfirmationAsync(
                    "The branches have unrelated histories (no common ancestor).\n\n" +
                    "This can happen when branches were created independently or the repository was recreated.\n\n" +
                    "Do you want to merge anyway? This will combine the histories and may result in merge conflicts.",
                    "Unrelated Histories");

                if (!confirmed)
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
    public async Task DeleteBranchLabelAsync(BranchLabel label)
    {
        if (label == null)
            return;

        var branch = new BranchInfo
        {
            Name = label.Name,
            IsRemote = label.IsRemote && !label.IsLocal,
            RemoteName = label.RemoteName,
            IsCurrent = label.IsCurrent
        };

        await DeleteBranchAsync(branch);
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
}
