using System;
using CommunityToolkit.Mvvm.Input;
using Leaf.Services;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial - Stash operations.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Stash changes.
    /// </summary>
    [RelayCommand]
    public async Task StashAsync()
    {
        if (SelectedRepository == null) return;

        try
        {
            await BeginBusyAsync("Stashing changes...");

            await _gitService.StashAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);

            NotifySuccess(Models.NotificationCategory.Stash, "Changes stashed", "Working tree changes saved to a new stash.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Stash", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Check if pop stash can be executed.
    /// </summary>
    private bool CanPopStash() => SelectedRepository != null && GitGraphViewModel?.SelectedStash != null;

    /// <summary>
    /// Pop the selected stashed changes.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPopStash))]
    public async Task PopStashAsync()
    {
        if (SelectedRepository == null) return;
        var selectedStash = GitGraphViewModel?.SelectedStash;
        if (selectedStash == null) return;

        Log.Info("Stash", $"PopStash: starting pop for stash index {selectedStash.Index}");

        try
        {
            await BeginBusyAsync("Popping stash...");

            var result = await _gitService.PopStashAsync(SelectedRepository.Path, selectedStash.Index, cancellationToken: CurrentRepositoryToken);

            Log.Info("Stash", $"PopStash: Success={result.Success}, HasConflicts={result.HasConflicts}, Error={result.ErrorMessage}");

            // Clear stash selection before refresh so preservation logic doesn't re-select
            GitGraphViewModel?.SelectStash(null);

            if (result.Success)
            {
                Log.Info("Stash", "PopStash: success, refreshing");
                NotifySuccess(Models.NotificationCategory.Stash, "Stash popped", "Stash applied to working tree.");
                await RefreshAsync();
            }
            else if (result.HasConflicts)
            {
                Log.Warn("Stash", "PopStash: conflicts detected, checking for actual conflicts");
                // Load conflicts first to check if there are actually any
                var conflicts = await _gitService.GetConflictsAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
                Log.Info("Stash", $"PopStash: actual conflicts found: {conflicts.Count}");

                if (conflicts.Count == 0)
                {
                    // No actual conflicts found - stash may have failed for another reason
                    NotifyWarning(Models.NotificationCategory.Stash, "Stash pop", result.ErrorMessage ?? "Stash pop completed with warnings.");
                    await RefreshAsync();
                }
                else
                {
                    NotifyWarning(Models.NotificationCategory.MergeAndRebase, "Stash conflicts", "Stash applied with conflicts — resolve to complete.");
                    await RefreshAsync();

                    // Show conflict resolution UI with friendly stash name
                    var stashName = !string.IsNullOrEmpty(selectedStash.MessageShort)
                        ? $"Stash: {selectedStash.MessageShort}"
                        : "Stashed changes";
                    var conflictViewModel = new ViewModels.Merge.MergeEditorViewModel(
                        _gitService, _clipboardService, _mergeEngine, SelectedRepository.Path)
                    {
                        SourceBranch = stashName,
                        TargetBranch = SelectedRepository.CurrentBranch ?? "HEAD",
                        IsCompactFileList = _settingsService.LoadSettings().CompactFileList,
                        GetSessionToken = () => CurrentRepositoryToken
                    };
                    await conflictViewModel.LoadConflictsAsync();

                    var conflictView = new Views.Merge.MergeEditorView
                    {
                        DataContext = conflictViewModel,
                    };

                    conflictViewModel.MergeCompleted += async (s, success) =>
                    {
                        conflictView.Close();
                        if (success)
                        {
                            // Clean up any leftover temp stash from smart pop
                            await _gitService.CleanupTempStashAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
                            NotifySuccess(Models.NotificationCategory.Stash, "Stash popped", "Stash applied successfully.");
                        }
                        else
                        {
                            NotifyInfo(Models.NotificationCategory.Stash, "Stash pop aborted", "Working tree restored to pre-pop state.");
                        }
                        // Dispose the local VM — not routed through MainViewModel's
                        // MergeConflictResolutionViewModel lifecycle, so no other
                        // code path will release the build-CTS it holds.
                        conflictViewModel.Dispose();
                        await RefreshAsync();
                    };

                    await _dialogService.ShowDialogAsync(conflictView);
                }
            }
            else
            {
                await ReportOperationFailureAsync("Pop stash", result.ErrorMessage ?? "unknown error");
            }
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Pop stash", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanDeleteStash() => SelectedRepository != null && GitGraphViewModel?.SelectedStash != null;

    /// <summary>
    /// Delete the selected stash without applying it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteStash))]
    public async Task DeleteStashAsync()
    {
        if (SelectedRepository == null) return;
        var selectedStash = GitGraphViewModel?.SelectedStash;
        if (selectedStash == null) return;

        try
        {
            await BeginBusyAsync("Deleting stash...");

            await _gitService.DeleteStashAsync(SelectedRepository.Path, selectedStash.Index, cancellationToken: CurrentRepositoryToken);

            // Clear stash selection before refresh
            GitGraphViewModel?.SelectStash(null);

            NotifySuccess(Models.NotificationCategory.Stash, "Stash deleted", "Stash dropped from the stash list.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Delete stash", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Stash changes with a message.
    /// </summary>
    public async Task StashChangesAsync(string message)
    {
        if (SelectedRepository == null) return;
        await _gitService.StashAsync(SelectedRepository.Path, message, cancellationToken: CurrentRepositoryToken);
    }
}
