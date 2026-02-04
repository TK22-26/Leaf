using System;
using CommunityToolkit.Mvvm.Input;
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
                try
                {
                    var host = new Uri(remoteUrl).Host;
                    var credentialKey = CredentialHelper.GetCredentialKeyForHost(host);
                    if (!string.IsNullOrEmpty(credentialKey))
                    {
                        pat = _credentialService.GetCredential(credentialKey);
                    }
                }
                catch
                {
                    // Invalid URL, skip PAT
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
            StatusMessage = "Pulling changes...";

            // Try to get credentials from stored PAT (map hostname to credential key)
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path);
            var originUrl = remotes.FirstOrDefault(r => r.Name == "origin")?.Url;
            string? pat = null;
            if (!string.IsNullOrEmpty(originUrl))
            {
                var host = new Uri(originUrl).Host;
                var credentialKey = CredentialHelper.GetCredentialKeyForHost(host);
                if (!string.IsNullOrEmpty(credentialKey))
                {
                    pat = _credentialService.GetCredential(credentialKey);
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
            IsBusy = true;
            StatusMessage = "Pushing changes...";

            // Try to get credentials from stored PAT (map hostname to credential key)
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path);
            var originUrl = remotes.FirstOrDefault(r => r.Name == "origin")?.Url;
            string? pat = null;
            if (!string.IsNullOrEmpty(originUrl))
            {
                var host = new Uri(originUrl).Host;
                var credentialKey = CredentialHelper.GetCredentialKeyForHost(host);
                if (!string.IsNullOrEmpty(credentialKey))
                {
                    pat = _credentialService.GetCredential(credentialKey);
                }
            }

            await _gitService.PushAsync(SelectedRepository.Path, null, null, pat);

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
