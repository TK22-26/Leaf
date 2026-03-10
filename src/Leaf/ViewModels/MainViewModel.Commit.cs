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

        System.Diagnostics.Debug.WriteLine($"[MERGE][OPS] RevertCommit: sha={commit.ShortSha} isMerge={commit.IsMerge}");

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
                IsBusy = true;
                StatusMessage = $"Reverting {commit.ShortSha} (parent {parentIndex})...";

                await _gitService.RevertMergeCommitAsync(SelectedRepository.Path, commit.Sha, parentIndex);

                StatusMessage = $"Reverted {commit.ShortSha}";
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MERGE][ERROR] RevertMergeCommit: {ex.Message}");
                StatusMessage = $"Revert failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }

            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"Reverting {commit.ShortSha}...";

            await _gitService.RevertCommitAsync(SelectedRepository.Path, commit.Sha);

            System.Diagnostics.Debug.WriteLine($"[MERGE][OPS] RevertCommit: success sha={commit.ShortSha}");
            StatusMessage = $"Reverted {commit.ShortSha}";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MERGE][ERROR] RevertCommit: {ex.Message}");
            StatusMessage = $"Revert failed: {ex.Message}";
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
            StatusMessage = "Cannot reset: no branch is checked out";
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
            IsBusy = true;
            var modeLabel = request.Mode.ToString().ToLower();
            StatusMessage = $"Resetting {branchName} to {request.Commit.ShortSha} ({modeLabel})...";

            await _gitService.ResetCurrentBranchToCommitAsync(
                SelectedRepository.Path, request.Commit.Sha, request.Mode);

            StatusMessage = $"Reset {branchName} to {request.Commit.ShortSha} ({modeLabel})";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Reset failed: {ex.Message}";
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

        IsBusy = true;
        StatusMessage = $"Checking out commit {commit.ShortSha}...";

        try
        {
            await _gitService.CheckoutCommitAsync(SelectedRepository.Path, commit.Sha);

            // Refresh the repo info to update detached HEAD state
            var info = await _gitService.GetRepositoryInfoAsync(SelectedRepository.Path);
            SelectedRepository.CurrentBranch = info.CurrentBranch;
            SelectedRepository.IsDetachedHead = info.IsDetachedHead;
            SelectedRepository.DetachedHeadSha = info.DetachedHeadSha;

            StatusMessage = $"Checked out commit {commit.ShortSha} (detached HEAD)";
            await RefreshAsync();
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

    [RelayCommand]
    public void CopyCommitSha(CommitInfo commit)
    {
        if (commit == null)
            return;

        _clipboardService.SetText(commit.Sha);
        StatusMessage = $"Copied {commit.ShortSha} to clipboard";
    }

    [RelayCommand]
    public async Task CherryPickCommitAsync(CommitInfo commit)
    {
        if (commit == null || SelectedRepository == null)
            return;

        System.Diagnostics.Debug.WriteLine($"[MERGE][OPS] CherryPickCommit: sha={commit.ShortSha}");
        IsBusy = true;
        StatusMessage = $"Cherry-picking {commit.ShortSha}...";

        try
        {
            var result = await _gitService.CherryPickAsync(SelectedRepository.Path, commit.Sha);
            if (result.Success)
            {
                System.Diagnostics.Debug.WriteLine($"[MERGE][OPS] CherryPickCommit: success");
                StatusMessage = $"Cherry-picked {commit.ShortSha}";
                await RefreshAsync();
            }
            else if (result.HasConflicts)
            {
                System.Diagnostics.Debug.WriteLine($"[MERGE][OPS] CherryPickCommit: conflicts detected");
                StatusMessage = $"Cherry-pick has conflicts: {commit.ShortSha}";
                await RefreshAsync();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[MERGE][ERROR] CherryPickCommit: {result.ErrorMessage}");
                StatusMessage = $"Cherry-pick failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MERGE][ERROR] CherryPickCommit: {ex.Message}");
            StatusMessage = $"Cherry-pick failed: {ex.Message}";
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
            var diffText = await _gitService.GetCommitToWorkingTreeDiffAsync(SelectedRepository.Path, commit.Sha);
            if (string.IsNullOrWhiteSpace(diffText))
            {
                StatusMessage = "No differences between commit and working directory";
                IsDiffViewerVisible = false;
                return;
            }

            var diffResult = BuildUnifiedDiffResult(diffText, $"Working Directory vs {commit.ShortSha}");
            DiffViewerViewModel.RepositoryPath = SelectedRepository.Path;
            DiffViewerViewModel.LoadDiff(diffResult);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Compare failed: {ex.Message}";
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

        var dialog = new CreateTagDialog
        {
            Owner = _ownerWindow
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Creating tag '{dialog.TagName}'...";
            await _gitService.CreateTagAsync(SelectedRepository.Path, dialog.TagName, dialog.TagMessage, commit.Sha);
            StatusMessage = $"Created tag '{dialog.TagName}'";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Create tag failed: {ex.Message}";
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
            IsBusy = true;
            StatusMessage = "Undoing last commit...";

            var success = await _gitService.UndoCommitAsync(SelectedRepository.Path);
            if (success)
            {
                StatusMessage = "Commit undone (changes preserved in working directory)";
                await RefreshAsync();
            }
            else
            {
                StatusMessage = "Cannot undo: commit already pushed or no parent commit";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Undo failed: {ex.Message}";
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
            IsBusy = true;
            StatusMessage = "Redoing last undone commit...";

            var success = await _gitService.RedoCommitAsync(SelectedRepository.Path);
            if (success)
            {
                StatusMessage = "Commit redone";
                await RefreshAsync();
            }
            else
            {
                StatusMessage = "Nothing to redo";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Redo failed: {ex.Message}";
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
            _ = CommitDetailViewModel.LoadCommitAsync(SelectedRepository.Path, commit.Sha);
        }
    }
}
