using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;
using Leaf.Views;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial — interactive rebase entry point. The right-click
/// menu on a commit fires <see cref="RebaseInteractivelyFromCommitCommand"/>
/// with the selected <see cref="CommitInfo"/>; we hand it off to a fresh
/// <see cref="InteractiveRebaseViewModel"/> hosted in
/// <see cref="InteractiveRebaseWindow"/>. Mid-rebase conflicts route
/// through the existing <see cref="MainViewModel.MergeConflict"/> path
/// because git's rebase already sets <c>OperationType.Rebase</c> on the
/// repo info — no special-cased plumbing here.
/// </summary>
public partial class MainViewModel
{
    [RelayCommand]
    public async Task RebaseInteractivelyFromCommitAsync(CommitInfo? commit)
    {
        if (commit == null || string.IsNullOrEmpty(commit.Sha))
        {
            Log.Info("InteractiveRebase", "Command invoked without a target commit; ignored.");
            return;
        }
        if (SelectedRepository == null) return;

        Log.Info("InteractiveRebase", $"User invoked rebase from {commit.ShortSha} on {SelectedRepository.Name}");

        if (_currentSession == null)
        {
            Log.Warn("InteractiveRebase", "No active session — refusing to open editor.");
            await _dialogService.ShowMessageAsync(
                "Repository session is not initialised. Try selecting the repository again.",
                "Interactive Rebase",
                MessageBoxButton.OK);
            return;
        }

        // Refuse to launch a second rebase on top of an in-progress one;
        // git would just bail with "rebase in progress" and the user would
        // get a less actionable message than this one.
        if (SelectedRepository.OperationType == GitOperationType.Rebase)
        {
            Log.Warn("InteractiveRebase", "Refused: rebase already in progress.");
            await _dialogService.ShowMessageAsync(
                "A rebase is already in progress. Continue, skip, or abort it before starting a new one.",
                "Interactive Rebase",
                MessageBoxButton.OK);
            return;
        }

        // We deliberately do not block on a dirty working tree here — git
        // itself will fail the rebase with a clear message ("cannot rebase:
        // You have unstaged changes…") and the failure path surfaces it
        // via StatusMessage. That keeps the entry-point cheap and avoids
        // a precheck that might disagree with git's own staging rules.

        var subject = string.IsNullOrEmpty(commit.MessageShort)
            ? commit.Sha
            : commit.MessageShort;

        var vm = new InteractiveRebaseViewModel(
            _interactiveRebaseService,
            _currentSession,
            commit.Sha,
            subject);

        // Result is captured here so the post-dialog branch can route
        // conflicts through the existing merge editor without reaching
        // back into the (now-disposed) view-model.
        MergeResult? terminalResult = null;
        EventHandler<MergeResult> onCompleted = (_, r) => terminalResult = r;
        vm.RebaseCompleted += onCompleted;

        try
        {
            var window = new InteractiveRebaseWindow(vm);
            // Kick off the load fire-and-forget — the window's loading
            // overlay covers the populate window. We can't await before
            // ShowDialogAsync because the dialog must be the foreground
            // operation (otherwise the loading overlay never appears).
            vm.LoadAsync(_currentSession.CancellationToken)
                .FireAndForget(nameof(InteractiveRebaseViewModel.LoadAsync), isUserAction: true);

            await _dialogService.ShowDialogAsync(window);
        }
        finally
        {
            vm.RebaseCompleted -= onCompleted;
        }

        // Always refresh — even Cancel can leave the working tree alone
        // but the user might have done something else in the meantime, so
        // a refresh keeps the graph + status pane authoritative.
        await RefreshAsync();

        if (terminalResult?.HasConflicts == true)
        {
            // Hand off to the existing rebase-conflict pathway. The merge
            // editor opens against OperationType.Rebase and exposes
            // continue/skip/abort via its own toolbar.
            Log.Info("InteractiveRebase", "Routing paused-conflict state to merge editor.");
            StatusMessage = "Rebase paused on conflict — resolve and continue.";
            await ContinueMergeAsync();
        }
        else if (terminalResult?.Success == true)
        {
            Log.Info("InteractiveRebase", "Rebase completed cleanly; refreshing repository view.");
            StatusMessage = "Interactive rebase completed.";
        }
        else if (terminalResult is { Success: false, HasConflicts: false } && !string.IsNullOrEmpty(terminalResult.ErrorMessage))
        {
            Log.Warn("InteractiveRebase", $"Rebase ended with error: {terminalResult.ErrorMessage}");
            StatusMessage = $"Rebase failed: {terminalResult.ErrorMessage}";
        }
        else if (terminalResult == null)
        {
            // Cancelled before Start — the dialog closed without raising
            // RebaseCompleted. No status message change; the user knows.
            Log.Info("InteractiveRebase", "Dialog closed without a Start (user cancelled or window dismissed).");
        }
    }
}
