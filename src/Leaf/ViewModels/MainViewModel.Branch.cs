using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;
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
        RequestBranchCreatePopup?.Invoke(this, EventArgs.Empty);
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
        RequestBranchCreatePopup?.Invoke(this, EventArgs.Empty);
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
        RequestBranchCreatePopup?.Invoke(this, EventArgs.Empty);
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
        RequestBranchCreatePopup?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public async Task ConfirmCreateBranchAsync()
    {
        if (SelectedRepository == null || string.IsNullOrWhiteSpace(NewBranchName))
            return;

        var branchName = NewBranchName.Trim();

        // Check for duplicate branch name before closing the popup
        if (!_isRenameBranchInput)
        {
            var exists = SelectedRepository.LocalBranches
                .Any(b => string.Equals(b.Name, branchName, StringComparison.OrdinalIgnoreCase));
            if (exists)
            {
                IsBranchInputVisible = false;
                NewBranchName = string.Empty;
                await _dialogService.ShowErrorToastAsync(
                    $"A branch named '{branchName}' already exists.",
                    "Branch Already Exists");
                return;
            }
        }

        IsBranchInputVisible = false;
        NewBranchName = string.Empty;

        try
        {
            // Initial placeholder — each branch below sets its own specific
            // StatusMessage; BeginBusyAsync is used up-front to get the
            // progress bar rendering before the git call starts.
            await BeginBusyAsync("Saving branch...");
            if (_isRenameBranchInput && !string.IsNullOrWhiteSpace(_pendingRenameBranchName))
            {
                if (string.Equals(branchName, _pendingRenameBranchName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                StatusMessage = $"Renaming branch '{_pendingRenameBranchName}'...";
                await _gitService.RenameBranchAsync(SelectedRepository.Path, _pendingRenameBranchName, branchName, cancellationToken: CurrentRepositoryToken);
                StatusMessage = $"Renamed branch to '{branchName}'";
                SelectedRepository.BranchesLoaded = false;
                await RefreshAsync();
            }
            else if (!string.IsNullOrWhiteSpace(_pendingBranchBaseSha))
            {
                StatusMessage = $"Creating branch '{branchName}' at {_pendingBranchBaseSha[..7]}...";
                await _gitService.CreateBranchAtCommitAsync(SelectedRepository.Path, branchName, _pendingBranchBaseSha, cancellationToken: CurrentRepositoryToken);
                StatusMessage = $"Created and checked out branch '{branchName}'";
            }
            else
            {
                StatusMessage = $"Creating branch '{branchName}'...";
                await _gitService.CreateBranchAsync(SelectedRepository.Path, branchName, cancellationToken: CurrentRepositoryToken);
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
            await BeginBusyAsync($"Deleting branch {branch.Name}...");

            if (branch.IsRemote)
            {
                var remoteName = branch.RemoteName ?? "origin";
                var branchName = GetRemoteBranchShortName(branch.Name, remoteName);

                // Resolve credential key from the remote URL only when a PAT is
                // stored; otherwise rely on GCM fallback.
                var remotes = await _gitService.GetRemotesAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
                var remoteUrl = remotes.FirstOrDefault(r => r.Name == remoteName)?.Url;
                var credentialKey = _credentialService.ResolveActiveCredentialKey(remoteUrl);

                await _gitService.DeleteRemoteBranchAsync(SelectedRepository.Path, remoteName, branchName, credentialKey, cancellationToken: CurrentRepositoryToken);
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
                        await _gitService.CheckoutAsync(SelectedRepository.Path, switchTarget, cancellationToken: CurrentRepositoryToken);
                    }
                }

                await _gitService.DeleteBranchAsync(SelectedRepository.Path, branch.Name, force: false, cancellationToken: CurrentRepositoryToken);
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
                    await _gitService.DeleteBranchAsync(SelectedRepository.Path, branch.Name, force: true, cancellationToken: CurrentRepositoryToken);
                    StatusMessage = $"Force deleted branch {branch.Name}";
                    await RefreshAsync();
                    return;
                }
                catch (Exception forceEx)
                {
                    await ReportOperationFailureAsync("Delete branch", forceEx);
                }
            }
            else
            {
                await ReportOperationFailureAsync("Delete branch", ex);
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
            await BeginBusyAsync($"Pulling {branch.Name}...");

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
                    isCurrentBranch: false, cancellationToken: CurrentRepositoryToken);

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
                branch.IsCurrent, cancellationToken: CurrentRepositoryToken);

            StatusMessage = $"Pulled {branch.Name}";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Pull", ex);
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
            await BeginBusyAsync($"Pushing {branch.Name}...");

            var (remoteName, remoteBranchName) = await ResolveRemoteTargetAsync(branch);
            await _gitService.PushBranchAsync(
                SelectedRepository.Path,
                branch.Name,
                remoteName,
                remoteBranchName,
                branch.IsCurrent, cancellationToken: CurrentRepositoryToken);

            StatusMessage = $"Pushed {branch.Name}";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync($"Push {branch.Name}", ex);
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
            await BeginBusyAsync($"Setting upstream for {branch.Name}...");

            var (remoteName, remoteBranchName) = await ResolveRemoteTargetAsync(branch);
            await _gitService.SetUpstreamAsync(SelectedRepository.Path, branch.Name, remoteName, remoteBranchName, cancellationToken: CurrentRepositoryToken);

            StatusMessage = $"Upstream set for {branch.Name}";
            SelectedRepository.BranchesLoaded = false;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Set upstream", ex);
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
            bool needsPull = false;
            string? pullRemoteName = null;

            if (branch.IsRemote)
            {
                // Extract local branch name (e.g., "origin/main" → "main")
                var remoteName = branch.RemoteName ?? "origin";
                var localBranchName = branch.Name.StartsWith($"{remoteName}/", StringComparison.OrdinalIgnoreCase)
                    ? branch.Name[(remoteName.Length + 1)..]
                    : branch.Name;

                // Check if local/remote branches exist and where they point
                var branches = await _gitService.GetBranchesAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
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
                    // Local branch is behind (or diverged from) remote — pull after checkout
                    needsPull = true;
                    pullRemoteName = remoteName;
                }

                // Local exists at same commit, OR no local exists
                // → use existing logic to switch to / create local branch
                branchName = localBranchName;
            }
            else
            {
                branchName = branch.Name;
            }

            await _gitService.CheckoutAsync(SelectedRepository.Path, branchName, allowConflicts: true, cancellationToken: CurrentRepositoryToken);

            // Fast-forward local branch to match remote if behind
            if (needsPull)
            {
                try
                {
                    StatusMessage = $"Pulling {branchName}...";
                    await _gitService.PullBranchFastForwardAsync(
                        SelectedRepository.Path,
                        branchName,
                        pullRemoteName!,
                        branchName,
                        isCurrentBranch: true, cancellationToken: CurrentRepositoryToken);
                }
                catch (InvalidOperationException ex)
                {
                    // Fast-forward not possible (branches diverged) — checkout
                    // succeeded, user sees the diverged state in the graph.
                    // Narrowed from catch-all per plan §2.2; trace-log so the
                    // actual git message is available when debugging why a
                    // specific branch didn't fast-forward.
                    Log.Info("Branch", $"Fast-forward skipped for {branchName}: {ex.Message}");
                }
            }

            // Refresh repo info, branches, and graph in parallel (all independent git calls)
            SelectedRepository.BranchesLoaded = false;
            var repoInfoTask = _gitService.GetRepositoryInfoFastAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
            var branchesTask = LoadBranchesForRepoAsync(SelectedRepository, skipFilterApplication: true);
            var graphTask = GitGraphViewModel?.RefreshAfterCheckoutAsync(branchName, detachedHeadSha: null) ?? Task.CompletedTask;

            await Task.WhenAll(repoInfoTask, branchesTask, graphTask);

            var repoInfo = await repoInfoTask;
            SelectedRepository.CurrentBranch = repoInfo.CurrentBranch;
            SelectedRepository.IsDetachedHead = repoInfo.IsDetachedHead;
            SelectedRepository.DetachedHeadSha = repoInfo.DetachedHeadSha;
            SelectedRepository.IsMergeInProgress = repoInfo.IsMergeInProgress;
            SelectedRepository.OperationType = repoInfo.OperationType;
            SelectedRepository.MergingBranch = repoInfo.MergingBranch;
            SelectedRepository.ConflictCount = repoInfo.ConflictCount;

            // Select the branch's tip commit (or requested commit)
            if (GitGraphViewModel != null)
            {
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
            await ReportOperationFailureAsync("Checkout", ex);
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
            await BeginBusyAsync($"Checking out tag {tag.Name}...");

            await _gitService.CheckoutCommitAsync(SelectedRepository.Path, tag.TargetSha, cancellationToken: CurrentRepositoryToken);

            // Refresh repo info, branches, and graph in parallel (all independent git calls)
            SelectedRepository.BranchesLoaded = false;
            var infoTask = _gitService.GetRepositoryInfoFastAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
            var branchesTask = LoadBranchesForRepoAsync(SelectedRepository, skipFilterApplication: true);
            var graphTask = GitGraphViewModel?.RefreshAfterCheckoutAsync(newBranchName: null, detachedHeadSha: tag.TargetSha) ?? Task.CompletedTask;

            await Task.WhenAll(infoTask, branchesTask, graphTask);

            var info = await infoTask;
            SelectedRepository.CurrentBranch = info.CurrentBranch;
            SelectedRepository.IsDetachedHead = info.IsDetachedHead;
            SelectedRepository.DetachedHeadSha = info.DetachedHeadSha;
            SelectedRepository.IsMergeInProgress = info.IsMergeInProgress;
            SelectedRepository.OperationType = info.OperationType;
            SelectedRepository.MergingBranch = info.MergingBranch;
            SelectedRepository.ConflictCount = info.ConflictCount;

            StatusMessage = $"Checked out tag {tag.Name} (detached HEAD)";
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Checkout tag", ex);
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
            await BeginBusyAsync($"Deleting tag {tag.Name}...");

            // Delete locally first
            await _gitService.DeleteTagAsync(SelectedRepository.Path, tag.Name, cancellationToken: CurrentRepositoryToken);

            // Also delete from remote origin (ignore errors if tag doesn't exist on remote)
            try
            {
                StatusMessage = $"Deleting tag {tag.Name} from remote...";
                await _gitService.DeleteRemoteTagAsync(SelectedRepository.Path, tag.Name, "origin", cancellationToken: CurrentRepositoryToken);
            }
            catch (InvalidOperationException ex)
            {
                // Remote deletion may fail if the tag doesn't exist on remote —
                // expected when the tag was only ever local. Narrowed per plan
                // §2.2; trace-log so real failures (auth, network) are
                // diagnosable after the fact.
                Log.Info("Tag", $"Remote tag delete skipped for {tag.Name}: {ex.Message}");
            }

            StatusMessage = $"Deleted tag {tag.Name}";
            await LoadBranchesForRepoAsync(SelectedRepository, forceReload: true);
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Delete tag", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void CopyBranchName(BranchInfo branch)
    {
        if (branch == null) return;
        _clipboardService.SetText(branch.Name);
    }

    [RelayCommand]
    public void CopyTagName(TagInfo tag)
    {
        if (tag == null) return;
        _clipboardService.SetText(tag.Name);
    }
}
