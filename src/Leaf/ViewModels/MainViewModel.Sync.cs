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

            foreach (var group in RepositoryGroups)
            {
                foreach (var repo in group.Repositories)
                {
                    try
                    {
                        await _gitService.FetchAsync(repo.Path, cancellationToken: CurrentRepositoryToken);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Sync", $"Fetch failed for {repo.Name}", ex);
                    }
                }
            }

            StatusMessage = "Fetch complete";

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

            StatusMessage = $"Fetched from {remoteName}";
            await SelectRepositoryAsync(SelectedRepository, fetchInBackground: false);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fetch failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Refresh current repository.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (SelectedRepository != null)
        {
            await SelectRepositoryAsync(SelectedRepository);
        }
    }

    /// <summary>
    /// Pull from remote.
    /// </summary>
    [RelayCommand]
    public async Task PullAsync()
    {
        if (SelectedRepository == null) return;

        try
        {
            await BeginBusyAsync("Pulling...");
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);

            // Check if SyncAllRemotes is enabled for multi-remote repos
            if (remotes.Count > 1)
            {
                var settings = _settingsService.LoadSettings();
                if (settings.SyncAllRemotes)
                {
                    // Fetch from all remotes first
                    StatusMessage = "Fetching from all remotes...";
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

            StatusMessage = "Pulling changes...";

            // Pull from tracking branch's remote
            var trackingRemoteUrl = remotes.FirstOrDefault(r => r.Name == "origin")?.Url
                                    ?? remotes.FirstOrDefault()?.Url;
            var pullCredentialKey = _credentialService.ResolveActiveCredentialKey(trackingRemoteUrl);

            await _gitService.PullAsync(SelectedRepository.Path, pullCredentialKey, cancellationToken: CurrentRepositoryToken);

            StatusMessage = "Pull complete";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pull failed: {ex.Message}";
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

            StatusMessage = "Pushing changes...";

            // Single remote - push directly
            var remote = remotes.FirstOrDefault();
            var pushCredentialKey = _credentialService.ResolveActiveCredentialKey(remote?.Url);

            await _gitService.PushAsync(SelectedRepository.Path, remote?.Name, pushCredentialKey, cancellationToken: CurrentRepositoryToken);

            // Fetch to update remote refs in the UI
            if (remote != null)
            {
                StatusMessage = "Updating remote refs...";
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

            StatusMessage = "Push complete";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Push failed: {ex.Message}";
            await _dialogService.ShowErrorToastAsync(
                $"Failed to push:\n\n{ex.Message}",
                "Push Failed");
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
            StatusMessage = $"Pushing to {remote.Name}...";

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
        StatusMessage = "Updating remote refs...";
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

        StatusMessage = $"Pushed to {successCount} of {remotes.Count} remotes";

        if (failedMessages.Count > 0)
        {
            var errorDetail = string.Join("\n", failedMessages);
            await _dialogService.ShowErrorToastAsync(
                $"Push failed for {failedMessages.Count} remote(s):\n\n{errorDetail}",
                "Push Failed");
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

            // Update ahead/behind counts
            SelectedRepository.AheadBy = e.AheadBy;
            SelectedRepository.BehindBy = e.BehindBy;

            // Update status
            StatusMessage = $"Auto-fetched at {e.FetchTime:HH:mm}" +
                           (e.AheadBy > 0 ? $" | ↑{e.AheadBy}" : "") +
                           (e.BehindBy > 0 ? $" | ↓{e.BehindBy}" : "");

            // Notify that LastFetchTime changed (property delegates to service)
            OnPropertyChanged(nameof(LastFetchTime));
        }).FireAndForget(nameof(OnAutoFetchCompleted), isUserAction: false);
    }
}
