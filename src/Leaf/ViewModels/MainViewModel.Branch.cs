using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Utils;

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
                    var credentialKey = CredentialHelper.GetCredentialKeyForUrl(remoteUrl);
                    if (!string.IsNullOrEmpty(credentialKey))
                    {
                        pat = _credentialService.GetPat(credentialKey);
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
            await BeginBusyAsync($"Checking out {branch.Name}...");

            // Check if this branch is already checked out in another worktree
            var branchNameToCheck = branch.IsRemote
                ? branch.Name[(branch.Name.IndexOf('/') + 1)..]
                : branch.Name;

            // Use path comparison instead of IsCurrent flag to identify the current worktree,
            // because IsCurrent can be stale after switching between repos
            var normalizedRepoPath = Path.GetFullPath(SelectedRepository.Path);
            var worktreeWithBranch = SelectedRepository.Worktrees
                .FirstOrDefault(wt =>
                    !string.Equals(Path.GetFullPath(wt.Path), normalizedRepoPath, StringComparison.OrdinalIgnoreCase) &&
                    wt.Exists &&
                    !string.IsNullOrEmpty(wt.BranchName) &&
                    string.Equals(wt.BranchName, branchNameToCheck, StringComparison.OrdinalIgnoreCase));

            if (worktreeWithBranch != null)
            {
                IsBusy = false;
                await SwitchToWorktreeAsync(worktreeWithBranch);
                return;
            }

            string branchName;
            BranchInfo? localBranch = null;

            if (branch.IsRemote)
            {
                // Extract local branch name (e.g., "origin/main" → "main")
                var remoteName = branch.RemoteName ?? "origin";
                var localBranchName = branch.Name.StartsWith($"{remoteName}/", StringComparison.OrdinalIgnoreCase)
                    ? branch.Name[(remoteName.Length + 1)..]
                    : branch.Name;

                // Check if local/remote branches exist and where they point
                var branches = await _gitService.GetBranchesAsync(SelectedRepository.Path);
                localBranch = branches.FirstOrDefault(b =>
                    !b.IsRemote && string.Equals(b.Name, localBranchName, StringComparison.OrdinalIgnoreCase));
                var remoteBranchName = $"{remoteName}/{localBranchName}";
                var remoteBranch = branches.FirstOrDefault(b =>
                    b.IsRemote && string.Equals(b.Name, remoteBranchName, StringComparison.OrdinalIgnoreCase));
                var remoteTipSha = remoteBranch?.TipSha;

                if (localBranch != null &&
                    !string.IsNullOrWhiteSpace(remoteTipSha) &&
                    !string.Equals(localBranch.TipSha, remoteTipSha, StringComparison.OrdinalIgnoreCase))
                {
                    // Local exists but is at different commit - checkout remote's commit (detached HEAD)
                    StatusMessage = $"Checking out {branch.Name}...";
                    await _gitService.CheckoutCommitAsync(SelectedRepository.Path, remoteTipSha);

                    var info = await _gitService.GetRepositoryInfoAsync(SelectedRepository.Path);
                    SelectedRepository.CurrentBranch = info.CurrentBranch;
                    SelectedRepository.IsDetachedHead = info.IsDetachedHead;
                    SelectedRepository.DetachedHeadSha = info.DetachedHeadSha;
                    SelectedRepository.IsMergeInProgress = info.IsMergeInProgress;
                    SelectedRepository.MergingBranch = info.MergingBranch;
                    SelectedRepository.ConflictCount = info.ConflictCount;

                    // Reload branches to update current indicator
                    SelectedRepository.BranchesLoaded = false;
                    await LoadBranchesForRepoAsync(SelectedRepository);

                    // Refresh git graph and select the checked out commit
                    if (GitGraphViewModel != null)
                    {
                        await GitGraphViewModel.LoadRepositoryAsync(SelectedRepository.Path);
                        GitGraphViewModel.SelectCommitBySha(remoteTipSha);
                    }

                    StatusMessage = $"Checked out {branch.Name} (detached HEAD)";
                    IsBusy = false;
                    return;
                }

                // Local exists at same commit, OR no local exists
                // → use existing logic to switch to / create local branch
                branchName = localBranchName;
            }
            else
            {
                branchName = branch.Name;
            }

            await _gitService.CheckoutAsync(SelectedRepository.Path, branchName, allowConflicts: true);

            // Refresh the repo info
            var repoInfo = await _gitService.GetRepositoryInfoAsync(SelectedRepository.Path);
            SelectedRepository.CurrentBranch = repoInfo.CurrentBranch;
            SelectedRepository.IsDetachedHead = repoInfo.IsDetachedHead;
            SelectedRepository.DetachedHeadSha = repoInfo.DetachedHeadSha;
            SelectedRepository.IsMergeInProgress = repoInfo.IsMergeInProgress;
            SelectedRepository.MergingBranch = repoInfo.MergingBranch;
            SelectedRepository.ConflictCount = repoInfo.ConflictCount;

            // Reload branches to update current indicator
            SelectedRepository.BranchesLoaded = false;
            await LoadBranchesForRepoAsync(SelectedRepository);

            // Refresh git graph and select the branch's tip commit (or requested commit)
            if (GitGraphViewModel != null)
            {
                await GitGraphViewModel.LoadRepositoryAsync(SelectedRepository.Path);
                var selectSha = !string.IsNullOrWhiteSpace(branch.TipSha)
                    ? branch.TipSha
                    : localBranch?.TipSha ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(selectSha))
                {
                    GitGraphViewModel.SelectCommitBySha(selectSha);
                }
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
            SelectedRepository.IsDetachedHead = info.IsDetachedHead;
            SelectedRepository.DetachedHeadSha = info.DetachedHeadSha;
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
    /// Delete a tag locally and from the remote.
    /// </summary>
    [RelayCommand]
    public async Task DeleteTagAsync(TagInfo tag)
    {
        if (SelectedRepository == null || tag == null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            $"Delete tag '{tag.Name}'?\n\nThis will delete the tag locally and from the remote origin.\nThis cannot be undone.",
            "Delete Tag");

        if (!confirmed) return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Deleting tag {tag.Name}...";

            // Delete locally first
            await _gitService.DeleteTagAsync(SelectedRepository.Path, tag.Name);

            // Also delete from remote origin (ignore errors if tag doesn't exist on remote)
            try
            {
                StatusMessage = $"Deleting tag {tag.Name} from remote...";
                await _gitService.DeleteRemoteTagAsync(SelectedRepository.Path, tag.Name, "origin");
            }
            catch
            {
                // Remote deletion may fail if tag doesn't exist on remote - that's OK
            }

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
