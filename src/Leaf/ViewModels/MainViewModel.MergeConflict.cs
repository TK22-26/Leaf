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
        // Auto-close on merge completion or external abort. Both cases come
        // through MergeCompleted: in-editor Abort/CompleteMerge button, or
        // RefreshMergeConflictResolutionAsync calling
        // NotifyMergeAbortedExternally when the file watcher detects
        // MERGE_HEAD vanished. ShowDialogAsync just calls ShowDialog(), so
        // without this the window only closes when the user clicks X.
        EventHandler<bool> closeOnComplete = (_, _) =>
        {
            if (conflictWindow.IsLoaded) conflictWindow.Close();
        };
        MergeConflictResolutionViewModel.MergeCompleted += closeOnComplete;
        try
        {
            await _dialogService.ShowDialogAsync(conflictWindow);
        }
        finally
        {
            conflictWindow.CommitJumpRequested -= OnMergeEditorCommitJumpRequested;
            // VM may already be null if RefreshMergeConflictResolutionAsync
            // tore it down (external abort path); guard the unsubscribe.
            if (conflictWindow.DataContext is ViewModels.Merge.MergeEditorViewModel vm)
            {
                vm.MergeCompleted -= closeOnComplete;
            }
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
                    NotifyInfo("Recovery cancelled", "Repository state unchanged.");
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
                        NotifyInfo("Recovery cancelled", "Repository state unchanged.");
                        return;
                    }
                }

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

                NotifySuccess("Index reset", discardChanges
                    ? "Index reset and files restored."
                    : "Index reset (working directory preserved).");
            }
            else
            {
                // Route to correct abort command based on operation type
                var opType = SelectedRepository.OperationType;
                Log.Info("Merge", $"AbortMerge: running abort for {opType}");

                switch (opType)
                {
                    case Models.GitOperationType.CherryPick:
                        await _gitService.AbortCherryPickAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
                        NotifySuccess("Cherry-pick aborted", "Working tree restored to pre-cherry-pick state.");
                        break;

                    case Models.GitOperationType.Revert:
                        await _gitService.AbortRevertAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
                        NotifySuccess("Revert aborted", "Working tree restored to pre-revert state.");
                        break;

                    case Models.GitOperationType.Rebase:
                        await _gitService.AbortRebaseAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
                        NotifySuccess("Rebase aborted", "Working tree restored to pre-rebase state.");
                        break;

                    case Models.GitOperationType.Am:
                        await _gitService.AbortAmAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
                        NotifySuccess("Patch apply aborted", "Working tree restored to pre-apply state.");
                        break;

                    default:
                        await _gitService.AbortMergeAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
                        NotifySuccess("Merge aborted", "Working tree restored to pre-merge state.");
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
            NotifyInfo("Merge tool needed", "Configure an external merge tool to resolve conflicts in it.");
            await OpenSettingsAsync("ExternalTools");
            // OpenSettingsAsync calls RefreshExternalMergeToolAvailabilityAsync
            // on close, which updates HasExternalMergeTool — no need to
            // pre-set it false here, that would just flicker any consumer
            // bound to the property during the window the dialog is open.
            mergeTool = await _externalToolConfig.GetCurrentToolAsync(
                SelectedRepository.Path, ExternalToolKind.Merge, CurrentRepositoryToken);
            if (mergeTool == null)
            {
                NotifyWarning("No merge tool", "No external merge tool configured.");
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

            // Mirror the feedback users get from per-conflict
            // resolution in Leaf's own merge view — without it a failed
            // external merge silently returns as if nothing happened.
            if (staged)
            {
                NotifySuccess("Conflict resolved", $"{conflict.FilePath} resolved in {mergeTool.DisplayName}.");
            }
            else
            {
                NotifyWarning("Merge tool exited", $"{mergeTool.DisplayName} did not produce a staged result for {conflict.FilePath}.");
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
            // Mirror ContinueMergeAsync: auto-close on MergeCompleted (in-
            // editor Abort/CompleteMerge or external abort detected by the
            // file watcher).
            EventHandler<bool> closeOnComplete = (_, _) =>
            {
                if (conflictWindow.IsLoaded) conflictWindow.Close();
            };
            MergeConflictResolutionViewModel.MergeCompleted += closeOnComplete;
            try
            {
                IsBusy = false;
                await _dialogService.ShowDialogAsync(conflictWindow);
            }
            finally
            {
                conflictWindow.CommitJumpRequested -= OnMergeEditorCommitJumpRequested;
                if (conflictWindow.DataContext is ViewModels.Merge.MergeEditorViewModel vm)
                {
                    vm.MergeCompleted -= closeOnComplete;
                }
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
                // Tell any open editor window the merge state vanished beneath
                // it (external `git merge --abort`, another client wrote
                // MERGE_HEAD away, etc.) before we drop our reference to the
                // VM. Surfaces as MergeCompleted(false), which the host's
                // editor-open subscription handles by closing the window.
                MergeConflictResolutionViewModel.NotifyMergeAbortedExternally();
                MergeConflictResolutionViewModel.MergeCompleted -= OnMergeConflictResolutionCompleted;
            }

            MergeConflictResolutionViewModel = null;
            _mergeConflictRepoPath = null;
            // Symmetry: clear the snapshot too so a future refactor that
            // ever reads it without an intervening surface re-capture
            // can't leak the previous operation's verb. Today the field
            // is always re-captured before the next read, but the cost
            // of zeroing it here is one assignment vs. a class of latent
            // bugs.
            _activeResolutionOperationType = Models.GitOperationType.None;
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

        // Snapshot the operation type EVERY time we surface the editor,
        // not only on new-VM creation. Same-repo sequential operations
        // (cherry-pick → revert → rebase, etc.) reuse the VM, so a
        // capture limited to isNewViewModel would leave the previous
        // operation's type cached and `OnMergeConflictResolutionCompleted`
        // would label the new outcome with the old verb.
        _activeResolutionOperationType = SelectedRepository.OperationType;

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

            // The merge editor handles cherry-pick / revert / rebase / am
            // conflicts too; "Merge complete/aborted" would lie for those
            // cases. Read the snapshot taken when the editor opened, NOT
            // SelectedRepository.OperationType — by the time this fires,
            // git has already cleared the sentinel files and a file-watcher
            // refresh may have set OperationType=None, which would route
            // every cherry-pick / revert / rebase / am result to the
            // generic "Merge" branch.
            var (verb, what) = _activeResolutionOperationType switch
            {
                Models.GitOperationType.CherryPick => ("Cherry-pick", "Cherry-pick"),
                Models.GitOperationType.Revert => ("Revert", "Revert"),
                Models.GitOperationType.Rebase => ("Rebase", "Rebase"),
                Models.GitOperationType.Am => ("Patch apply", "Patch apply"),
                _ => ("Merge", "Merge"),
            };

            if (success)
                NotifySuccess($"{verb} complete", $"{what} completed successfully.");
            else
                NotifyInfo($"{verb} aborted", "Working tree restored.");

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            AsyncErrorHandler.Handle(ex, nameof(OnMergeConflictResolutionCompleted), isUserAction: true);
        }
    }

    /// <summary>
    /// Snapshot of <see cref="RepositoryInfo.OperationType"/> taken when
    /// the merge-editor session was opened. Read by
    /// <see cref="OnMergeConflictResolutionCompleted"/> so the success
    /// toast labels the right verb (cherry-pick / revert / rebase / am
    /// vs plain merge). Reading the live value is racy — by the time
    /// the editor closes, git has cleared the sentinel files and a file
    /// watcher refresh may have already set OperationType=None.
    /// </summary>
    private Models.GitOperationType _activeResolutionOperationType = Models.GitOperationType.None;
}
