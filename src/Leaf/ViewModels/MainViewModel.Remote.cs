using System;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Utils;
using Leaf.Views;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial - Remote management operations.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Add a new remote to the repository.
    /// </summary>
    [RelayCommand]
    public async Task AddRemoteAsync()
    {
        if (SelectedRepository == null) return;

        try
        {
            // Get existing remote names
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path);
            var existingNames = remotes.Select(r => r.Name);

            var dialog = new RemoteDialog(existingNames) { Owner = _ownerWindow };

            if (dialog.ShowDialog() != true) return;

            IsBusy = true;
            StatusMessage = $"Adding remote '{dialog.RemoteName}'...";

            await _gitService.AddRemoteAsync(
                SelectedRepository.Path,
                dialog.RemoteName,
                dialog.FetchUrl,
                dialog.PushUrl);

            // Refresh branches to show the new remote
            SelectedRepository.BranchesLoaded = false;
            await LoadBranchesForRepoAsync(SelectedRepository, forceReload: true);

            StatusMessage = $"Added remote '{dialog.RemoteName}'";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Add remote failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Edit an existing remote.
    /// </summary>
    [RelayCommand]
    public async Task EditRemoteAsync(RemoteBranchGroup remote)
    {
        if (SelectedRepository == null || remote == null) return;

        try
        {
            // Get existing remote names and the full remote info
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path);
            var remoteInfo = remotes.FirstOrDefault(r => r.Name == remote.Name);

            if (remoteInfo == null)
            {
                StatusMessage = $"Remote '{remote.Name}' not found";
                return;
            }

            var existingNames = remotes.Select(r => r.Name);
            var dialog = new RemoteDialog(existingNames, remoteInfo.Name, remoteInfo.Url, remoteInfo.PushUrl)
            {
                Owner = _ownerWindow
            };

            if (dialog.ShowDialog() != true) return;

            IsBusy = true;
            StatusMessage = $"Updating remote '{remote.Name}'...";

            // Check if name changed - rename first
            if (!string.Equals(remote.Name, dialog.RemoteName, StringComparison.OrdinalIgnoreCase))
            {
                await _gitService.RenameRemoteAsync(SelectedRepository.Path, remote.Name, dialog.RemoteName);
            }

            // Update URLs
            var currentRemoteName = dialog.RemoteName; // Use new name if renamed
            await _gitService.SetRemoteUrlAsync(SelectedRepository.Path, currentRemoteName, dialog.FetchUrl, isPushUrl: false);

            if (dialog.PushUrl != null)
            {
                await _gitService.SetRemoteUrlAsync(SelectedRepository.Path, currentRemoteName, dialog.PushUrl, isPushUrl: true);
            }

            // Refresh branches
            SelectedRepository.BranchesLoaded = false;
            await LoadBranchesForRepoAsync(SelectedRepository, forceReload: true);

            StatusMessage = $"Updated remote '{currentRemoteName}'";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Edit remote failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Remove a remote from the repository.
    /// </summary>
    [RelayCommand]
    public async Task RemoveRemoteAsync(string remoteName)
    {
        if (SelectedRepository == null || string.IsNullOrEmpty(remoteName)) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            $"Remove remote '{remoteName}'?\n\nThis will remove the remote configuration. Remote branches will no longer be tracked.",
            "Remove Remote");

        if (!confirmed) return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Removing remote '{remoteName}'...";

            await _gitService.RemoveRemoteAsync(SelectedRepository.Path, remoteName);

            // Refresh branches
            SelectedRepository.BranchesLoaded = false;
            await LoadBranchesForRepoAsync(SelectedRepository, forceReload: true);

            StatusMessage = $"Removed remote '{remoteName}'";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Remove remote failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Set a remote as the default for push operations.
    /// </summary>
    [RelayCommand]
    public async Task SetDefaultRemoteAsync(string remoteName)
    {
        if (SelectedRepository == null || string.IsNullOrEmpty(remoteName)) return;

        try
        {
            await _gitService.SetConfigAsync(SelectedRepository.Path, "leaf.defaultremote", remoteName);

            // Refresh branches to update the default indicator
            SelectedRepository.BranchesLoaded = false;
            await LoadBranchesForRepoAsync(SelectedRepository, forceReload: true);

            StatusMessage = $"Set '{remoteName}' as default remote";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Set default remote failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Copy a remote's URL to the clipboard.
    /// </summary>
    [RelayCommand]
    public void CopyRemoteUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;

        try
        {
            Clipboard.SetText(url);
            StatusMessage = "Copied URL to clipboard";
        }
        catch
        {
            StatusMessage = "Failed to copy URL";
        }
    }

    /// <summary>
    /// Fetch from all remotes.
    /// </summary>
    [RelayCommand]
    public async Task FetchAllRemotesAsync()
    {
        if (SelectedRepository == null) return;

        try
        {
            IsBusy = true;
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path);

            var successCount = 0;
            foreach (var remote in remotes)
            {
                StatusMessage = $"Fetching {remote.Name}...";

                // Get credentials for this remote
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
                    await _gitService.FetchAsync(SelectedRepository.Path, remote.Name, password: pat);
                    successCount++;
                }
                catch (Exception ex)
                {
                    // Log but continue with other remotes
                    System.Diagnostics.Debug.WriteLine($"Fetch failed for {remote.Name}: {ex.Message}");
                }
            }

            StatusMessage = $"Fetched from {successCount} of {remotes.Count} remotes";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fetch all failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Push with remote selection dialog (for multi-remote scenarios).
    /// </summary>
    public async Task PushWithSelectionAsync()
    {
        if (SelectedRepository == null) return;

        try
        {
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path);

            if (remotes.Count <= 1)
            {
                // Single remote - use existing push behavior
                await PushAsync();
                return;
            }

            // Multiple remotes - show selection dialog
            var defaultRemote = await _gitService.GetConfigAsync(SelectedRepository.Path, "leaf.defaultremote") ?? "origin";

            var dialog = new PushDialog(SelectedRepository.CurrentBranch, remotes, defaultRemote)
            {
                Owner = _ownerWindow
            };

            if (dialog.ShowDialog() != true) return;

            IsBusy = true;
            var selectedRemotes = dialog.SelectedRemoteNames.ToList();
            var pushedRemotes = new List<(RemoteInfo remote, string? pat)>();

            foreach (var remoteName in selectedRemotes)
            {
                StatusMessage = $"Pushing to {remoteName}...";

                // Get credentials for this remote
                var remoteInfo = remotes.FirstOrDefault(r => r.Name == remoteName);
                string? pat = null;

                if (!string.IsNullOrEmpty(remoteInfo?.Url))
                {
                    var credentialKey = CredentialHelper.GetCredentialKeyForUrl(remoteInfo.Url);
                    if (!string.IsNullOrEmpty(credentialKey))
                    {
                        pat = _credentialService.GetPat(credentialKey);
                    }
                }

                try
                {
                    await _gitService.PushAsync(SelectedRepository.Path, remoteName, null, pat);
                    if (remoteInfo != null)
                    {
                        pushedRemotes.Add((remoteInfo, pat));
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Push to {remoteName} failed: {ex.Message}";
                    // Continue with other remotes or stop?
                    // For now, continue
                }
            }

            // Fetch from all pushed remotes to update remote refs in the UI
            StatusMessage = "Updating remote refs...";
            foreach (var (remote, pat) in pushedRemotes)
            {
                try
                {
                    await _gitService.FetchAsync(SelectedRepository.Path, remote.Name, password: pat);
                }
                catch
                {
                    // Ignore fetch failures - push succeeded
                }
            }

            StatusMessage = $"Pushed to {pushedRemotes.Count} remotes";
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
}
