using System.Collections.ObjectModel;
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
    /// banner + detail view — null when no bisect is in progress.
    /// Setting / clearing this property also flips
    /// <see cref="ContentMode"/> so the center column takes over with
    /// the full bisect detail view (and the right pane hides), mirroring
    /// how PR mode works.
    /// </summary>
    [ObservableProperty]
    private BisectState? _currentBisectState;

    partial void OnCurrentBisectStateChanged(BisectState? value)
    {
        // Mode swap: bisect active → take over the center column;
        // bisect ended → restore the standard graph layout. We avoid
        // clobbering PR modes by only swapping when the current mode
        // is something we own (Graph ↔ Bisect). If the user opens a
        // PR while a bisect is running we leave PR mode in charge —
        // the bisect can resume on PR close because the underlying
        // git state survives, and CurrentBisectState stays non-null.
        if (value != null && ContentMode == ContentMode.Graph)
        {
            ContentMode = ContentMode.Bisect;
        }
        else if (value == null && ContentMode == ContentMode.Bisect)
        {
            ContentMode = ContentMode.Graph;
        }
    }

    /// <summary>
    /// Set to the converging "first bad commit" SHA on the terminating
    /// step. The banner flips to a "found it" mode and the user can
    /// jump to the commit or reset.
    /// </summary>
    [ObservableProperty]
    private string? _bisectFoundSha;

    /// <summary>
    /// Files changed by the currently-tested commit (or by the converging
    /// "first bad commit" once <see cref="BisectFoundSha"/> is set). Bound
    /// by the bisect detail pane so the user can review the diff alongside
    /// the verdict buttons — every other Git GUI we surveyed forces the
    /// user to switch to an external editor between checkout and verdict;
    /// this is the killer feature of the in-app bisect UX.
    /// </summary>
    public ObservableCollection<FileChangeInfo> CurrentBisectChanges { get; } = new();

    /// <summary>
    /// User-driven verdict history (most-recent first). Bound by the
    /// bisect detail pane's "Verdict log" section; the head row gets an
    /// Undo affordance via <see cref="UndoLastBisectVerdictCommand"/>.
    /// </summary>
    public ObservableCollection<BisectLogEntry> BisectLog { get; } = new();

    /// <summary>
    /// Full commit info (author, date, message body) for the currently-
    /// tested commit or the converged "first bad commit". Bound by the
    /// bisect detail header so the user has the full context — author
    /// + relative date + body — to read alongside the diff before
    /// clicking a verdict.
    /// </summary>
    [ObservableProperty]
    private CommitInfo? _currentBisectCommitInfo;

    /// <summary>
    /// Currently-selected file in the bisect detail's change list. The
    /// embedded diff viewer (<see cref="BisectDiffViewerViewModel"/>)
    /// loads this file's diff so the user can read the actual code
    /// alongside the verdict buttons.
    /// </summary>
    [ObservableProperty]
    private FileChangeInfo? _selectedBisectFile;

    /// <summary>
    /// Dedicated DiffViewerViewModel for the bisect detail pane —
    /// separate from the main <c>DiffViewerViewModel</c> so the
    /// bisect's embedded diff doesn't fight with the global
    /// IsDiffViewerVisible takeover state. Set by
    /// <see cref="MainViewModel"/>'s ctor.
    /// </summary>
    public DiffViewerViewModel? BisectDiffViewerViewModel { get; set; }

    /// <summary>
    /// Monotonic counter incremented on every selected-file change so
    /// the embedded bisect diff loader can supersede in-flight loads
    /// when the user switches files quickly. Each call captures its
    /// sequence number; later checks compare against the latest value
    /// to decide whether to apply results or abandon them.
    /// </summary>
    private int _bisectDiffSequence;

    /// <summary>
    /// Whether the "Changes in this commit" panel in the bisect right
    /// rail is expanded. Mirrors the staged/unstaged collapsible
    /// sections in <c>WorkingChangesView</c>.
    /// </summary>
    [ObservableProperty]
    private bool _isBisectChangesExpanded = true;

    /// <summary>
    /// Whether the "Verdict log" panel in the bisect right rail is
    /// expanded.
    /// </summary>
    [ObservableProperty]
    private bool _isBisectVerdictLogExpanded = true;

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
            await ReloadBisectDetailAsync();
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
            await ReloadBisectDetailAsync();

            if (result.IsTerminating)
            {
                Log.Info("Bisect", $"Converged: first bad commit = {result.FirstBadSha}");
                var summary = result.FirstBadSha != null
                    ? $"{Shorten(result.FirstBadSha)} is the first bad commit."
                    : "Bisect ended (every remaining candidate was skipped).";
                NotifySuccess("Bisect converged", summary);
            }
            // Mid-bisect testing state has no toast — the banner + right
            // pane show "Testing <sha> (K steps left)" + the diff
            // continuously, which is the canonical UI for that state.

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

            // ClearBisectState wipes every bisect observable in lockstep
            // (root state + dependents + diff viewer). Inline cleanup here
            // used to miss CurrentBisectCommitInfo / SelectedBisectFile /
            // the diff viewer — leaving stale commit metadata visible
            // after the user ended the bisect.
            ClearBisectState();
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
            ClearBisectState();
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
            await ReloadBisectDetailAsync();
        }
        catch (Exception ex)
        {
            // Wipe the lot on a transient error — leaving partial state
            // (e.g. CurrentBisectChanges populated from a prior session
            // while CurrentBisectState is null) shows ghost data on the
            // next render. ReloadBisectDetailAsync's null-state path
            // already handles cleanup; we just route through it.
            Log.Info("Bisect", $"RefreshBisectState: {ex.Message}");
            ClearBisectState();
        }
    }

    /// <summary>
    /// Reset every bisect-related observable to its empty/null state.
    /// Used by the no-session early-out, the explicit reset path, and
    /// the error path so they can't leak partial UI state from a prior
    /// bisect. Built on <see cref="ClearDependentBisectState"/>; this
    /// method additionally nulls the root-level <see cref="CurrentBisectState"/>
    /// and <see cref="BisectFoundSha"/>.
    /// </summary>
    private void ClearBisectState()
    {
        CurrentBisectState = null;
        BisectFoundSha = null;
        ClearDependentBisectState();
    }

    /// <summary>
    /// Clear only the observables derived from the current bisect commit
    /// — leaves <see cref="CurrentBisectState"/> and
    /// <see cref="BisectFoundSha"/> untouched. Used by
    /// <see cref="ReloadBisectDetailAsync"/>'s null-branch where the root
    /// state is already null and we just need to wipe its dependents.
    /// </summary>
    private void ClearDependentBisectState()
    {
        CurrentBisectCommitInfo = null;
        SelectedBisectFile = null;
        CurrentBisectChanges.Clear();
        BisectLog.Clear();
        BisectDiffViewerViewModel?.Clear();
    }

    /// <summary>
    /// SHA the bisect view binds against for diffs, file lists, commit
    /// header lookups, and clipboard copy. Resolves to the converged
    /// "first bad" commit when bisect has terminated; otherwise the
    /// commit git just checked out for testing. Centralising this
    /// avoids drift between call sites when convergence semantics
    /// change — every bisect-aware read goes through this one helper.
    /// Returns null when no bisect is active.
    /// </summary>
    private string? EffectiveBisectSha =>
        !string.IsNullOrEmpty(BisectFoundSha)
            ? BisectFoundSha
            : CurrentBisectState?.CurrentSha;

    private static string Shorten(string? sha) =>
        string.IsNullOrEmpty(sha) ? "?" : sha.Length >= 7 ? sha[..7] : sha;

    /// <summary>
    /// Re-populate <see cref="CurrentBisectChanges"/> and <see cref="BisectLog"/>
    /// from git. Called after every Start / Mark / Undo / refresh so the
    /// right-pane bisect detail view stays in lockstep with on-disk state.
    /// Loads the diff of the currently-tested commit (or the converged
    /// "first bad commit" when present) so the user can review what
    /// changed before clicking a verdict.
    /// </summary>
    private async Task ReloadBisectDetailAsync()
    {
        if (_currentSession == null || CurrentBisectState == null)
        {
            ClearDependentBisectState();
            return;
        }

        // Diff target — the "what should we be looking at right now?"
        // SHA. Centralised on EffectiveBisectSha so every bisect-aware
        // read agrees: detail diff (here), per-file diff load
        // (OnSelectedBisectFileChanged), and SHA copy (CopyBisectSha).
        var diffSha = EffectiveBisectSha;

        try
        {
            // Full commit info for the testing/converged card header
            // (author, full date, message body). The state record gives
            // us subject + short-sha but not the rest.
            CurrentBisectCommitInfo = !string.IsNullOrEmpty(diffSha)
                ? await _gitService.GetCommitAsync(SelectedRepository!.Path, diffSha, CurrentRepositoryToken)
                : null;

            CurrentBisectChanges.Clear();
            if (!string.IsNullOrEmpty(diffSha))
            {
                var changes = await _gitService.GetCommitChangesAsync(
                    SelectedRepository!.Path, diffSha, CurrentRepositoryToken);
                foreach (var c in changes) CurrentBisectChanges.Add(c);
            }

            BisectLog.Clear();
            var entries = await _bisectService.GetLogAsync(_currentSession, CurrentRepositoryToken);
            foreach (var e in entries) BisectLog.Add(e);

            // Auto-select the first changed file so the diff pane isn't
            // empty on a fresh step. The user can pick a different file
            // from the list to inspect; selection drives the embedded
            // DiffViewerControl.
            if (CurrentBisectChanges.Count > 0)
            {
                SelectedBisectFile = CurrentBisectChanges[0];
            }
            else
            {
                SelectedBisectFile = null;
                BisectDiffViewerViewModel?.Clear();
            }
        }
        catch (Exception ex)
        {
            // Best-effort — a transient git error shouldn't blow up the
            // bisect flow. Log; the user can refresh.
            Log.Info("Bisect", $"ReloadBisectDetail: {ex.Message}");
        }
    }

    /// <summary>
    /// Load the diff for <paramref name="file"/> (resolved against the
    /// currently-tested or converged bisect commit) into the embedded
    /// bisect diff viewer. Wired to the file-list selection in
    /// <see cref="BisectDetailView"/> so picking a row updates the
    /// right-side diff pane.
    /// </summary>
    partial void OnSelectedBisectFileChanged(FileChangeInfo? value)
    {
        if (value == null || BisectDiffViewerViewModel == null || _currentSession == null) return;
        var diffSha = EffectiveBisectSha;
        if (string.IsNullOrEmpty(diffSha)) return;

        // Bump the sequence and fire the load. Any in-flight earlier
        // loads see a now-stale sequence on completion and abandon
        // their results — last-call-wins, no torn renders if the user
        // clicks through files faster than git can read them.
        _ = LoadBisectDiffAsync(value, diffSha, ++_bisectDiffSequence);
    }

    private async Task LoadBisectDiffAsync(FileChangeInfo file, string commitSha, int sequence)
    {
        if (BisectDiffViewerViewModel == null || SelectedRepository == null) return;
        try
        {
            BisectDiffViewerViewModel.IsLoading = true;
            BisectDiffViewerViewModel.RepositoryPath = SelectedRepository.Path;
            var (oldContent, newContent) = await _gitService.GetFileDiffAsync(
                SelectedRepository.Path, commitSha, file.Path, cancellationToken: CurrentRepositoryToken);

            // Superseded? Newer selection bumped the sequence while we
            // were awaiting git. Drop our results — the newer load will
            // paint the right thing when its await returns.
            if (sequence != _bisectDiffSequence) return;

            var result = _diffService.ComputeDiff(oldContent, newContent, file.FileName, file.Path);
            BisectDiffViewerViewModel.LoadDiff(result);
        }
        catch (Exception ex)
        {
            Log.Info("Bisect", $"LoadBisectDiff for '{file.Path}': {ex.Message}");
        }
        finally
        {
            // Only clear IsLoading when we're still the latest load.
            // A superseded load clearing it would prematurely tell the
            // UI "diff ready" while a newer load is still pending.
            if (sequence == _bisectDiffSequence)
                BisectDiffViewerViewModel.IsLoading = false;
        }
    }

    /// <summary>
    /// Roll back the most recent verdict via the
    /// <see cref="IBisectService.UndoLastVerdictAsync"/> replay-based
    /// flow. The bisect emerges in the state it was in just before the
    /// click; the user can then issue a different verdict or End Bisect
    /// outright.
    /// </summary>
    /// <summary>
    /// Copy the full SHA of the currently-tested (or converged) bisect
    /// commit to the clipboard. Bound by the bisect detail header's
    /// SHA button so users can paste the sha into bug reports / chat
    /// without leaving the bisect flow.
    /// </summary>
    [RelayCommand]
    public void CopyBisectSha()
    {
        var sha = EffectiveBisectSha;
        if (string.IsNullOrEmpty(sha)) return;
        _clipboardService.SetText(sha);
        NotifyInfo("Copied", $"SHA {Shorten(sha)} copied to clipboard.");
    }

    [RelayCommand]
    public async Task UndoLastBisectVerdictAsync()
    {
        if (_currentSession == null || CurrentBisectState == null) return;
        if (BisectLog.Count == 0)
        {
            NotifyInfo("Nothing to undo", "No verdicts have been issued in this bisect yet.");
            return;
        }

        try
        {
            await BeginBusyAsync("Undoing last verdict...");
            var result = await _bisectService.UndoLastVerdictAsync(_currentSession, CurrentRepositoryToken);

            if (!result.Success)
            {
                await ReportOperationFailureAsync("Undo verdict", result.ErrorMessage ?? "Could not undo last verdict.");
                return;
            }

            CurrentBisectState = result.State;
            // Replay can re-converge to the same first-bad commit if the
            // user undoes a verdict that doesn't change the search range
            // (rare — they'd be undoing the converging step). Honour
            // whatever git tells us.
            BisectFoundSha = result.FirstBadSha;
            await ReloadBisectDetailAsync();

            // Toast text: distinguish the three post-undo outcomes.
            // Re-converged is rare but real; we'd rather say "first bad
            // commit" than the misleading "Re-testing X" — the user
            // expected an undo to re-open the search, and they need to
            // know it didn't.
            string undoMessage;
            if (!string.IsNullOrEmpty(BisectFoundSha))
                undoMessage = $"Re-converged on {Shorten(BisectFoundSha)} as the first bad commit.";
            else if (CurrentBisectState != null)
                undoMessage = $"Re-testing {CurrentBisectState.CurrentShortSha}.";
            else
                undoMessage = "Bisect state restored.";
            NotifyInfo("Verdict undone", undoMessage);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Undo verdict", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
