using System;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Views;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial - Branch merge and label operations.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Merge a branch into the current branch.
    /// Shows a dialog to select merge type (normal, squash, or fast-forward).
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

        // Show merge options dialog
        var dialogViewModel = new MergeDialogViewModel
        {
            SourceBranch = branch.Name,
            TargetBranch = SelectedRepository.CurrentBranch ?? "current branch",
            CommitMessage = $"Squash merge '{branch.Name}' into {SelectedRepository.CurrentBranch}"
        };

        var dialog = new MergeDialog
        {
            DataContext = dialogViewModel,
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        // Execute the appropriate merge based on selected type
        try
        {
            IsBusy = true;

            MergeResult result;
            switch (dialogViewModel.SelectedMergeType)
            {
                case MergeType.Squash:
                    StatusMessage = $"Squash merging {branch.Name} into {SelectedRepository.CurrentBranch}...";
                    result = await _gitService.SquashMergeAsync(SelectedRepository.Path, branch.Name);
                    break;

                case MergeType.FastForwardOnly:
                    StatusMessage = $"Fast-forwarding to {branch.Name}...";
                    result = await _gitService.FastForwardAsync(SelectedRepository.Path, branch.Name);
                    break;

                default: // MergeType.Normal
                    StatusMessage = $"Merging {branch.Name} into {SelectedRepository.CurrentBranch}...";
                    result = await ExecuteNormalMergeAsync(branch.Name);
                    break;
            }

            await HandleMergeResultAsync(result, branch.Name, dialogViewModel.SelectedMergeType);
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

    private async Task<MergeResult> ExecuteNormalMergeAsync(string branchName)
    {
        var result = await _gitService.MergeBranchAsync(SelectedRepository!.Path, branchName);
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
                return new MergeResult { Success = false, ErrorMessage = "Merge cancelled" };
            }

            // Retry with flag
            StatusMessage = $"Merging {branchName} (allowing unrelated histories)...";
            result = await _gitService.MergeBranchAsync(SelectedRepository.Path, branchName, allowUnrelatedHistories: true);
            System.Diagnostics.Debug.WriteLine($"[MainVM] Retry merge result: Success={result.Success}, Conflicts={result.HasConflicts}");
        }

        return result;
    }

    private async Task HandleMergeResultAsync(MergeResult result, string branchName, MergeType mergeType)
    {
        if (result.Success)
        {
            var typeDesc = mergeType switch
            {
                MergeType.Squash => "Squash merged",
                MergeType.FastForwardOnly => "Fast-forwarded to",
                _ => "Successfully merged"
            };
            StatusMessage = $"{typeDesc} {branchName}";

            // Refresh git graph
            if (GitGraphViewModel != null)
            {
                await GitGraphViewModel.LoadRepositoryAsync(SelectedRepository!.Path);
            }
        }
        else if (result.HasConflicts)
        {
            StatusMessage = "Merge has conflicts - resolve to complete";

            // Refresh repo info to update merge banner and conflicts immediately
            var info = await _gitService.GetRepositoryInfoAsync(SelectedRepository!.Path);
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
