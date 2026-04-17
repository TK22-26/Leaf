using System;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial — submodule operations (init/update/sync/deinit
/// and "Open as Repository"). All commands refresh branches after
/// mutation so the sidebar reflects the new submodule state.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Init and update a single submodule — idempotent, safe to invoke
    /// whether the submodule is uninitialized or out-of-sync.
    /// </summary>
    [RelayCommand]
    public async Task InitSubmoduleAsync(SubmoduleInfo? submodule)
    {
        if (SelectedRepository == null || submodule == null) return;

        try
        {
            await BeginBusyAsync($"Initializing {submodule.Path}...");
            await _gitService.InitAndUpdateSubmodulesAsync(
                SelectedRepository.Path,
                [submodule.Path],
                recursive: false,
                CurrentRepositoryToken);
            await RefreshAfterSubmoduleMutationAsync();
            StatusMessage = $"Initialized {submodule.Path}.";
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync($"Init submodule {submodule.Path}", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Update a submodule to the commit recorded in the parent tree.
    /// </summary>
    [RelayCommand]
    public async Task UpdateSubmoduleAsync(SubmoduleInfo? submodule)
    {
        if (SelectedRepository == null || submodule == null) return;

        try
        {
            await BeginBusyAsync($"Updating {submodule.Path}...");
            await _gitService.InitAndUpdateSubmodulesAsync(
                SelectedRepository.Path,
                [submodule.Path],
                recursive: false,
                CurrentRepositoryToken);
            await RefreshAfterSubmoduleMutationAsync();
            StatusMessage = $"Updated {submodule.Path}.";
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync($"Update submodule {submodule.Path}", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Init + recursively update every registered submodule. Useful
    /// right after a clone when nothing is on disk yet.
    /// </summary>
    [RelayCommand]
    public async Task UpdateAllSubmodulesAsync()
    {
        if (SelectedRepository == null) return;

        try
        {
            await BeginBusyAsync("Updating all submodules (recursive)...");
            await _gitService.InitAndUpdateSubmodulesAsync(
                SelectedRepository.Path,
                [],
                recursive: true,
                CurrentRepositoryToken);
            await RefreshAfterSubmoduleMutationAsync();
            StatusMessage = "Submodules updated.";
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Update all submodules", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Copy the submodule's URL from <c>.gitmodules</c> into the local
    /// <c>.git/config</c>. Needed after an upstream URL change.
    /// </summary>
    [RelayCommand]
    public async Task SyncSubmoduleAsync(SubmoduleInfo? submodule)
    {
        if (SelectedRepository == null || submodule == null) return;

        try
        {
            await BeginBusyAsync($"Syncing {submodule.Path}...");
            await _gitService.SyncSubmodulesAsync(
                SelectedRepository.Path,
                [submodule.Path],
                recursive: false,
                CurrentRepositoryToken);
            await RefreshAfterSubmoduleMutationAsync();
            StatusMessage = $"Synced {submodule.Path}.";
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync($"Sync submodule {submodule.Path}", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Deinit a submodule: remove its working tree and unregister it
    /// from <c>.git/config</c>. Destructive enough to ask first —
    /// untracked/modified content inside the submodule will be lost
    /// when <paramref name="submodule"/> is dirty.
    /// </summary>
    [RelayCommand]
    public async Task DeinitSubmoduleAsync(SubmoduleInfo? submodule)
    {
        if (SelectedRepository == null || submodule == null) return;

        var force = submodule.IsDirty;
        var prompt = force
            ? $"Deinitialize '{submodule.Path}'?\n\nThe submodule has local changes. " +
              "They will be discarded."
            : $"Deinitialize '{submodule.Path}'?\n\nThe submodule's working tree will be removed. " +
              "The registration in .gitmodules stays intact, so you can re-init later.";
        var confirmed = await _dialogService.ShowConfirmationAsync(prompt, "Deinitialize Submodule");
        if (!confirmed) return;

        try
        {
            await BeginBusyAsync($"Deinitializing {submodule.Path}...");
            await _gitService.DeinitSubmoduleAsync(
                SelectedRepository.Path,
                submodule.Path,
                force,
                CurrentRepositoryToken);
            await RefreshAfterSubmoduleMutationAsync();
            StatusMessage = $"Deinitialized {submodule.Path}.";
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync($"Deinit submodule {submodule.Path}", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Full refresh after a submodule mutation: branches need a forced
    /// reload because <c>LoadBranchesForRepoAsync</c> otherwise
    /// short-circuits when the repo already has cached state. Then
    /// fall through to the standard refresh so the graph + working
    /// changes pick up the mutation too.
    /// </summary>
    private async Task RefreshAfterSubmoduleMutationAsync()
    {
        if (SelectedRepository == null) return;
        await LoadBranchesForRepoAsync(SelectedRepository, forceReload: true);
        await RefreshAsync();
    }

    /// <summary>
    /// Treat the submodule like a standalone repository — add it to
    /// Leaf's repo list (if not already there) and switch to it. A
    /// no-op on uninitialized submodules because there's no repo on
    /// disk yet.
    /// </summary>
    [RelayCommand]
    public async Task OpenSubmoduleAsRepositoryAsync(SubmoduleInfo? submodule)
    {
        if (SelectedRepository == null || submodule == null) return;
        if (!submodule.IsInitialized)
        {
            StatusMessage = $"{submodule.Path} is not initialized. Init it first.";
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(SelectedRepository.Path, submodule.Path));
            if (!Directory.Exists(fullPath))
            {
                StatusMessage = $"Submodule directory not found on disk: {fullPath}";
                return;
            }

            var existing = _repositoryService.FindRepository(fullPath);
            RepositoryInfo target;
            if (existing != null)
            {
                target = existing;
            }
            else
            {
                // No session token: SelectRepositoryAsync immediately
                // rotates to a fresh session for the new repo.
                target = await _gitService.GetRepositoryInfoFastAsync(fullPath);
                _repositoryService.AddRepository(target);
            }

            await SelectRepositoryAsync(target);
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync($"Open submodule {submodule.Path} as repo", ex);
        }
    }
}
