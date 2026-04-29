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

        var conflictWindow = new Views.Merge.MergeEditorView
        {
            DataContext = MergeConflictResolutionViewModel
        };
        // C5: blame-peek sha link → jump the commit graph to that commit.
        // The editor window fires CommitJumpRequested; we route through the
        // same OnNavigateToCommitRequested handler the CommitDetail panel
        // uses for its own commit-hash hyperlinks.
        conflictWindow.CommitJumpRequested += OnMergeEditorCommitJumpRequested;
        try
        {
            await _dialogService.ShowDialogAsync(conflictWindow);
        }
        finally
        {
            conflictWindow.CommitJumpRequested -= OnMergeEditorCommitJumpRequested;
        }
    }

    private void OnMergeEditorCommitJumpRequested(object? sender, string sha)
    {
        if (string.IsNullOrEmpty(sha)) return;
        GitGraphViewModel?.SelectCommitBySha(sha);
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
            // No tool configured. Drop the user directly into the External
            // Tools settings page — earlier the button would be disabled
            // (and so wouldn't even surface its tooltip), leaving users
            // with no clue why nothing was happening. The "configure on
            // demand" pattern matches GitHub Desktop and Sourcetree, and
            // the settings page already offers auto-detection so most
            // users land back here with a working tool one click later.
            //
            // ExternalToolsSettings persists tool selection via its Apply
            // button (writes to `git config --global` through
            // IExternalToolConfigService.SetSelectedToolAsync), so a
            // user who picks a tool + clicks Apply + closes the dialog
            // gets picked up by the re-fetch below. If they close without
            // Apply, no tool is saved — explicit user choice, we honour
            // it by returning early.
            StatusMessage = "Configure an external merge tool to resolve conflicts in it.";
            await OpenSettingsAsync("ExternalTools");
            // OpenSettingsAsync calls RefreshExternalMergeToolAvailabilityAsync
            // on close, which updates HasExternalMergeTool — no need to
            // pre-set it false here, that would just flicker any consumer
            // bound to the property during the window the dialog is open.
            mergeTool = await _externalToolConfig.GetCurrentToolAsync(
                SelectedRepository.Path, ExternalToolKind.Merge, CurrentRepositoryToken);
            if (mergeTool == null)
            {
                StatusMessage = "No external merge tool configured.";
                return;
            }
        }

        try
        {
            await BeginBusyAsync($"Opening {mergeTool.DisplayName} for merge...");

            var staged = await _gitService.OpenConflictInMergeToolAsync(
                SelectedRepository.Path,
                conflict.FilePath,
                (b, l, r, m, ct) => _externalToolLauncher.LaunchMergeAsync(mergeTool, b, l, r, m, ct),
                cancellationToken: CurrentRepositoryToken);

            await RefreshAsync();

            // Mirror the status feedback users get from per-conflict
            // resolution in Leaf's own merge view — without it a failed
            // external merge silently returns as if nothing happened.
            StatusMessage = staged
                ? $"{conflict.FilePath} resolved in {mergeTool.DisplayName}."
                : $"{mergeTool.DisplayName} did not produce a staged result for {conflict.FilePath}.";
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
    /// Re-check whether an external merge tool is configured for the
    /// currently selected repository. Called on repo switch and after
    /// the Settings dialog closes so the "Resolve in External Tool"
    /// button's enabled state stays in sync with git config.
    /// </summary>
    public async Task RefreshExternalMergeToolAvailabilityAsync()
    {
        if (SelectedRepository == null)
        {
            HasExternalMergeTool = false;
            return;
        }

        try
        {
            var tool = await _externalToolConfig.GetCurrentToolAsync(
                SelectedRepository.Path, ExternalToolKind.Merge, CurrentRepositoryToken);
            HasExternalMergeTool = tool != null;
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                or OperationCanceledException)
        {
            Log.Info("ExternalMerge", $"Availability probe failed: {ex.Message}");
            HasExternalMergeTool = false;
        }
    }

    [RelayCommand]
    public async Task OpenConflictInLeafAsync(ConflictInfo? conflict)
    {
        if (SelectedRepository == null || conflict == null) return;

        // The path between double-click and the merge editor appearing runs
        // RefreshMergeConflictResolutionAsync (git calls), constructs the
        // editor view, expands its (large) template, and waits for the
        // dialog service to actually show the window. On a cold open this
        // can be one-to-several seconds during which the main window
        // appears frozen. Flip IsBusy so the indeterminate progress bar
        // ticks immediately, then clear it just before handing off to
        // ShowDialogAsync — once the modal is up the editor's own
        // IsLoading state owns the user's attention and a still-running
        // main-window progress bar would be misleading.
        var fileName = System.IO.Path.GetFileName(conflict.FilePath);
        await BeginBusyAsync($"Opening {fileName} in merge editor…");
        try
        {
            await RefreshMergeConflictResolutionAsync();
            if (MergeConflictResolutionViewModel == null) return;

            MergeConflictResolutionViewModel.SelectedConflict = conflict;

            var conflictWindow = new Views.Merge.MergeEditorView
            {
                DataContext = MergeConflictResolutionViewModel
            };
            conflictWindow.CommitJumpRequested += OnMergeEditorCommitJumpRequested;
            try
            {
                IsBusy = false;
                StatusMessage = $"Resolving conflicts in {fileName}";
                await _dialogService.ShowDialogAsync(conflictWindow);
            }
            finally
            {
                conflictWindow.CommitJumpRequested -= OnMergeEditorCommitJumpRequested;
            }
        }
        finally
        {
            IsBusy = false;
        }
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

            var conflictViewModel = new ViewModels.Merge.MergeEditorViewModel(
                _gitService, _clipboardService, _mergeEngine,
                _wordDiffService, _aiMergeAssistant, _imageMergeService, SelectedRepository.Path)
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
