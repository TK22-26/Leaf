using System;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;
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
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
            var existingNames = remotes.Select(r => r.Name);

            var dialog = new RemoteDialog(existingNames);

            if (!await _dialogService.ShowDialogAsync(dialog)) return;

            await BeginBusyAsync($"Adding remote '{dialog.RemoteName}'...");

            await _gitService.AddRemoteAsync(
                SelectedRepository.Path,
                dialog.RemoteName,
                dialog.FetchUrl,
                dialog.PushUrl, cancellationToken: CurrentRepositoryToken);

            // Refresh branches to show the new remote
            SelectedRepository.BranchesLoaded = false;
            await LoadBranchesForRepoAsync(SelectedRepository, forceReload: true);

            NotifySuccess(Models.NotificationCategory.RemoteConfig, "Remote added", $"Added '{dialog.RemoteName}' ({dialog.FetchUrl}).");
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Add remote", ex);
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
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
            var remoteInfo = remotes.FirstOrDefault(r => r.Name == remote.Name);

            if (remoteInfo == null)
            {
                NotifyWarning(Models.NotificationCategory.RemoteConfig, "Remote not found", $"Remote '{remote.Name}' no longer exists in this repository.");
                return;
            }

            var existingNames = remotes.Select(r => r.Name);
            var dialog = new RemoteDialog(existingNames, remoteInfo.Name, remoteInfo.Url, remoteInfo.PushUrl);

            if (!await _dialogService.ShowDialogAsync(dialog)) return;

            await BeginBusyAsync($"Updating remote '{remote.Name}'...");

            // Check if name changed - rename first
            if (!string.Equals(remote.Name, dialog.RemoteName, StringComparison.OrdinalIgnoreCase))
            {
                await _gitService.RenameRemoteAsync(SelectedRepository.Path, remote.Name, dialog.RemoteName, cancellationToken: CurrentRepositoryToken);
            }

            // Update URLs
            var currentRemoteName = dialog.RemoteName; // Use new name if renamed
            await _gitService.SetRemoteUrlAsync(SelectedRepository.Path, currentRemoteName, dialog.FetchUrl, isPushUrl: false, cancellationToken: CurrentRepositoryToken);

            if (dialog.PushUrl != null)
            {
                await _gitService.SetRemoteUrlAsync(SelectedRepository.Path, currentRemoteName, dialog.PushUrl, isPushUrl: true, cancellationToken: CurrentRepositoryToken);
            }

            // Refresh branches
            SelectedRepository.BranchesLoaded = false;
            await LoadBranchesForRepoAsync(SelectedRepository, forceReload: true);

            NotifySuccess(Models.NotificationCategory.RemoteConfig, "Remote updated", $"Updated remote '{currentRemoteName}'.");
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Edit remote", ex);
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
            await BeginBusyAsync($"Removing remote '{remoteName}'...");

            await _gitService.RemoveRemoteAsync(SelectedRepository.Path, remoteName, cancellationToken: CurrentRepositoryToken);

            // Refresh branches
            SelectedRepository.BranchesLoaded = false;
            await LoadBranchesForRepoAsync(SelectedRepository, forceReload: true);

            NotifySuccess(Models.NotificationCategory.RemoteConfig, "Remote removed", $"Removed remote '{remoteName}'.");
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Remove remote", ex);
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
            await _gitService.SetConfigAsync(SelectedRepository.Path, "leaf.defaultremote", remoteName, cancellationToken: CurrentRepositoryToken);

            // Refresh branches to update the default indicator
            SelectedRepository.BranchesLoaded = false;
            await LoadBranchesForRepoAsync(SelectedRepository, forceReload: true);

            NotifySuccess(Models.NotificationCategory.RemoteConfig, "Default remote set", $"'{remoteName}' is now the default remote for push.");
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Set default remote", ex);
        }
    }

    /// <summary>
    /// Copy a remote's URL to the clipboard.
    /// </summary>
    [RelayCommand]
    public async Task CopyRemoteUrlAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return;

        try
        {
            Clipboard.SetText(url);
            NotifyInfo(Models.NotificationCategory.RemoteConfig, "URL copied", "Remote URL copied to clipboard.");
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            // WPF Clipboard uses OLE which occasionally throws
            // CLIPBRD_E_CANT_OPEN when another process is holding the
            // clipboard open. Surface the failure through the policy
            // pipeline so users see both a toast and the status-bar line,
            // and log the underlying HRESULT for diagnostics.
            await ReportOperationFailureAsync("Copy URL", ex);
            Log.Warn("Remote", $"Clipboard.SetText failed: {ex.Message}");
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
            await BeginBusyAsync("Fetching from all remotes...");
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);

            var successCount = 0;
            foreach (var remote in remotes)
            {
                // Resolve credential key only when Leaf has a stored PAT;
                // otherwise git uses its default helpers (GCM).
                var credentialKey = _credentialService.ResolveActiveCredentialKey(remote.Url);

                try
                {
                    await _gitService.FetchAsync(SelectedRepository.Path, remote.Name, credentialKey: credentialKey, cancellationToken: CurrentRepositoryToken);
                    successCount++;
                }
                catch (Exception ex)
                {
                    // Log but continue with other remotes
                    Log.Error("Remote", $"Fetch failed for {remote.Name}", ex);
                }
            }

            NotifySuccess(Models.NotificationCategory.SyncOperations, "Fetch complete", $"Fetched from {successCount} of {remotes.Count} remotes.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Fetch all", ex);
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
            var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);

            if (remotes.Count <= 1)
            {
                // Single remote - use existing push behavior
                await PushAsync();
                return;
            }

            // Multiple remotes - show selection dialog
            var defaultRemote = await _gitService.GetConfigAsync(SelectedRepository.Path, "leaf.defaultremote", cancellationToken: CurrentRepositoryToken) ?? "origin";

            var dialog = new PushDialog(SelectedRepository.CurrentBranch, remotes, defaultRemote);

            if (!await _dialogService.ShowDialogAsync(dialog)) return;

            await BeginBusyAsync("Pushing to selected remotes...");
            var selectedRemotes = dialog.SelectedRemoteNames.ToList();
            var pushedRemotes = new List<(RemoteInfo remote, string? credentialKey)>();
            var failedMessages = new List<string>();

            foreach (var remoteName in selectedRemotes)
            {
                // Resolve credential key from the remote URL only when a PAT
                // is stored; otherwise rely on GCM fallback.
                var remoteInfo = remotes.FirstOrDefault(r => r.Name == remoteName);
                var credentialKey = _credentialService.ResolveActiveCredentialKey(remoteInfo?.Url);

                try
                {
                    await _gitService.PushAsync(SelectedRepository.Path, remoteName, credentialKey, cancellationToken: CurrentRepositoryToken);
                    if (remoteInfo != null)
                    {
                        pushedRemotes.Add((remoteInfo, credentialKey));
                    }
                }
                catch (Exception ex)
                {
                    failedMessages.Add($"{remoteName}: {ex.Message}");
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
                    // Post-push fetch is cosmetic (updating local refs view);
                    // the push itself succeeded. Log so persistent refresh
                    // failures are diagnosable.
                    Log.Info("Remote", $"Post-push fetch {remote.Name} failed: {ex.Message}");
                }
            }

            if (failedMessages.Count > 0)
            {
                var errorDetail = string.Join("\n", failedMessages);
                await _dialogService.ShowErrorToastAsync(
                    $"Push failed for {failedMessages.Count} remote(s):\n\n{errorDetail}",
                    "Push Failed");
            }
            else
            {
                NotifySuccess(Models.NotificationCategory.SyncOperations, "Push complete", $"Pushed to {pushedRemotes.Count} remote{(pushedRemotes.Count == 1 ? "" : "s")}.");
            }

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
}
