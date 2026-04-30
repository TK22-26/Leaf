using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;
using Leaf.Views;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial — git bisect entry points and verdict commands.
/// The flow is: <see cref="StartBisectFromCommitAsync"/> opens
/// <see cref="StartBisectDialog"/> with the right-clicked commit
/// pre-filled as "good" and HEAD as "bad", calls
/// <see cref="IBisectService.StartAsync"/>, then surfaces the resulting
/// state via <see cref="CurrentBisectState"/> for the bisect banner.
/// Verdict buttons fire <see cref="MarkBisectAsync"/>; reset fires
/// <see cref="ResetBisectAsync"/>. We keep state on the VM rather than
/// re-reading from disk on every refresh so the banner doesn't flicker
/// between updates.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Live snapshot of the current bisect session. Bound by the bisect
    /// banner — null when no bisect is in progress (banner hidden).
    /// </summary>
    [ObservableProperty]
    private BisectState? _currentBisectState;

    /// <summary>
    /// Set to the converging "first bad commit" SHA on the terminating
    /// step. The banner flips to a "found it" mode and the user can
    /// jump to the commit or reset.
    /// </summary>
    [ObservableProperty]
    private string? _bisectFoundSha;

    [RelayCommand]
    public async Task StartBisectFromCommitAsync(CommitInfo? commit)
    {
        if (SelectedRepository == null || _currentSession == null) return;

        // Bisect-already-running is the most likely conflict; check it
        // through the session-aware service (worktree-correct) rather than
        // via SelectedRepository.OperationType — the latter resolves
        // gitdir as <repo>/.git, which is wrong for linked worktrees.
        if (await _bisectService.IsBisectInProgressAsync(_currentSession, CurrentRepositoryToken))
        {
            await RefreshBisectStateAsync();
            await _dialogService.ShowMessageAsync(
                "A bisect is already in progress. Use the bisect banner to mark commits or reset.",
                "Start Bisect", MessageBoxButton.OK);
            return;
        }

        // Other long-running ops (merge, rebase, am, etc.) — bisect mutates
        // HEAD and would compound state. We use OperationType here even
        // though it has the same worktree limitation — the alternative is
        // adding session-based probes for every op, which is broader scope
        // than this feature and tracked in project_worktree_gitdir_resolution.
        var opType = SelectedRepository.OperationType;
        if (opType is not GitOperationType.None and not GitOperationType.Bisect)
        {
            Log.Info("Bisect", $"StartBisect refused: {opType} already in progress.");
            await _dialogService.ShowMessageAsync(
                $"A {opType.ToString().ToLowerInvariant()} is currently in progress. " +
                "Finish or abort it before starting a bisect.",
                "Start Bisect", MessageBoxButton.OK);
            return;
        }

        // Pre-flight: refuse on a dirty working tree. The dialog hint
        // mentions this but git's own error after the dialog closes is
        // late and ugly. RepositoryInfo.IsDirty is already populated by
        // SelectRepositoryAsync; we just consult it.
        if (SelectedRepository.IsDirty)
        {
            await _dialogService.ShowMessageAsync(
                "Working tree has uncommitted changes. Stash, commit, or discard them before starting a bisect — git checks out commits as it works and won't proceed with dirty state.",
                "Start Bisect", MessageBoxButton.OK);
            return;
        }

        var defaultGood = commit?.Sha ?? string.Empty;
        var dialog = new StartBisectDialog(defaultBadRef: "HEAD", defaultGoodRef: defaultGood);
        if (!await _dialogService.ShowDialogAsync(dialog)) return;

        try
        {
            await BeginBusyAsync("Starting bisect...");
            var result = await _bisectService.StartAsync(
                _currentSession, dialog.BadRef, dialog.GoodRef, CurrentRepositoryToken);

            if (!result.Success)
            {
                await ReportOperationFailureAsync(
                    "Start bisect",
                    result.ErrorMessage ?? "git bisect start failed.");
                return;
            }

            CurrentBisectState = result.State;
            BisectFoundSha = result.FirstBadSha;
            // Mid-bisect banner already shows "Testing X (K steps left)";
            // no toast for that state. Convergence on the start step is
            // vanishingly rare (it requires bad == good, i.e. a one-commit
            // range) but real, so we fire a success toast there.
            // The all-skipped terminator can't fire at start (no verdicts
            // yet means nothing has been skipped), so the FirstBadSha is
            // always present on a terminating start; the null-branch is a
            // belt-and-suspenders fallback for any future edge.
            if (result.IsTerminating)
            {
                var converged = result.FirstBadSha != null
                    ? $"{Shorten(result.FirstBadSha)} is the first bad commit."
                    : "Bisect ended immediately.";
                NotifySuccess("Bisect converged", converged);
            }
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Start bisect", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public Task MarkBisectGoodAsync() => MarkBisectAsync(BisectVerdict.Good);

    [RelayCommand]
    public Task MarkBisectBadAsync() => MarkBisectAsync(BisectVerdict.Bad);

    [RelayCommand]
    public Task MarkBisectSkipAsync() => MarkBisectAsync(BisectVerdict.Skip);

    private async Task MarkBisectAsync(BisectVerdict verdict)
    {
        if (_currentSession == null) return;
        // Banner buttons should only fire while CurrentBisectState != null,
        // but we double-check here so a stale shortcut / scripted invocation
        // doesn't surface git's verbose "We are not bisecting." stderr.
        if (CurrentBisectState == null)
        {
            Log.Info("Bisect", $"Mark {verdict} ignored: no bisect in progress.");
            return;
        }

        try
        {
            await BeginBusyAsync($"Marking commit {verdict}...");
            var result = await _bisectService.MarkAsync(_currentSession, verdict, CurrentRepositoryToken);

            if (!result.Success)
            {
                await ReportOperationFailureAsync(
                    "Mark bisect commit",
                    result.ErrorMessage ?? $"git bisect {verdict} failed.");
                return;
            }

            CurrentBisectState = result.State;
            BisectFoundSha = result.FirstBadSha;

            if (result.IsTerminating)
            {
                Log.Info("Bisect", $"Converged: first bad commit = {result.FirstBadSha}");
                var summary = result.FirstBadSha != null
                    ? $"{Shorten(result.FirstBadSha)} is the first bad commit."
                    : "Bisect ended (every remaining candidate was skipped).";
                NotifySuccess("Bisect converged", summary);
            }
            // Mid-bisect testing state has no toast — the banner shows
            // "Testing <sha> (K steps left)" continuously, which is the
            // canonical UI for that state.

            // Refresh first so the graph repopulates with current branch /
            // commit data, THEN select. Prior order had Select first and
            // the subsequent refresh would wipe the highlight. RefreshAsync
            // is async-resilient — it doesn't tear down the selection by
            // itself, but the graph rebuild that runs underneath does.
            await RefreshAsync();
            if (result.IsTerminating && !string.IsNullOrEmpty(result.FirstBadSha))
            {
                GitGraphViewModel?.SelectCommitBySha(result.FirstBadSha);
            }
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Mark bisect commit", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ResetBisectAsync()
    {
        if (_currentSession == null) return;

        try
        {
            await BeginBusyAsync("Resetting bisect...");
            var result = await _bisectService.ResetAsync(_currentSession, CurrentRepositoryToken);

            if (!result.Success)
            {
                await ReportOperationFailureAsync(
                    "Reset bisect",
                    result.ErrorMessage ?? "git bisect reset failed.");
                return;
            }

            CurrentBisectState = null;
            BisectFoundSha = null;
            NotifyInfo("Bisect ended", "HEAD has been restored.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Reset bisect", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Re-read bisect state from git. Called after a repository switch
    /// or external refresh so the banner reflects on-disk reality.
    /// </summary>
    public async Task RefreshBisectStateAsync()
    {
        if (_currentSession == null)
        {
            CurrentBisectState = null;
            BisectFoundSha = null;
            return;
        }

        try
        {
            var state = await _bisectService.GetStateAsync(_currentSession, CurrentRepositoryToken);
            CurrentBisectState = state.IsActive ? state : null;
            // GetStateAsync also reads BISECT_LOG for the converged-but-
            // not-reset case (user closed Leaf mid-bisect after the
            // converging step). Lifting state.FirstBadSha into the VM
            // flips the banner into "found it" mode on cold open instead
            // of misleadingly showing "Testing X" with no steps left.
            BisectFoundSha = state.FirstBadSha;
        }
        catch (Exception ex)
        {
            Log.Info("Bisect", $"RefreshBisectState: {ex.Message}");
            CurrentBisectState = null;
        }
    }

    private static string Shorten(string? sha) =>
        string.IsNullOrEmpty(sha) ? "?" : sha.Length >= 7 ? sha[..7] : sha;
}
