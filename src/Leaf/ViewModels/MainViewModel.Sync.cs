using System;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;
using Leaf.Utils;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial - Sync operations (fetch, pull, push, refresh).
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Fetch all repositories.
    /// </summary>
    [RelayCommand]
    public async Task FetchAllAsync()
    {
        try
        {
            await BeginBusyAsync("Fetching all repositories...");

            // Count attempts and capture failed repo names so the toast
            // doesn't lie. The inner loop swallows per-repo errors into
            // the log; surfacing the repo names directly in the toast
            // saves the user from opening the log file for the common
            // case ("you have 3 broken remotes — these three").
            int total = 0;
            int succeeded = 0;
            var failedNames = new List<string>();
            foreach (var group in RepositoryGroups)
            {
                foreach (var repo in group.Repositories)
                {
                    total++;
                    try
                    {
                        await _gitService.FetchAsync(repo.Path, cancellationToken: CurrentRepositoryToken);
                        succeeded++;
                    }
                    catch (Exception ex)
                    {
                        failedNames.Add(repo.Name);
                        Log.Error("Sync", $"Fetch failed for {repo.Name}", ex);
                    }
                }
            }

            if (succeeded == total)
            {
                NotifySuccess(Models.NotificationCategory.SyncOperations, "Fetch complete", $"Fetched {succeeded} repositor{(succeeded == 1 ? "y" : "ies")}.");
            }
            else
            {
                // Cap the listed names so the toast stays readable. The
                // log has the full picture either way.
                const int MaxNamesInToast = 5;
                var displayed = failedNames.Take(MaxNamesInToast);
                var listed = string.Join(", ", displayed);
                if (failedNames.Count > MaxNamesInToast)
                {
                    listed += $", +{failedNames.Count - MaxNamesInToast} more";
                }
                NotifyWarning(
                    Models.NotificationCategory.SyncOperations,
                    "Fetch finished with errors",
                    $"Fetched {succeeded} of {total} repositories.\nFailed: {listed}");
            }

            // Refresh current repo if selected
            if (SelectedRepository != null)
            {
                await SelectRepositoryAsync(SelectedRepository, fetchInBackground: false);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Fetch from a specific remote.
    /// </summary>
    [RelayCommand]
    public async Task FetchRemoteAsync(string? remoteName)
    {
        if (SelectedRepository == null || string.IsNullOrEmpty(remoteName))
            return;

        try
        {
            await BeginBusyAsync($"Fetching from {remoteName}...");

            // Resolve credential key from the remote URL only if Leaf has a PAT
            // stored — otherwise git falls back to GCM. The PAT itself is
            // looked up in-process by Leaf.AskPass.exe.
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
            var remoteUrl = remotes.FirstOrDefault(r => r.Name == remoteName)?.Url;
            var credentialKey = _credentialService.ResolveActiveCredentialKey(remoteUrl);

            await _gitService.FetchAsync(SelectedRepository.Path, remoteName, credentialKey: credentialKey, cancellationToken: CurrentRepositoryToken);

            NotifySuccess(Models.NotificationCategory.SyncOperations, "Fetch complete", $"Fetched from {remoteName}.");
            await SelectRepositoryAsync(SelectedRepository, fetchInBackground: false);
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Fetch", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Refresh current repository.
    /// </summary>
    /// <remarks>
    /// Semantically this is "the user (or an operation that just mutated
    /// state) is asking for fresh data" — it must bypass the branch-load
    /// cache. SelectRepositoryAsync's BranchesLoaded short-circuit was
    /// designed to keep re-selecting the same repo from the sidebar cheap,
    /// not to gate explicit refreshes; otherwise every mutating caller
    /// (delete/rename/finish-flow/PR-merge/...) has to remember to set
    /// BranchesLoaded = false beforehand, and forgetting it leaves the
    /// sidebar showing phantom branches until the file watcher's debounce
    /// catches up. Invalidating here makes the contract uniform across
    /// every call site.
    /// </remarks>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (SelectedRepository != null)
        {
            SelectedRepository.BranchesLoaded = false;
            await SelectRepositoryAsync(SelectedRepository, fetchInBackground: false);
        }
    }

    /// <summary>
    /// Pull from remote, honouring the user's <c>pull.rebase</c> git
    /// config. The toolbar's primary Pull button binds here.
    /// </summary>
    [RelayCommand]
    public Task PullAsync() => PullCoreAsync(rebaseOverride: null);

    /// <summary>
    /// Pull with <c>--rebase</c> regardless of the user's <c>pull.rebase</c>
    /// config. Bound to the "Pull (rebase)" sub-entry on the Pull split-button.
    /// </summary>
    [RelayCommand]
    public Task PullRebaseAsync() => PullCoreAsync(rebaseOverride: true);

    /// <summary>
    /// Shared body for both pull commands. <paramref name="rebaseOverride"/>
    /// flows straight to <see cref="IGitService.PullAsync"/>: <c>null</c>
    /// defers to git config, <c>true</c> forces rebase, <c>false</c> would
    /// force merge (no UI surface for that today, but the plumbing is
    /// symmetric so a future "Pull (merge)" wires up cleanly).
    /// </summary>
    private async Task PullCoreAsync(bool? rebaseOverride)
    {
        if (SelectedRepository == null) return;

        var label = rebaseOverride == true ? "Pulling (rebase)..." : "Pulling...";

        try
        {
            await BeginBusyAsync(label);
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);

            // Check if SyncAllRemotes is enabled for multi-remote repos
            if (remotes.Count > 1)
            {
                var settings = _settingsService.LoadSettings();
                if (settings.SyncAllRemotes)
                {
                    // Fetch from all remotes first — the busy spinner is
                    // already up (BeginBusyAsync); no per-remote status.
                    foreach (var remote in remotes)
                    {
                        var fetchCredentialKey = _credentialService.ResolveActiveCredentialKey(remote.Url);

                        try
                        {
                            await _gitService.FetchAsync(SelectedRepository.Path, remote.Name, credentialKey: fetchCredentialKey, cancellationToken: CurrentRepositoryToken);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Sync", $"Fetch from {remote.Name} failed during pull", ex);
                        }
                    }
                }
            }

            // Pull from tracking branch's remote
            var trackingRemoteUrl = remotes.FirstOrDefault(r => r.Name == "origin")?.Url
                                    ?? remotes.FirstOrDefault()?.Url;
            var pullCredentialKey = _credentialService.ResolveActiveCredentialKey(trackingRemoteUrl);

            await _gitService.PullAsync(
                SelectedRepository.Path,
                pullCredentialKey,
                rebase: rebaseOverride,
                cancellationToken: CurrentRepositoryToken);

            var successDescription = rebaseOverride == true
                ? "Your branch was rebased onto the remote."
                : "Your branch is up to date with the remote.";
            NotifySuccess(Models.NotificationCategory.SyncOperations, "Pull complete", successDescription);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync(rebaseOverride == true ? "Pull (rebase)" : "Pull", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Push to remote.
    /// </summary>
    [RelayCommand]
    public async Task PushAsync()
    {
        if (SelectedRepository == null) return;

        try
        {
            await BeginBusyAsync("Pushing...");
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);

            // Check if there are multiple remotes
            if (remotes.Count > 1)
            {
                var settings = _settingsService.LoadSettings();
                if (settings.SyncAllRemotes)
                {
                    // Push to all remotes automatically
                    await PushToAllRemotesAsync(remotes);
                    return;
                }

                // Show selection dialog
                await PushWithSelectionAsync();
                return;
            }

            // Single remote - push directly. Busy spinner is up via
            // BeginBusyAsync; no per-stage status update.
            var remote = remotes.FirstOrDefault();
            var pushCredentialKey = _credentialService.ResolveActiveCredentialKey(remote?.Url);

            await _gitService.PushAsync(SelectedRepository.Path, remote?.Name, pushCredentialKey, cancellationToken: CurrentRepositoryToken);

            // Fetch to update remote refs in the UI
            if (remote != null)
            {
                try
                {
                    await _gitService.FetchAsync(SelectedRepository.Path, remote.Name, credentialKey: pushCredentialKey, cancellationToken: CurrentRepositoryToken);
                }
                catch (InvalidOperationException ex)
                {
                    // Post-push fetch is cosmetic; the push itself succeeded.
                    Log.Info("Sync", $"Post-push fetch {remote.Name} failed: {ex.Message}");
                }
            }

            // remote is virtually always non-null at this point: a repo
            // with zero remotes would have caused PushAsync to fail (no
            // remote arg = no upstream = "fatal: No configured push
            // destination") and the catch below would surface that. The
            // null-arm of the conditional below is a defensive belt for
            // the rare case where PushAsync succeeded against an
            // unconfigured push target (e.g. credentials-only-no-fetch
            // remote we never enumerate); the toast then drops the
            // target name.
            NotifySuccess(
                Models.NotificationCategory.SyncOperations,
                "Push complete",
                remote != null ? $"Pushed to {remote.Name}." : "Push completed.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Push", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Push to all remotes.
    /// </summary>
    private async Task PushToAllRemotesAsync(List<RemoteInfo> remotes)
    {
        if (SelectedRepository == null) return;

        // IsBusy is already true — caller (PushAsync) did BeginBusyAsync.
        var successCount = 0;
        var failedMessages = new List<string>();
        var pushedRemotes = new List<(RemoteInfo remote, string? credentialKey)>();

        foreach (var remote in remotes)
        {
            var credentialKey = _credentialService.ResolveActiveCredentialKey(remote.Url);

            try
            {
                await _gitService.PushAsync(SelectedRepository.Path, remote.Name, credentialKey, cancellationToken: CurrentRepositoryToken);
                successCount++;
                pushedRemotes.Add((remote, credentialKey));
            }
            catch (Exception ex)
            {
                failedMessages.Add($"{remote.Name}: {ex.Message}");
                Log.Error("Sync", $"Push to {remote.Name} failed", ex);
            }
        }

        // Fetch from all pushed remotes to update remote refs in the UI
        foreach (var (remote, credentialKey) in pushedRemotes)
        {
            try
            {
                await _gitService.FetchAsync(SelectedRepository.Path, remote.Name, credentialKey: credentialKey, cancellationToken: CurrentRepositoryToken);
            }
            catch (InvalidOperationException ex)
            {
                // Post-push fetch is cosmetic; the push itself succeeded.
                Log.Info("Sync", $"Post-push fetch {remote.Name} failed: {ex.Message}");
            }
        }

        if (failedMessages.Count > 0)
        {
            var errorDetail = string.Join("\n", failedMessages);
            await _dialogService.ShowErrorToastAsync(
                $"Push failed for {failedMessages.Count} remote(s):\n\n{errorDetail}",
                "Push Failed");
        }
        else if (successCount > 0)
        {
            NotifySuccess(Models.NotificationCategory.SyncOperations, "Push complete",
                $"Pushed to {successCount} of {remotes.Count} remote(s).");
        }

        await RefreshAsync();
        // IsBusy is dropped by caller (PushAsync) in its finally block —
        // no need to flip it here.
    }

    /// <summary>
    /// Start the auto-fetch timer.
    /// </summary>
    private void StartAutoFetchTimer()
    {
        _autoFetchService.Start(AutoFetchInterval, () => SelectedRepository?.Path);
    }

    /// <summary>
    /// Stop the auto-fetch timer.
    /// </summary>
    public void StopAutoFetchTimer()
    {
        _autoFetchService.Stop();
    }

    /// <summary>
    /// Handle auto-fetch completion - update UI state.
    /// </summary>
    private void OnAutoFetchCompleted(object? sender, AutoFetchCompletedEventArgs e)
    {
        _dispatcherService.InvokeAsync(() =>
        {
            if (SelectedRepository == null)
                return;

            if (!string.Equals(SelectedRepository.Path, e.RepositoryPath, StringComparison.OrdinalIgnoreCase))
                return;

            // Update ahead/behind counts
            SelectedRepository.AheadBy = e.AheadBy;
            SelectedRepository.BehindBy = e.BehindBy;

            // No toast on auto-fetch — it runs every N minutes in the
            // background and would be pure noise. The ahead/behind
            // counters above and the LastFetchTime indicator below are
            // the legitimate signals.

            // Notify that LastFetchTime changed (property delegates to service)
            OnPropertyChanged(nameof(LastFetchTime));

            RefreshAfterBackgroundFetchAsync(SelectedRepository)
                .FireAndForget(nameof(RefreshAfterBackgroundFetchAsync), isUserAction: false);
        }).FireAndForget(nameof(OnAutoFetchCompleted), isUserAction: false);
    }

    private async Task RefreshAfterBackgroundFetchAsync(RepositoryInfo repository)
    {
        if (SelectedRepository == null ||
            !string.Equals(SelectedRepository.Path, repository.Path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        repository.BranchesLoaded = false;
        await LoadBranchesForRepoAsync(repository, forceReload: true, skipFilterApplication: true);

        if (SelectedRepository == null ||
            !string.Equals(SelectedRepository.Path, repository.Path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (repository.HiddenBranchNames.Count > 0 || repository.SoloBranchNames.Count > 0)
        {
            ApplyBranchFiltersForRepo(repository);
        }

        if (GitGraphViewModel != null)
        {
            await GitGraphViewModel.LoadRepositoryAsync(repository.Path);
        }
    }
}
