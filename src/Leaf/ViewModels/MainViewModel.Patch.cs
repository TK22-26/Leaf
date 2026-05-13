using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;
using Leaf.Views;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial — patch creation, quick-clipboard export, and
/// apply-patch entry points. Routes everything through
/// <see cref="IPatchService"/>; conflicts on <c>git am</c> hand off to
/// the existing merge-editor pathway via <see cref="ContinueMergeAsync"/>
/// which already understands rebase-style state.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Single-commit "Create Patch File…" entry point from the commit
    /// context menu. Asks the user where to write the <c>.patch</c> via
    /// <see cref="CreatePatchDialog"/>, then runs <c>git format-patch</c>.
    /// </summary>
    [RelayCommand]
    public async Task CreatePatchFromCommitAsync(CommitInfo? commit)
    {
        if (commit == null || string.IsNullOrEmpty(commit.Sha) || SelectedRepository == null) return;
        if (_currentSession == null)
        {
            Log.Warn("Patch", "CreatePatch: no active session.");
            return;
        }

        // The default output directory (working tree root) doesn't exist
        // for a bare repo — git format-patch itself works there but we'd
        // be writing patches into the bare git directory, which is hostile.
        // Fall back to Documents and let the user pick somewhere sane.
        var defaultDir = _currentSession.IsBareRepository
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : SelectedRepository.Path;
        var description = string.IsNullOrEmpty(commit.MessageShort)
            ? commit.ShortSha
            : $"{commit.ShortSha}  {commit.MessageShort}";

        // Pre-populate options from the user's git config so the dialog
        // reflects what `git format-patch` would do with no flags. We
        // pass --signoff / --no-signoff explicitly on submit, so the
        // checkbox state is authoritative — but reading config first
        // means the *initial* state matches the user's expectation
        // instead of always-off.
        var defaults = await ReadFormatPatchDefaultsAsync(SelectedRepository.Path, CurrentRepositoryToken);

        var dialog = new CreatePatchDialog(description, defaultDir, defaults);
        if (!await _dialogService.ShowDialogAsync(dialog)) return;

        try
        {
            await BeginBusyAsync("Creating patch...");
            var result = await _patchService.CreateAsync(
                _currentSession,
                [commit.Sha],
                dialog.OutputDirectory,
                dialog.Options,
                CurrentRepositoryToken);

            if (!result.Success)
            {
                await ReportOperationFailureAsync("Create patch", result.ErrorMessage ?? "git format-patch failed.");
                return;
            }

            // Title carries the verb. Body: file name (single-patch case)
            // plus folder path. The multi-file path used to duplicate the
            // directory by including it in both a summary and the body;
            // now body is the directory and the lead line is the file
            // count. ("description" is shadowed by an outer local that
            // holds the commit summary fed to the dialog — hence the
            // distinct local name.)
            var toastBody = result.Files.Count == 1
                ? $"{Path.GetFileName(result.Files[0])}\n{result.OutputDirectory}"
                : $"Saved {result.Files.Count} patch files to:\n{result.OutputDirectory}";
            NotifySuccess(Models.NotificationCategory.Patch, "Patch created", toastBody);
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Create patch", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// "Copy as Patch" — runs <c>git format-patch -1 --stdout</c> and puts
    /// the result on the clipboard. No file is written; the user pastes
    /// the patch into mail / chat / a code review tool.
    /// </summary>
    [RelayCommand]
    public async Task CopyCommitAsPatchAsync(CommitInfo? commit)
    {
        if (commit == null || string.IsNullOrEmpty(commit.Sha) || SelectedRepository == null) return;
        if (_currentSession == null) return;

        try
        {
            await BeginBusyAsync("Exporting patch to clipboard...");
            var text = await _patchService.ExportToTextAsync(
                _currentSession, commit.Sha, CurrentRepositoryToken);
            // Empty stdout from format-patch means the commit produced no
            // diff (e.g. a true no-op merge or a commit equal to its
            // parent). Per the engineering-software policy, surface this
            // rather than putting a blank string on the user's clipboard
            // and acting like the export succeeded.
            if (string.IsNullOrEmpty(text))
            {
                await ReportOperationFailureAsync(
                    "Copy as patch",
                    $"Commit {commit.ShortSha} produced no diff — nothing to copy.");
                return;
            }
            _clipboardService.SetText(text);
            NotifySuccess(
                Models.NotificationCategory.Patch,
                "Patch copied",
                $"Patch for {commit.ShortSha} is on your clipboard ({text.Length:N0} chars).");
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Copy as patch", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// "Apply Patch…" entry point on the File menu. Opens
    /// <see cref="ApplyPatchDialog"/>, which lets the user pick patches +
    /// strategy and shows a header preview, then drives the apply.
    /// </summary>
    [RelayCommand]
    public async Task ApplyPatchAsync()
    {
        if (SelectedRepository == null) return;
        if (_currentSession == null)
        {
            await _dialogService.ShowMessageAsync(
                "Repository session is not initialised. Try selecting the repository again.",
                "Apply Patch", MessageBoxButton.OK);
            return;
        }

        // git am / git apply both require a working tree. Refuse on bare
        // repos so the user gets a clear message instead of a verbatim
        // git error after they've gone through the file picker.
        if (_currentSession.IsBareRepository)
        {
            Log.Info("Patch", "ApplyPatch refused: bare repository.");
            await _dialogService.ShowMessageAsync(
                "This repository is bare — there is no working tree to apply patches into.",
                "Apply Patch", MessageBoxButton.OK);
            return;
        }

        // Refuse if any other git operation is paused. git am itself
        // would fail with "fatal: previous rebase directory ... still
        // exists" against any rebase-apply state — surfacing that as a
        // hard error after the file picker is worse UX than catching it
        // up front. For a paused am we route the user to the merge
        // editor so they can finish the prior series; for the rest we
        // just message them back to the right tool.
        var opType = SelectedRepository.OperationType;
        if (opType == GitOperationType.Am)
        {
            // ContinueMergeAsync opens the merge editor modal next, so we
            // don't fire a separate toast — that would just be noise the
            // user has to dismiss before they can resolve the conflict.
            Log.Info("Patch", "ApplyPatch: am-in-progress — routing to merge editor.");
            await ContinueMergeAsync();
            return;
        }
        if (opType != GitOperationType.None)
        {
            Log.Info("Patch", $"ApplyPatch refused: {opType} already in progress.");
            await _dialogService.ShowMessageAsync(
                $"A {opType.ToString().ToLowerInvariant()} is currently in progress. " +
                "Finish or abort it before applying new patches.",
                "Apply Patch", MessageBoxButton.OK);
            return;
        }

        var dialog = new ApplyPatchDialog(_patchService);
        if (!await _dialogService.ShowDialogAsync(dialog)) return;
        if (dialog.PatchFiles.Count == 0) return;

        // git apply with multiple patches dumps every diff into the working
        // tree, mixed together, with no per-patch rollback — easy to land
        // an unrunnable intermediate state. Confirm before doing that.
        if (dialog.Strategy == ApplyPatchStrategy.Apply && dialog.PatchFiles.Count > 1)
        {
            var ok = await _dialogService.ShowConfirmationAsync(
                $"You picked the 'git apply' strategy with {dialog.PatchFiles.Count} patches.\n\n" +
                "All diffs will be applied to the working tree as a single uncommitted blob. " +
                "If a later patch fails, the earlier ones do not roll back.\n\n" +
                "Apply anyway?",
                "Apply Patch");
            if (!ok)
            {
                Log.Info("Patch", "ApplyPatch: user cancelled multi-patch apply confirmation.");
                return;
            }
        }

        // Detached HEAD + git am produces orphaned commits — they get
        // recorded but no branch advances, so the user comes back later
        // wondering where their work went. Warn them; keep am as the
        // user's choice (some workflows really do want this).
        if (dialog.Strategy == ApplyPatchStrategy.Am && SelectedRepository.IsDetachedHead)
        {
            var ok = await _dialogService.ShowConfirmationAsync(
                "HEAD is currently detached. 'git am' will create commits but no branch will " +
                "advance to point at them — they'll be unreachable as soon as you check out " +
                "anything else.\n\n" +
                "Continue anyway? (You can create a branch first to capture the new commits.)",
                "Apply Patch");
            if (!ok)
            {
                Log.Info("Patch", "ApplyPatch: user cancelled detached-HEAD apply confirmation.");
                return;
            }
        }

        try
        {
            await BeginBusyAsync("Applying patches...");
            var result = await _patchService.ApplyAsync(
                _currentSession,
                dialog.PatchFiles,
                dialog.Strategy,
                CurrentRepositoryToken);

            if (result.Success)
            {
                var summary = dialog.Strategy == ApplyPatchStrategy.Am
                    ? $"Applied {dialog.PatchFiles.Count} patch(es) as new commits."
                    : "Patch applied to working tree.";
                NotifySuccess(Models.NotificationCategory.Patch, "Patch applied", summary);
                await RefreshAsync();
                return;
            }

            if (result.HasConflicts)
            {
                Log.Info("Patch", $"am paused on conflict (sha={result.ConflictAtSha}); routing to merge editor.");
                // No toast — the merge editor opens immediately and the
                // banner there carries the same "paused on conflict" cue.
                await RefreshAsync();
                await ContinueMergeAsync();
                return;
            }

            await ReportOperationFailureAsync("Apply patch", result.ErrorMessage ?? "git am/apply failed.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Apply patch", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Read <c>format.signoff</c> and <c>format.subjectPrefix</c> from
    /// git config (Local scope, which falls back to global/system) so the
    /// Create Patch dialog opens reflecting what bare <c>git format-patch</c>
    /// would do. Failures fall through to the model's own zero-defaults —
    /// missing config is the common case, not an error.
    /// </summary>
    private async Task<CreatePatchOptions> ReadFormatPatchDefaultsAsync(string repoPath, CancellationToken ct)
    {
        var defaults = new CreatePatchOptions();
        try
        {
            var signoff = await _gitService.GetConfigAsync(repoPath, "format.signoff", GitConfigScope.Local, ct);
            var parsed = PatchService.ParseGitConfigBool(signoff);
            if (parsed.HasValue) defaults.SignOff = parsed.Value;

            var subjectPrefix = await _gitService.GetConfigAsync(repoPath, "format.subjectPrefix", GitConfigScope.Local, ct);
            if (!string.IsNullOrEmpty(subjectPrefix))
            {
                defaults.SubjectPrefix = subjectPrefix;
            }
        }
        catch (Exception ex)
        {
            // Config probes are best-effort — a transient git failure
            // shouldn't block the dialog.
            Log.Info("Patch", $"ReadFormatPatchDefaults: {ex.Message} (using zero defaults)");
        }
        return defaults;
    }
}
