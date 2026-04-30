using System;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial — submodule operations (init/update/sync/deinit,
/// add/remove/update-to-remote, and "Open as Repository"). Every command
/// that mutates submodule state refreshes the sidebar in a <c>finally</c>
/// block so a failed mutation still reflects whatever partial state git
/// left on disk.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Init and update a single submodule — idempotent, safe to invoke
    /// whether the submodule is uninitialized or out-of-sync.
    /// </summary>
    [RelayCommand]
    public Task InitSubmoduleAsync(SubmoduleInfo? submodule) =>
        RunSubmoduleInitUpdateAsync(submodule, verbProgressive: "Initializing", verbPast: "Initialized");

    /// <summary>
    /// Update a submodule to the commit recorded in the parent tree.
    /// Thin alias for <see cref="InitSubmoduleAsync"/> — the underlying
    /// <c>git submodule update --init</c> is idempotent — but the
    /// distinct menu entry matches the two mental models users
    /// actually have (init vs update to recorded).
    /// </summary>
    [RelayCommand]
    public Task UpdateSubmoduleAsync(SubmoduleInfo? submodule) =>
        RunSubmoduleInitUpdateAsync(submodule, verbProgressive: "Updating", verbPast: "Updated");

    private async Task RunSubmoduleInitUpdateAsync(SubmoduleInfo? submodule, string verbProgressive, string verbPast)
    {
        var originRepo = SelectedRepository;
        if (originRepo == null || submodule == null) return;

        try
        {
            await BeginBusyAsync($"{verbProgressive} {submodule.Path}...");
            await _gitService.InitAndUpdateSubmodulesAsync(
                originRepo.Path,
                [submodule.Path],
                recursive: false,
                CurrentRepositoryToken);
            NotifySuccess("Submodule updated", $"{verbPast} {submodule.Path}.");
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync($"{verbProgressive} submodule {submodule.Path}", ex);
        }
        finally
        {
            await RefreshAfterSubmoduleMutationAsync(originRepo);
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
        var originRepo = SelectedRepository;
        if (originRepo == null) return;

        try
        {
            await BeginBusyAsync("Updating all submodules (recursive)...");
            await _gitService.InitAndUpdateSubmodulesAsync(
                originRepo.Path,
                [],
                recursive: true,
                CurrentRepositoryToken);
            NotifySuccess("Submodules updated", "All registered submodules initialized and updated.");
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Update all submodules", ex);
        }
        finally
        {
            await RefreshAfterSubmoduleMutationAsync(originRepo);
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
        var originRepo = SelectedRepository;
        if (originRepo == null || submodule == null) return;

        try
        {
            await BeginBusyAsync($"Syncing {submodule.Path}...");
            await _gitService.SyncSubmodulesAsync(
                originRepo.Path,
                [submodule.Path],
                recursive: false,
                CurrentRepositoryToken);
            NotifySuccess("Submodule synced", $"{submodule.Path} URL copied to .git/config.");
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync($"Sync submodule {submodule.Path}", ex);
        }
        finally
        {
            await RefreshAfterSubmoduleMutationAsync(originRepo);
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
        var originRepo = SelectedRepository;
        if (originRepo == null || submodule == null) return;

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
                originRepo.Path,
                submodule.Path,
                force,
                CurrentRepositoryToken);
            NotifySuccess("Submodule deinitialized", $"{submodule.Path} working tree removed.");
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync($"Deinit submodule {submodule.Path}", ex);
        }
        finally
        {
            await RefreshAfterSubmoduleMutationAsync(originRepo);
            IsBusy = false;
        }
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
            NotifyWarning("Submodule not initialized", $"{submodule.Path} is not initialized. Init it first.");
            return;
        }

        try
        {
            await BeginBusyAsync($"Opening {submodule.Path}...");

            var repoRoot = Path.GetFullPath(SelectedRepository.Path);
            var fullPath = Path.GetFullPath(Path.Combine(repoRoot, submodule.Path));

            // Belt-and-braces: the parser rejects traversal segments,
            // but a non-parser path into SubmoduleInfo (a future fake in
            // tests, say) would otherwise let a malicious repo send
            // the user to an arbitrary directory.
            var rootWithSep = repoRoot.EndsWith(Path.DirectorySeparatorChar)
                ? repoRoot
                : repoRoot + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            {
                NotifyWarning("Submodule refused", $"Refusing to open '{submodule.Path}': resolves outside the repository.");
                return;
            }

            if (!Directory.Exists(fullPath))
            {
                NotifyWarning("Submodule missing", $"Submodule directory not found on disk: {fullPath}");
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
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Open the add-submodule dialog and register the result. Leaves
    /// the resulting <c>.gitmodules</c> + gitlink staged so the user
    /// can inspect and commit via the normal commit path.
    /// </summary>
    [RelayCommand]
    public async Task AddSubmoduleAsync()
    {
        var originRepo = SelectedRepository;
        if (originRepo == null) return;

        var dialog = new Views.AddSubmoduleDialog();
        if (await _dialogService.ShowDialogAsync(dialog) != true)
            return;

        try
        {
            await BeginBusyAsync($"Adding submodule at {dialog.Path}...");
            await _gitService.AddSubmoduleAsync(
                originRepo.Path,
                dialog.Url,
                dialog.Path,
                dialog.Branch,
                CurrentRepositoryToken);
            NotifySuccess("Submodule added", $"Added at {dialog.Path}. Commit to finalize.");
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Add submodule", ex);
        }
        finally
        {
            await RefreshAfterSubmoduleMutationAsync(originRepo);
            IsBusy = false;
        }
    }

    /// <summary>
    /// Update a submodule to the tip of its tracked branch
    /// (<c>git submodule update --remote</c>). Only meaningful when the
    /// submodule has a <c>branch</c> configured — callers should hide
    /// the menu item otherwise, but the command itself also throws
    /// early with a clear message if you invoke it anyway.
    /// </summary>
    [RelayCommand]
    public async Task UpdateSubmoduleToRemoteAsync(SubmoduleInfo? submodule)
    {
        var originRepo = SelectedRepository;
        if (originRepo == null || submodule == null) return;
        if (string.IsNullOrWhiteSpace(submodule.Branch))
        {
            NotifyWarning("No tracking branch", $"{submodule.Path} has no tracking branch configured.");
            return;
        }

        try
        {
            await BeginBusyAsync($"Updating {submodule.Path} to tip of {submodule.Branch}...");
            await _gitService.UpdateSubmoduleToRemoteAsync(
                originRepo.Path,
                submodule.Path,
                CurrentRepositoryToken);
            NotifySuccess("Submodule updated", $"Updated {submodule.Path} to latest on {submodule.Branch}.");
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync($"Update submodule {submodule.Path} to remote", ex);
        }
        finally
        {
            await RefreshAfterSubmoduleMutationAsync(originRepo);
            IsBusy = false;
        }
    }

    /// <summary>
    /// Fully remove a submodule — deinit, cache cleanup, and staged
    /// removal of the gitlink and <c>.gitmodules</c> entry. Commit is
    /// left to the user so they can write a message.
    /// </summary>
    [RelayCommand]
    public async Task RemoveSubmoduleAsync(SubmoduleInfo? submodule)
    {
        var originRepo = SelectedRepository;
        if (originRepo == null || submodule == null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            $"Remove submodule '{submodule.Path}'?\n\n" +
            "This unregisters the submodule, deletes its working tree, and stages the " +
            "removal from .gitmodules. You'll still need to commit to finalize the change.",
            "Remove Submodule");
        if (!confirmed) return;

        try
        {
            await BeginBusyAsync($"Removing {submodule.Path}...");
            await _gitService.RemoveSubmoduleAsync(
                originRepo.Path,
                submodule,
                CurrentRepositoryToken);
            NotifySuccess("Submodule removed", $"Removed {submodule.Path}. Commit to finalize.");
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync($"Remove submodule {submodule.Path}", ex);
        }
        finally
        {
            await RefreshAfterSubmoduleMutationAsync(originRepo);
            IsBusy = false;
        }
    }

    /// <summary>
    /// Full refresh after a submodule mutation. Callers capture
    /// <see cref="SelectedRepository"/> at op-start and pass it here
    /// so a repo-switch mid-operation doesn't cause us to reload the
    /// *new* repo's branches (which would silently leave the origin
    /// repo holding stale pre-mutation data). If the user did switch
    /// we mark the origin's branch cache stale so the next visit
    /// picks up the real post-mutation state.
    /// </summary>
    private async Task RefreshAfterSubmoduleMutationAsync(RepositoryInfo? originRepo)
    {
        if (originRepo == null) return;

        if (SelectedRepository != originRepo)
        {
            // User navigated away — don't churn the current repo, but
            // make sure the origin picks up the mutation next time it
            // becomes active.
            originRepo.BranchesLoaded = false;
            return;
        }

        await LoadBranchesForRepoAsync(originRepo, forceReload: true);
        await RefreshAsync();
    }
}
