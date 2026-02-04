using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial - Branch operations (create, delete, checkout).
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Create a new branch.
    /// </summary>
    [RelayCommand]
    public void CreateBranch()
    {
        if (SelectedRepository == null) return;

        // Show floating branch input
        _pendingBranchBaseSha = null;
        _pendingRenameBranchName = null;
        _isRenameBranchInput = false;
        NewBranchName = string.Empty;
        BranchInputActionText = "Create";
        BranchInputPlaceholder = "Branch name...";
        IsBranchInputVisible = true;
    }

    [RelayCommand]
    public void CreateBranchAtCommit(CommitInfo commit)
    {
        if (SelectedRepository == null || commit == null)
            return;

        _pendingBranchBaseSha = commit.Sha;
        _pendingRenameBranchName = null;
        _isRenameBranchInput = false;
        NewBranchName = string.Empty;
        BranchInputActionText = "Create";
        BranchInputPlaceholder = "Branch name...";
        IsBranchInputVisible = true;
    }

    [RelayCommand]
    public void CreateBranchAtBranch(BranchInfo branch)
    {
        if (SelectedRepository == null || branch == null)
            return;

        _pendingBranchBaseSha = branch.TipSha;
        _pendingRenameBranchName = null;
        _isRenameBranchInput = false;
        NewBranchName = string.Empty;
        BranchInputActionText = "Create";
        BranchInputPlaceholder = "Branch name...";
        IsBranchInputVisible = true;
    }

    [RelayCommand]
    public void RenameBranch(BranchInfo branch)
    {
        if (SelectedRepository == null || branch == null || branch.IsRemote)
            return;

        _pendingBranchBaseSha = null;
        _pendingRenameBranchName = branch.Name;
        _isRenameBranchInput = true;
        NewBranchName = branch.Name;
        BranchInputActionText = "Rename";
        BranchInputPlaceholder = "New branch name...";
        IsBranchInputVisible = true;
    }

    [RelayCommand]
    public async Task ConfirmCreateBranchAsync()
    {
        if (SelectedRepository == null || string.IsNullOrWhiteSpace(NewBranchName))
            return;

        var branchName = NewBranchName.Trim();
        IsBranchInputVisible = false;
        NewBranchName = string.Empty;

        try
        {
            IsBusy = true;
            if (_isRenameBranchInput && !string.IsNullOrWhiteSpace(_pendingRenameBranchName))
            {
                if (string.Equals(branchName, _pendingRenameBranchName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                StatusMessage = $"Renaming branch '{_pendingRenameBranchName}'...";
                await _gitService.RenameBranchAsync(SelectedRepository.Path, _pendingRenameBranchName, branchName);
                StatusMessage = $"Renamed branch to '{branchName}'";
                SelectedRepository.BranchesLoaded = false;
                await RefreshAsync();
            }
            else if (!string.IsNullOrWhiteSpace(_pendingBranchBaseSha))
            {
                StatusMessage = $"Creating branch '{branchName}' at {_pendingBranchBaseSha[..7]}...";
                await _gitService.CreateBranchAtCommitAsync(SelectedRepository.Path, branchName, _pendingBranchBaseSha);
                StatusMessage = $"Created and checked out branch '{branchName}'";
            }
            else
            {
                StatusMessage = $"Creating branch '{branchName}'...";
                await _gitService.CreateBranchAsync(SelectedRepository.Path, branchName);
                StatusMessage = $"Created and checked out branch '{branchName}'";
            }
            SelectedRepository.BranchesLoaded = false;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = _isRenameBranchInput ? $"Rename branch failed: {ex.Message}" : $"Create branch failed: {ex.Message}";
        }
        finally
        {
            _pendingBranchBaseSha = null;
            _pendingRenameBranchName = null;
            _isRenameBranchInput = false;
            BranchInputActionText = "Create";
            BranchInputPlaceholder = "Branch name...";
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void CancelBranchInput()
    {
        IsBranchInputVisible = false;
        NewBranchName = string.Empty;
        _pendingBranchBaseSha = null;
        _pendingRenameBranchName = null;
        _isRenameBranchInput = false;
        BranchInputActionText = "Create";
        BranchInputPlaceholder = "Branch name...";
    }

    /// <summary>
    /// Delete a branch.
    /// </summary>
    [RelayCommand]
    public async Task DeleteBranchAsync(BranchInfo branch)
    {
        if (SelectedRepository == null || branch == null)
            return;

        if (!await ConfirmBranchDeletionAsync(branch))
            return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Deleting branch {branch.Name}...";

            if (branch.IsRemote)
            {
                var remoteName = branch.RemoteName ?? "origin";
                var branchName = GetRemoteBranchShortName(branch.Name, remoteName);

                // Get credentials for remote operations (same pattern as Push/Fetch)
                string? pat = null;
                var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path);
                var remoteUrl = remotes.FirstOrDefault(r => r.Name == remoteName)?.Url;
                if (!string.IsNullOrEmpty(remoteUrl))
                {
                    try
                    {
                        var host = new Uri(remoteUrl).Host;
                        var credentialKey = GetCredentialKeyForHost(host);
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

                await _gitService.DeleteRemoteBranchAsync(SelectedRepository.Path, remoteName, branchName, password: pat);
            }
            else
            {
                // If deleting current branch, switch to another branch first
                if (branch.IsCurrent)
                {
                    var switchTarget = await GetBranchToSwitchToAsync(branch.Name);
                    if (switchTarget != null)
                    {
                        StatusMessage = $"Switching to {switchTarget}...";
                        await _gitService.CheckoutAsync(SelectedRepository.Path, switchTarget);
                    }
                }

                await _gitService.DeleteBranchAsync(SelectedRepository.Path, branch.Name, force: false);
            }

            StatusMessage = $"Deleted branch {branch.Name}";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            if (!branch.IsRemote && await ConfirmForceDeleteAsync(branch, ex.Message))
            {
                try
                {
                    await _gitService.DeleteBranchAsync(SelectedRepository.Path, branch.Name, force: true);
                    StatusMessage = $"Force deleted branch {branch.Name}";
                    await RefreshAsync();
                    return;
                }
                catch (Exception forceEx)
                {
                    StatusMessage = $"Delete branch failed: {forceEx.Message}";
                }
            }
            else
            {
                StatusMessage = $"Delete branch failed: {ex.Message}";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task PullBranchFastForwardAsync(BranchInfo branch)
    {
        if (SelectedRepository == null || branch == null)
            return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Pulling {branch.Name}...";

            if (branch.IsRemote)
            {
                var remoteNameValue = branch.RemoteName ?? "origin";
                var localName = branch.Name.StartsWith($"{remoteNameValue}/", StringComparison.OrdinalIgnoreCase)
                    ? branch.Name[(remoteNameValue.Length + 1)..]
                    : branch.Name;

                await _gitService.PullBranchFastForwardAsync(
                    SelectedRepository.Path,
                    localName,
                    remoteNameValue,
                    branch.Name,
                    isCurrentBranch: false);

                StatusMessage = $"Created local {localName} from {branch.Name}";
                await RefreshAsync();
                return;
            }

            var (remoteName, remoteBranchName) = await ResolveRemoteTargetAsync(branch);
            await _gitService.PullBranchFastForwardAsync(
                SelectedRepository.Path,
                branch.Name,
                remoteName,
                remoteBranchName,
                branch.IsCurrent);

            StatusMessage = $"Pulled {branch.Name}";
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

    [RelayCommand]
    public async Task PushBranchAsync(BranchInfo branch)
    {
        if (SelectedRepository == null || branch == null || branch.IsRemote)
            return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Pushing {branch.Name}...";

            var (remoteName, remoteBranchName) = await ResolveRemoteTargetAsync(branch);
            await _gitService.PushBranchAsync(
                SelectedRepository.Path,
                branch.Name,
                remoteName,
                remoteBranchName,
                branch.IsCurrent);

            StatusMessage = $"Pushed {branch.Name}";
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

    [RelayCommand]
    public async Task SetUpstreamAsync(BranchInfo branch)
    {
        if (SelectedRepository == null || branch == null || branch.IsRemote)
            return;

        if (!string.IsNullOrWhiteSpace(branch.TrackingBranchName))
            return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Setting upstream for {branch.Name}...";

            var (remoteName, remoteBranchName) = await ResolveRemoteTargetAsync(branch);
            await _gitService.SetUpstreamAsync(SelectedRepository.Path, branch.Name, remoteName, remoteBranchName);

            StatusMessage = $"Upstream set for {branch.Name}";
            SelectedRepository.BranchesLoaded = false;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Set upstream failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Checkout a branch.
    /// </summary>
    [RelayCommand]
    public async Task CheckoutBranchAsync(BranchInfo branch)
    {
        if (SelectedRepository == null) return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Checking out {branch.Name}...";

            // For remote branches, extract the name after origin/
            var branchName = branch.IsRemote && branch.Name.StartsWith("origin/")
                ? branch.Name["origin/".Length..]
                : branch.Name;

            await _gitService.CheckoutAsync(SelectedRepository.Path, branchName, allowConflicts: true);

            // Refresh the repo info
            var info = await _gitService.GetRepositoryInfoAsync(SelectedRepository.Path);
            SelectedRepository.CurrentBranch = info.CurrentBranch;
            SelectedRepository.IsMergeInProgress = info.IsMergeInProgress;
            SelectedRepository.MergingBranch = info.MergingBranch;
            SelectedRepository.ConflictCount = info.ConflictCount;

            // Reload branches to update current indicator
            SelectedRepository.BranchesLoaded = false;
            await LoadBranchesForRepoAsync(SelectedRepository);

            // Refresh git graph
            if (GitGraphViewModel != null)
            {
                await GitGraphViewModel.LoadRepositoryAsync(SelectedRepository.Path);
            }

            if (SelectedRepository.ConflictCount > 0)
            {
                if (string.IsNullOrEmpty(SelectedRepository.MergingBranch))
                {
                    SelectedRepository.MergingBranch = branchName;
                }

                StatusMessage = "Checkout has conflicts - resolve to complete";
                await RefreshMergeConflictResolutionAsync();
            }
            else
            {
                StatusMessage = $"Checked out {branchName}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Checkout failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Checkout a tag (detached HEAD).
    /// </summary>
    [RelayCommand]
    public async Task CheckoutTagAsync(TagInfo tag)
    {
        if (SelectedRepository == null || tag == null) return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Checking out tag {tag.Name}...";

            await _gitService.CheckoutCommitAsync(SelectedRepository.Path, tag.TargetSha);

            // Refresh the repo info
            var info = await _gitService.GetRepositoryInfoAsync(SelectedRepository.Path);
            SelectedRepository.CurrentBranch = info.CurrentBranch;
            SelectedRepository.IsMergeInProgress = info.IsMergeInProgress;
            SelectedRepository.MergingBranch = info.MergingBranch;
            SelectedRepository.ConflictCount = info.ConflictCount;

            // Reload branches to update current indicator
            SelectedRepository.BranchesLoaded = false;
            await LoadBranchesForRepoAsync(SelectedRepository);

            // Refresh git graph
            if (GitGraphViewModel != null)
            {
                await GitGraphViewModel.LoadRepositoryAsync(SelectedRepository.Path);
            }

            StatusMessage = $"Checked out tag {tag.Name} (detached HEAD)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Checkout tag failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Delete a tag.
    /// </summary>
    [RelayCommand]
    public async Task DeleteTagAsync(TagInfo tag)
    {
        if (SelectedRepository == null || tag == null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            $"Delete tag '{tag.Name}'?\n\nThis cannot be undone.",
            "Delete Tag");

        if (!confirmed) return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Deleting tag {tag.Name}...";

            await _gitService.DeleteTagAsync(SelectedRepository.Path, tag.Name);

            StatusMessage = $"Deleted tag {tag.Name}";
            await LoadBranchesForRepoAsync(SelectedRepository, forceReload: true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Delete tag failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
