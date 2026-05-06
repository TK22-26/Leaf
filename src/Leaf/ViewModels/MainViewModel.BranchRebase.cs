using System;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;
using Leaf.Views;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial — rebase entry point from the branch sidebar.
/// Mirrors <see cref="MainViewModel.BranchMerge"/>: a dialog picks the strategy,
/// the corresponding service verb runs against the active session, and conflicts
/// route through the existing merge editor (the editor already special-cases
/// <see cref="GitOperationType.Rebase"/> for its continue/skip/abort buttons).
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Rebase the current branch onto <paramref name="onto"/>.
    /// Bound to the "Rebase onto…" branch context menu item.
    /// </summary>
    [RelayCommand]
    public async Task RebaseBranchAsync(BranchInfo onto)
    {
        if (onto == null) return;
        if (SelectedRepository == null)
        {
            Log.Error("Rebase", "RebaseBranch: SelectedRepository is null");
            return;
        }
        if (_currentSession == null)
        {
            Log.Warn("Rebase", "RebaseBranch: no active session.");
            return;
        }

        // Refuse to start a fresh rebase while one is paused — git would
        // bail with a noisy "rebase in progress" message; this is a more
        // actionable cue.
        if (SelectedRepository.OperationType is GitOperationType.Rebase or GitOperationType.Am)
        {
            Log.Warn("Rebase", $"Refused: {SelectedRepository.OperationType} already in progress.");
            await _dialogService.ShowMessageAsync(
                "Another rebase or patch-apply is already in progress. " +
                "Continue, skip, or abort it before starting a new one.",
                "Rebase",
                MessageBoxButton.OK);
            return;
        }

        var currentBranch = SelectedRepository.CurrentBranch ?? "current branch";

        // Resolve the target name the same way merge does — a remote-only
        // branch label needs to fly as "remote/name", a local one as the
        // bare name. BranchInfo carries the metadata directly.
        var ontoName = onto.IsRemote && !string.IsNullOrEmpty(onto.RemoteName)
            ? $"{onto.RemoteName}/{onto.Name}"
            : onto.Name;

        var dialogVm = new RebaseDialogViewModel
        {
            SourceBranch = currentBranch,
            TargetBranch = ontoName,
        };

        var dialog = new RebaseDialog { DataContext = dialogVm };
        if (!await _dialogService.ShowDialogAsync(dialog))
        {
            return;
        }

        if (dialogVm.SelectedMode == RebaseMode.Interactive)
        {
            // Interactive mode rebases everything since the merge-base of
            // HEAD and the target. The existing entry point takes a "from"
            // commit SHA and replays from there onto current HEAD's parent
            // — for the branch-onto-branch case we want to rewrite each
            // commit unique to the source branch and land on `ontoName`,
            // which is exactly what `git rebase -i <onto>` does.
            await RunInteractiveRebaseOntoAsync(ontoName, dialogVm.Autosquash, dialogVm.UpdateRefs);
            return;
        }

        await RunStandardRebaseAsync(ontoName, dialogVm.Autosquash, dialogVm.UpdateRefs);
    }

    /// <summary>
    /// Branch-label variant: triggered from a label drawn on the commit graph
    /// (right-click on an inline label rather than the sidebar tree row).
    /// </summary>
    [RelayCommand]
    public async Task RebaseBranchLabelAsync(BranchLabel label)
    {
        if (label == null) return;

        var name = label.IsRemote && !label.IsLocal && label.RemoteName != null
            ? $"{label.RemoteName}/{label.Name}"
            : label.Name;

        var branch = new BranchInfo
        {
            Name = name,
            IsRemote = label.IsRemote,
            RemoteName = label.RemoteName,
            IsCurrent = label.IsCurrent,
        };

        await RebaseBranchAsync(branch);
    }

    private async Task RunStandardRebaseAsync(string ontoName, bool autosquash, bool updateRefs)
    {
        try
        {
            await BeginBusyAsync($"Rebasing onto {ontoName}...");

            var result = await _rebaseService.RebaseAsync(
                _currentSession!,
                ontoName,
                autosquash,
                updateRefs);

            await HandleRebaseResultAsync(result, ontoName);
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Rebase", ex);
            Log.Error("Rebase", "RebaseBranch failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Open the interactive-rebase window targeted at <paramref name="ontoName"/>
    /// as the upstream — i.e. <c>git rebase -i &lt;ontoName&gt;</c>. The plan
    /// shows commits in <c>ontoName..HEAD</c>; on Start, HEAD's unique
    /// commits are rewritten onto <paramref name="ontoName"/>.
    /// </summary>
    private async Task RunInteractiveRebaseOntoAsync(string ontoName, bool autosquash, bool updateRefs)
    {
        if (_currentSession == null)
        {
            Log.Warn("Rebase", "RunInteractiveRebaseOntoAsync: no active session.");
            return;
        }

        // The standard interactive flow doesn't push autosquash / update-refs
        // into git's todo run today (Leaf builds the plan itself, bypassing
        // git's auto-recognition of fixup!/squash! subjects). Surface that
        // honestly rather than silently dropping the flags.
        if (autosquash || updateRefs)
        {
            var dropped = autosquash && updateRefs ? "Autosquash and Update-refs"
                : autosquash ? "Autosquash" : "Update-refs";
            NotifyInfo("Interactive rebase",
                $"{dropped} option(s) are only applied in Standard mode. Edit the plan manually if needed.");
        }

        // The view-model wants a friendly "from" subject; we synthesise one
        // from the target ref since the user picked a branch, not a commit.
        var subject = $"onto {ontoName}";

        var vm = new InteractiveRebaseViewModel(
            _interactiveRebaseService,
            _currentSession,
            fromCommitSha: ontoName, // displayed in header; service uses upstreamRef directly
            fromCommitSubject: subject,
            upstreamRef: ontoName);

        MergeResult? terminalResult = null;
        EventHandler<MergeResult> onCompleted = (_, r) => terminalResult = r;
        vm.RebaseCompleted += onCompleted;

        try
        {
            var window = new InteractiveRebaseWindow(vm);
            vm.LoadAsync(_currentSession.CancellationToken)
                .FireAndForget(nameof(InteractiveRebaseViewModel.LoadAsync), isUserAction: true);
            await _dialogService.ShowDialogAsync(window);
        }
        finally
        {
            vm.RebaseCompleted -= onCompleted;
        }

        await RefreshAsync();

        if (terminalResult?.HasConflicts == true)
        {
            Log.Info("Rebase", "Routing paused-conflict state to merge editor.");
            await ContinueMergeAsync();
        }
        else if (terminalResult?.Success == true)
        {
            NotifySuccess("Interactive rebase complete", $"Rebased onto {ontoName}.");
        }
        else if (terminalResult is { Success: false, HasConflicts: false } && !string.IsNullOrEmpty(terminalResult.ErrorMessage))
        {
            await ReportOperationFailureAsync("Interactive rebase", terminalResult.ErrorMessage);
        }
    }

    /// <summary>
    /// Mirrors <see cref="HandleMergeResultAsync"/>: success → toast + graph
    /// refresh; conflicts → repo-info refresh and hand off to the merge
    /// editor (which renders rebase-mode controls when the op type is
    /// <see cref="GitOperationType.Rebase"/>); failure → error toast.
    /// </summary>
    private async Task HandleRebaseResultAsync(MergeResult result, string ontoName)
    {
        if (result.Success)
        {
            NotifySuccess("Rebase complete", $"Rebased onto {ontoName}.");
            if (GitGraphViewModel != null)
            {
                await GitGraphViewModel.LoadRepositoryAsync(SelectedRepository!.Path);
            }
            return;
        }

        if (result.HasConflicts)
        {
            NotifyWarning("Rebase paused", "Resolve conflicts to continue the rebase.");

            var info = await _gitService.GetRepositoryInfoFastAsync(
                SelectedRepository!.Path, cancellationToken: CurrentRepositoryToken);
            SelectedRepository.IsMergeInProgress = info.IsMergeInProgress;
            SelectedRepository.OperationType = info.OperationType;
            SelectedRepository.MergingBranch = info.MergingBranch;
            SelectedRepository.ConflictCount = info.ConflictCount;

            await RefreshMergeConflictResolutionAsync();
            await RefreshAsync();
            return;
        }

        await ReportOperationFailureAsync("Rebase", result.ErrorMessage ?? "unknown error");
        Log.Error("Rebase", $"HandleRebaseResult: {result.ErrorMessage}");
    }
}
