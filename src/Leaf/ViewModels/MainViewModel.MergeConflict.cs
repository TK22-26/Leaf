using System;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial - Merge conflict resolution operations.
/// </summary>
public partial class MainViewModel
{
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

            // Check if we're in an orphaned conflict state (conflicts without MERGE_HEAD)
            var isOrphaned = await _gitService.IsOrphanedConflictStateAsync(SelectedRepository.Path);

            if (isOrphaned)
            {
                // Show dialog to let user choose how to recover
                StatusMessage = "Detected orphaned conflict state...";

                var result = await _dialogService.ShowMessageAsync(
                    "The repository has conflicts but no merge is in progress.\n" +
                    "This can happen after a failed checkout or other operation.\n\n" +
                    "Choose how to recover:\n\n" +
                    "YES - Reset index only (keeps your working directory changes)\n" +
                    "NO - Reset and restore (discards ALL uncommitted changes)\n" +
                    "CANCEL - Do nothing",
                    "Recovery Required",
                    MessageBoxButton.YesNoCancel);

                if (result == MessageBoxResult.Cancel)
                {
                    StatusMessage = "Recovery cancelled";
                    return;
                }

                var discardChanges = result == MessageBoxResult.No;

                if (discardChanges)
                {
                    // Extra confirmation for destructive option
                    var confirmed = await _dialogService.ShowConfirmationAsync(
                        "This will discard ALL uncommitted changes in your working directory.\n\n" +
                        "This cannot be undone. Are you sure?",
                        "Confirm Discard Changes");

                    if (!confirmed)
                    {
                        StatusMessage = "Recovery cancelled";
                        return;
                    }
                }

                StatusMessage = discardChanges
                    ? "Resetting index and restoring files..."
                    : "Resetting index...";

                await _gitService.ResetOrphanedConflictsAsync(SelectedRepository.Path, discardChanges);

                StatusMessage = discardChanges
                    ? "Index reset and files restored"
                    : "Index reset (working directory preserved)";
            }
            else
            {
                // Normal merge abort
                StatusMessage = "Aborting merge...";
                await _gitService.AbortMergeAsync(SelectedRepository.Path);
                StatusMessage = "Merge aborted";
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Abort failed: {ex.Message}";
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

            var conflictViewModel = new ConflictResolutionViewModel(_gitService, _clipboardService, _dispatcherService, SelectedRepository.Path)
            {
                IsCompactFileList = _settingsService.LoadSettings().CompactFileList
            };
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

        // Force property change notification to update UI bindings
        OnPropertyChanged(nameof(MergeConflictResolutionViewModel));
    }

    private async void OnMergeConflictResolutionCompleted(object? sender, bool success)
    {
        StatusMessage = success ? "Merge completed successfully" : "Merge aborted";
        await RefreshAsync();
    }
}
