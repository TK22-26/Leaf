using System;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;
using Leaf.Views;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial - Commit operations (revert, reset, cherry-pick, undo/redo).
/// </summary>
public partial class MainViewModel
{
    [RelayCommand]
    public async Task RevertCommitAsync(CommitInfo commit)
    {
        if (SelectedRepository == null || commit == null)
            return;

        Log.Info("Merge", $"RevertCommit: sha={commit.ShortSha} isMerge={commit.IsMerge}");

        if (commit.IsMerge)
        {
            var result = await _dialogService.ShowMessageAsync(
                "This is a merge commit.\n\nRevert using the first parent (current branch)?\n" +
                "Yes = parent 1, No = parent 2.",
                "Revert Merge Commit",
                System.Windows.MessageBoxButton.YesNoCancel);

            var parentIndex = result switch
            {
                System.Windows.MessageBoxResult.Yes => 1,
                System.Windows.MessageBoxResult.No => 2,
                _ => 0
            };

            if (parentIndex == 0)
            {
                return;
            }

            try
            {
                await BeginBusyAsync($"Reverting {commit.ShortSha} (parent {parentIndex})...");

                await _gitService.RevertMergeCommitAsync(SelectedRepository.Path, commit.Sha, parentIndex, cancellationToken: CurrentRepositoryToken);

                NotifySuccess(Models.NotificationCategory.MergeAndRebase, "Commit reverted", $"Reverted merge commit {commit.ShortSha} (parent {parentIndex}).");
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                Log.Error("Merge", "RevertMergeCommit failed", ex);
                await ReportOperationFailureAsync("Revert", ex);
            }
            finally
            {
                IsBusy = false;
            }

            return;
        }

        try
        {
            await BeginBusyAsync($"Reverting {commit.ShortSha}...");

            await _gitService.RevertCommitAsync(SelectedRepository.Path, commit.Sha, cancellationToken: CurrentRepositoryToken);

            Log.Info("Merge", $"RevertCommit: success sha={commit.ShortSha}");
            NotifySuccess(Models.NotificationCategory.MergeAndRebase, "Commit reverted", $"Reverted {commit.ShortSha}.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Merge", "RevertCommit failed", ex);
            await ReportOperationFailureAsync("Revert", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ResetCurrentBranchToCommitAsync(ResetCurrentBranchRequest request)
    {
        if (SelectedRepository == null || request?.Commit == null)
            return;

        var branchName = SelectedRepository.CurrentBranch;

        if (string.IsNullOrWhiteSpace(branchName) || SelectedRepository.IsDetachedHead)
        {
            NotifyWarning(Models.NotificationCategory.BranchAdmin, "Cannot reset", "No branch is checked out.");
            return;
        }

        var (message, title) = request.Mode switch
        {
            GitResetMode.Soft => (
                $"Move {branchName} to {request.Commit.ShortSha} and keep all changes staged.\n\n{request.Commit.MessageShort}",
                "Reset Branch (Soft)"),
            GitResetMode.Mixed => (
                $"Move {branchName} to {request.Commit.ShortSha} and keep changes in your working directory as unstaged.\n\n{request.Commit.MessageShort}",
                "Reset Branch (Mixed)"),
            GitResetMode.Hard => (
                $"Move {branchName} to {request.Commit.ShortSha} and discard all staged and unstaged tracked changes. Untracked files will not be removed.\n\n{request.Commit.MessageShort}",
                "Hard Reset Branch"),
            _ => throw new ArgumentOutOfRangeException()
        };

        if (!await _dialogService.ShowConfirmationAsync(message, title))
            return;

        try
        {
            var modeLabel = request.Mode.ToString().ToLower();
            await BeginBusyAsync($"Resetting {branchName} to {request.Commit.ShortSha} ({modeLabel})...");

            await _gitService.ResetCurrentBranchToCommitAsync(
                SelectedRepository.Path, request.Commit.Sha, request.Mode, cancellationToken: CurrentRepositoryToken);

            NotifySuccess(Models.NotificationCategory.BranchAdmin, "Branch reset", $"Reset {branchName} to {request.Commit.ShortSha} ({modeLabel}).");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Reset", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task CheckoutCommitAsync(CommitInfo commit)
    {
        if (commit == null || SelectedRepository == null)
            return;

        await BeginBusyAsync($"Checking out commit {commit.ShortSha}...");

        try
        {
            await _gitService.CheckoutCommitAsync(SelectedRepository.Path, commit.Sha, cancellationToken: CurrentRepositoryToken);

            // Refresh the repo info to update detached HEAD state
            var info = await _gitService.GetRepositoryInfoFastAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
            SelectedRepository.CurrentBranch = info.CurrentBranch;
            SelectedRepository.IsDetachedHead = info.IsDetachedHead;
            SelectedRepository.DetachedHeadSha = info.DetachedHeadSha;

            NotifySuccess(Models.NotificationCategory.BranchCheckout, "Commit checked out", $"Now at {commit.ShortSha} (detached HEAD).");
            await RefreshAsync();
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

    [RelayCommand]
    public void CopyCommitSha(CommitInfo commit)
    {
        if (commit == null)
            return;

        _clipboardService.SetText(commit.Sha);
        NotifyInfo(Models.NotificationCategory.MergeAndRebase, "SHA copied", $"Copied {commit.ShortSha} to clipboard.");
    }

    [RelayCommand]
    public async Task CherryPickCommitAsync(CommitInfo commit)
    {
        if (commit == null || SelectedRepository == null)
            return;

        Log.Info("Merge", $"CherryPickCommit: sha={commit.ShortSha}");
        await BeginBusyAsync($"Cherry-picking {commit.ShortSha}...");

        try
        {
            var result = await _gitService.CherryPickAsync(SelectedRepository.Path, commit.Sha, cancellationToken: CurrentRepositoryToken);
            if (result.Success)
            {
                Log.Info("Merge", "CherryPickCommit: success");
                NotifySuccess(Models.NotificationCategory.MergeAndRebase, "Cherry-picked", $"Applied {commit.ShortSha} to current branch.");
                await RefreshAsync();
            }
            else if (result.HasConflicts)
            {
                Log.Warn("Merge", "CherryPickCommit: conflicts detected");
                // Refresh first so MergeStatusView populates in the right
                // pane, then warn — without the toast the user sees the
                // command "do nothing" and has to guess where to look.
                await RefreshAsync();
                NotifyWarning(
                    Models.NotificationCategory.MergeAndRebase,
                    "Cherry-pick has conflicts",
                    $"{commit.ShortSha} could not apply cleanly. Resolve the conflicts in the merge panel.");
            }
            else
            {
                Log.Error("Merge", $"CherryPickCommit: {result.ErrorMessage}");
                await ReportOperationFailureAsync("Cherry-pick", result.ErrorMessage ?? "unknown error");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Merge", "CherryPickCommit failed", ex);
            await ReportOperationFailureAsync("Cherry-pick", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task CompareCommitToWorkingDirectoryAsync(CommitInfo commit)
    {
        if (commit == null || SelectedRepository == null || DiffViewerViewModel == null)
            return;

        DiffViewerViewModel.IsLoading = true;
        IsDiffViewerVisible = true;

        try
        {
            var diffText = await _gitService.GetCommitToWorkingTreeDiffAsync(SelectedRepository.Path, commit.Sha, cancellationToken: CurrentRepositoryToken);
            if (string.IsNullOrWhiteSpace(diffText))
            {
                NotifyInfo(Models.NotificationCategory.MergeAndRebase, "No differences", "Commit and working directory are identical.");
                IsDiffViewerVisible = false;
                return;
            }

            var diffResult = BuildUnifiedDiffResult(diffText, $"Working Directory vs {commit.ShortSha}");
            DiffViewerViewModel.RepositoryPath = SelectedRepository.Path;
            DiffViewerViewModel.LoadDiff(diffResult);
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Compare", ex);
            IsDiffViewerVisible = false;
        }
        finally
        {
            DiffViewerViewModel.IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task CreateTagAtCommitAsync(CommitInfo commit)
    {
        if (commit == null || SelectedRepository == null)
            return;

        var dialog = new CreateTagDialog();
        if (!await _dialogService.ShowDialogAsync(dialog))
            return;

        try
        {
            await BeginBusyAsync($"Creating tag '{dialog.TagName}'...");
            await _gitService.CreateTagAsync(SelectedRepository.Path, dialog.TagName, dialog.TagMessage, commit.Sha, cancellationToken: CurrentRepositoryToken);
            NotifySuccess(Models.NotificationCategory.BranchAdmin, "Tag created", $"Tagged {commit.ShortSha} as '{dialog.TagName}'.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Create tag", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Undo last commit (soft reset).
    /// </summary>
    [RelayCommand]
    public async Task UndoAsync()
    {
        if (SelectedRepository == null) return;

        try
        {
            await BeginBusyAsync("Undoing last commit...");

            var success = await _gitService.UndoCommitAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
            if (success)
            {
                NotifySuccess(Models.NotificationCategory.MergeAndRebase, "Commit undone", "Changes preserved in working directory.");
                await RefreshAsync();
            }
            else
            {
                NotifyWarning(Models.NotificationCategory.MergeAndRebase, "Cannot undo", "Commit already pushed or no parent commit.");
            }
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Undo", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Redo the last undone commit (soft reset to ORIG_HEAD).
    /// </summary>
    [RelayCommand]
    public async Task Redo()
    {
        if (SelectedRepository == null) return;

        try
        {
            await BeginBusyAsync("Redoing last undone commit...");

            var success = await _gitService.RedoCommitAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
            if (success)
            {
                NotifySuccess(Models.NotificationCategory.MergeAndRebase, "Commit redone", "Restored last undone commit.");
                await RefreshAsync();
            }
            else
            {
                NotifyInfo(Models.NotificationCategory.MergeAndRebase, "Nothing to redo", "No undone commit to restore.");
            }
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Redo", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadCommitDetails(CommitInfo? commit)
    {
        if (CommitDetailViewModel != null && SelectedRepository != null && commit != null)
        {
            CommitDetailViewModel.LoadCommitAsync(SelectedRepository.Path, commit.Sha)
                .FireAndForget(nameof(CommitDetailViewModel.LoadCommitAsync), isUserAction: true);
        }
    }
}
