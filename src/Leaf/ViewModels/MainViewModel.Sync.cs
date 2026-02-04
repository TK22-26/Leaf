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
            IsBusy = true;
            StatusMessage = "Fetching all repositories...";

            foreach (var group in RepositoryGroups)
            {
                foreach (var repo in group.Repositories)
                {
                    try
                    {
                        await _gitService.FetchAsync(repo.Path);
                    }
                    catch
                    {
                        // Continue with other repos
                    }
                }
            }

            StatusMessage = "Fetch complete";

            // Refresh current repo if selected
            if (SelectedRepository != null)
            {
                await SelectRepositoryAsync(SelectedRepository);
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
            IsBusy = true;
            StatusMessage = $"Fetching from {remoteName}...";

            // Get credentials for this remote (map hostname to credential key)
            string? pat = null;
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path);
            var remoteUrl = remotes.FirstOrDefault(r => r.Name == remoteName)?.Url;
            if (!string.IsNullOrEmpty(remoteUrl))
            {
                var credentialKey = CredentialHelper.GetCredentialKeyForUrl(remoteUrl);
                if (!string.IsNullOrEmpty(credentialKey))
                {
                    pat = _credentialService.GetPat(credentialKey);
                }
            }

            await _gitService.FetchAsync(SelectedRepository.Path, remoteName, password: pat);

            StatusMessage = $"Fetched from {remoteName}";
            await RefreshAsync();
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
            IsBusy = true;
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path);

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
                        string? fetchPat = null;
                        if (!string.IsNullOrEmpty(remote.Url))
                        {
                            var credentialKey = CredentialHelper.GetCredentialKeyForUrl(remote.Url);
                            if (!string.IsNullOrEmpty(credentialKey))
                            {
                                fetchPat = _credentialService.GetPat(credentialKey);
                            }
                        }

                        try
                        {
                            await _gitService.FetchAsync(SelectedRepository.Path, remote.Name, password: fetchPat);
                        }
                        catch
                        {
                            // Continue with other remotes
                        }
                    }
                }
            }

            StatusMessage = "Pulling changes...";

            // Pull from tracking branch's remote
            var trackingRemoteUrl = remotes.FirstOrDefault(r => r.Name == "origin")?.Url
                                    ?? remotes.FirstOrDefault()?.Url;
            string? pat = null;
            if (!string.IsNullOrEmpty(trackingRemoteUrl))
            {
                var credentialKey = CredentialHelper.GetCredentialKeyForUrl(trackingRemoteUrl);
                if (!string.IsNullOrEmpty(credentialKey))
                {
                    pat = _credentialService.GetPat(credentialKey);
                }
            }

            await _gitService.PullAsync(SelectedRepository.Path, null, pat);

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
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path);

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

            IsBusy = true;
            StatusMessage = "Pushing changes...";

            // Single remote - push directly
            var remote = remotes.FirstOrDefault();
            string? pat = null;
            if (!string.IsNullOrEmpty(remote?.Url))
            {
                var credentialKey = CredentialHelper.GetCredentialKeyForUrl(remote.Url);
                if (!string.IsNullOrEmpty(credentialKey))
                {
                    pat = _credentialService.GetPat(credentialKey);
                }
            }

            await _gitService.PushAsync(SelectedRepository.Path, remote?.Name, null, pat);

            StatusMessage = "Push complete";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Push failed: {ex.Message}";
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

        IsBusy = true;
        var successCount = 0;

        foreach (var remote in remotes)
        {
            StatusMessage = $"Pushing to {remote.Name}...";

            string? pat = null;
            if (!string.IsNullOrEmpty(remote.Url))
            {
                var credentialKey = CredentialHelper.GetCredentialKeyForUrl(remote.Url);
                if (!string.IsNullOrEmpty(credentialKey))
                {
                    pat = _credentialService.GetPat(credentialKey);
                }
            }

            try
            {
                await _gitService.PushAsync(SelectedRepository.Path, remote.Name, null, pat);
                successCount++;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Push to {remote.Name} failed: {ex.Message}");
            }
        }

        StatusMessage = $"Pushed to {successCount} of {remotes.Count} remotes";
        await RefreshAsync();
        IsBusy = false;
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
    }
}
