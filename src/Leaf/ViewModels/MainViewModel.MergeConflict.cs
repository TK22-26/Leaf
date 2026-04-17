using System;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;

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
            DataContext = MergeConflictResolutionViewModel
        };

        await _dialogService.ShowDialogAsync(conflictWindow);
    }

    /// <summary>
    /// Open the first unresolved conflict in the configured external
    /// merge tool. No-op (with a status message) if no tool is selected.
    /// </summary>
    [RelayCommand]
    public async Task OpenInMergeToolAsync()
    {
        if (SelectedRepository == null) return;

        var mergeTool = await _externalToolConfig.GetCurrentToolAsync(
            SelectedRepository.Path, ExternalToolKind.Merge, CurrentRepositoryToken);
        if (mergeTool == null)
        {
            StatusMessage = "No external merge tool configured. See Settings → External Tools.";
            return;
        }

        try
        {
            await BeginBusyAsync($"Opening {mergeTool.DisplayName} for merge...");

            var conflicts = await _gitService.GetConflictsAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
            var firstConflict = conflicts.FirstOrDefault();

            if (firstConflict != null)
            {
                var staged = await _gitService.OpenConflictInMergeToolAsync(
                    SelectedRepository.Path,
                    firstConflict.FilePath,
                    (b, l, r, m, ct) => _externalToolLauncher.LaunchMergeAsync(mergeTool, b, l, r, m, ct),
                    cancellationToken: CurrentRepositoryToken);

                await RefreshAsync();

                var remaining = await _gitService.GetConflictsAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
                if (!staged)
                {
                    StatusMessage = $"{mergeTool.DisplayName} did not produce a staged result.";
                }
                else if (remaining.Count == 0)
                {
                    StatusMessage = $"All conflicts resolved in {mergeTool.DisplayName}.";
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
            await ReportOperationFailureAsync($"Open {mergeTool.DisplayName}", ex);
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
            await BeginBusyAsync("Aborting...");
            Log.Info("Merge", $"AbortMerge: repo={SelectedRepository.Name}");

            // Check if we're in an orphaned conflict state (conflicts without MERGE_HEAD)
            var isOrphaned = await _gitService.IsOrphanedConflictStateAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
            Log.Info("Merge", $"AbortMerge: isOrphaned={isOrphaned}");

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

                await _gitService.ResetOrphanedConflictsAsync(SelectedRepository.Path, discardChanges, cancellationToken: CurrentRepositoryToken);

                // Clean up stored merge conflict file
                try
                {
                    await _gitService.ClearStoredMergeConflictFilesAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
                }
                catch (Exception clearEx) when (clearEx is IOException or UnauthorizedAccessException)
                {
                    // Stored conflict file may already be gone or locked —
                    // the reset itself already succeeded, so this is
                    // cosmetic.
                    Log.Info("Merge", $"Clear stored merge conflicts failed: {clearEx.Message}");
                }

                StatusMessage = discardChanges
                    ? "Index reset and files restored"
                    : "Index reset (working directory preserved)";
            }
            else
            {
                // Route to correct abort command based on operation type
                var opType = SelectedRepository.OperationType;
                Log.Info("Merge", $"AbortMerge: running abort for {opType}");

                switch (opType)
                {
                    case Models.GitOperationType.CherryPick:
                        StatusMessage = "Aborting cherry-pick...";
                        await _gitService.AbortCherryPickAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
                        StatusMessage = "Cherry-pick aborted";
                        break;

                    case Models.GitOperationType.Revert:
                        StatusMessage = "Aborting revert...";
                        await _gitService.AbortRevertAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
                        StatusMessage = "Revert aborted";
                        break;

                    case Models.GitOperationType.Rebase:
                        StatusMessage = "Aborting rebase...";
                        await _gitService.AbortRebaseAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
                        StatusMessage = "Rebase aborted";
                        break;

                    default:
                        StatusMessage = "Aborting merge...";
                        await _gitService.AbortMergeAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
                        StatusMessage = "Merge aborted";
                        break;
                }

                Log.Info("Merge", "AbortMerge: completed");
            }

            // Clean up the stored merge conflict file immediately after abort
            try
            {
                await _gitService.ClearStoredMergeConflictFilesAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
            }
            catch (Exception clearEx)
            {
                Log.Warn("Merge", $"AbortMerge: failed to clear stored conflicts: {clearEx.Message}");
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Merge", "AbortMerge failed", ex);
            await ReportOperationFailureAsync("Abort", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task OpenConflictInMergeToolAsync(ConflictInfo? conflict)
    {
        if (SelectedRepository == null || conflict == null) return;

        var mergeTool = await _externalToolConfig.GetCurrentToolAsync(
            SelectedRepository.Path, ExternalToolKind.Merge, CurrentRepositoryToken);
        if (mergeTool == null)
        {
            StatusMessage = "No external merge tool configured. See Settings → External Tools.";
            return;
        }

        try
        {
            await BeginBusyAsync($"Opening {mergeTool.DisplayName} for merge...");

            await _gitService.OpenConflictInMergeToolAsync(
                SelectedRepository.Path,
                conflict.FilePath,
                (b, l, r, m, ct) => _externalToolLauncher.LaunchMergeAsync(mergeTool, b, l, r, m, ct),
                cancellationToken: CurrentRepositoryToken);

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync($"Open {mergeTool.DisplayName}", ex);
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
            DataContext = MergeConflictResolutionViewModel
        };

        await _dialogService.ShowDialogAsync(conflictWindow);
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
        Log.Info("Merge", $"RefreshMergeConflictResolution: merge={SelectedRepository.IsMergeInProgress} conflictCount={SelectedRepository.ConflictCount}");
        if (!hasMergeConflicts)
        {
            if (MergeConflictResolutionViewModel != null)
            {
                MergeConflictResolutionViewModel.MergeCompleted -= OnMergeConflictResolutionCompleted;
            }

            MergeConflictResolutionViewModel = null;
            _mergeConflictRepoPath = null;
            _gitService.ClearStoredMergeConflictFilesAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken)
                .FireAndForget(nameof(_gitService.ClearStoredMergeConflictFilesAsync), isUserAction: false);
            return;
        }

        if (string.IsNullOrEmpty(SelectedRepository.MergingBranch))
        {
            var info = await _gitService.GetRepositoryInfoFastAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
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
                IsCompactFileList = _settingsService.LoadSettings().CompactFileList,
                GetSessionToken = () => CurrentRepositoryToken
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
        try
        {
            Log.Info("Merge", $"OnMergeConflictResolutionCompleted: success={success}");
            StatusMessage = success ? "Merge completed successfully" : "Merge aborted";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            AsyncErrorHandler.Handle(ex, nameof(OnMergeConflictResolutionCompleted), isUserAction: true);
        }
    }
}
