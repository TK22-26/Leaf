using System;
using CommunityToolkit.Mvvm.Input;

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
            IsBusy = true;
            StatusMessage = "Stashing changes...";

            await _gitService.StashAsync(SelectedRepository.Path);

            StatusMessage = "Changes stashed";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Stash failed: {ex.Message}";
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

        System.Diagnostics.Debug.WriteLine($"[MainVM.PopStash] Starting pop for stash index {selectedStash.Index}");

        try
        {
            IsBusy = true;
            StatusMessage = "Popping stash...";

            var result = await _gitService.PopStashAsync(SelectedRepository.Path, selectedStash.Index);

            System.Diagnostics.Debug.WriteLine($"[MainVM.PopStash] Service returned: Success={result.Success}, HasConflicts={result.HasConflicts}, Error={result.ErrorMessage}");

            // Clear stash selection before refresh so preservation logic doesn't re-select
            GitGraphViewModel?.SelectStash(null);

            if (result.Success)
            {
                System.Diagnostics.Debug.WriteLine("[MainVM.PopStash] SUCCESS branch - refreshing");
                StatusMessage = "Stash popped";
                await RefreshAsync();
            }
            else if (result.HasConflicts)
            {
                System.Diagnostics.Debug.WriteLine("[MainVM.PopStash] CONFLICTS branch - checking for actual conflicts");
                // Load conflicts first to check if there are actually any
                var conflicts = await _gitService.GetConflictsAsync(SelectedRepository.Path);
                System.Diagnostics.Debug.WriteLine($"[MainVM.PopStash] Actual conflicts found: {conflicts.Count}");

                if (conflicts.Count == 0)
                {
                    // No actual conflicts found - stash may have failed for another reason
                    StatusMessage = result.ErrorMessage ?? "Stash pop completed with warnings";
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = "Stash applied with conflicts - resolve to complete";
                    await RefreshAsync();

                    // Show conflict resolution UI with friendly stash name
                    var stashName = !string.IsNullOrEmpty(selectedStash.MessageShort)
                        ? $"Stash: {selectedStash.MessageShort}"
                        : "Stashed changes";
                    var conflictViewModel = new ConflictResolutionViewModel(_gitService, _clipboardService, _dispatcherService, SelectedRepository.Path)
                    {
                        SourceBranch = stashName,
                        TargetBranch = SelectedRepository.CurrentBranch ?? "HEAD"
                    };
                    await conflictViewModel.LoadConflictsAsync();

                    var conflictView = new Views.ConflictResolutionView
                    {
                        DataContext = conflictViewModel,
                        Owner = _ownerWindow
                    };

                    conflictViewModel.MergeCompleted += async (s, success) =>
                    {
                        conflictView.Close();
                        if (success)
                        {
                            // Clean up any leftover temp stash from smart pop
                            await _gitService.CleanupTempStashAsync(SelectedRepository.Path);
                            StatusMessage = "Stash applied successfully";
                        }
                        else
                        {
                            StatusMessage = "Stash pop aborted";
                        }
                        await RefreshAsync();
                    };

                    conflictView.ShowDialog();
                }
            }
            else
            {
                StatusMessage = $"Pop stash failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pop stash failed: {ex.Message}";
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
            IsBusy = true;
            StatusMessage = "Deleting stash...";

            await _gitService.DeleteStashAsync(SelectedRepository.Path, selectedStash.Index);

            // Clear stash selection before refresh
            GitGraphViewModel?.SelectStash(null);

            StatusMessage = "Stash deleted";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Delete stash failed: {ex.Message}";
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
        await _gitService.StashAsync(SelectedRepository.Path, message);
    }
}
