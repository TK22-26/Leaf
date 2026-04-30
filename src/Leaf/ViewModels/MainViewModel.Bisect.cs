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

        // If a bisect is already running, opening the start dialog would
        // get the user a confusing "git bisect: not in bisect mode" or
        // overwrite-the-state result. Route them to the in-progress
        // banner instead.
        if (await _bisectService.IsBisectInProgressAsync(_currentSession, CurrentRepositoryToken))
        {
            await RefreshBisectStateAsync();
            await _dialogService.ShowMessageAsync(
                "A bisect is already in progress. Use the bisect banner to mark commits or reset.",
                "Start Bisect", MessageBoxButton.OK);
            return;
        }

        // Refuse if any other long-running op is paused — bisect mutates
        // HEAD and would either fail or compound state.
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
            StatusMessage = result.IsTerminating
                ? $"Bisect converged immediately: {Shorten(result.FirstBadSha)}"
                : $"Bisect started — testing {result.State?.CurrentShortSha} ({result.State?.StepsRemaining} steps remaining).";
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
                StatusMessage = $"Bisect converged: {Shorten(result.FirstBadSha)} is the first bad commit.";
                // Jump the graph to the found commit so the user can
                // inspect it without manual hunt — they're going to want
                // to read the diff next.
                if (!string.IsNullOrEmpty(result.FirstBadSha))
                {
                    GitGraphViewModel?.SelectCommitBySha(result.FirstBadSha);
                }
            }
            else
            {
                StatusMessage = $"Testing {result.State?.CurrentShortSha} ({result.State?.StepsRemaining} steps remaining).";
            }
            await RefreshAsync();
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
            StatusMessage = "Bisect ended; HEAD restored.";
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
            // We don't reconstitute BisectFoundSha here — that's only
            // known when a Mark call observed git's terminating output.
            // After a refresh we treat the bisect as "in progress" and
            // let the next verdict reveal the converging step.
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
