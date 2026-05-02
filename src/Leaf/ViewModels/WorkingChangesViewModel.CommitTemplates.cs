using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;
using Leaf.Services.Git.Core;

namespace Leaf.ViewModels;

/// <summary>
/// §5.15 partial — commit-template apply pipeline. Resolves placeholders
/// against the live branch + git config, splits the resulting message
/// into the subject / description text boxes, and reports a target caret
/// position for the view to snap focus to.
/// </summary>
public partial class WorkingChangesViewModel
{
    /// <summary>
    /// Apply mode requested by the picker. <c>Replace</c> overwrites the
    /// current message and description; <c>Append</c> adds the resolved
    /// text to the description (or to the first line when that's still
    /// empty) without disturbing the existing subject. Trailer-shaped
    /// presets (<c>Co-authored-by</c>, <c>Signed-off-by</c>) ship with
    /// a leading <c>\n\n</c> in their body precisely so Append behaves
    /// like a real footer.
    /// </summary>
    public enum CommitTemplateApplyMode
    {
        Replace,
        Append,
    }

    /// <summary>
    /// Caret target produced by the most recent template apply. The
    /// CommitInputControl reads these via DP-mirror properties on its
    /// own surface and snaps the focused TextBox + CaretIndex to match.
    /// Reset to (-1, false) after each consumer applies them so a stale
    /// value doesn't survive a second apply that didn't write a fresh
    /// caret.
    /// </summary>
    [ObservableProperty]
    private int _templateCaretIndex = -1;

    [ObservableProperty]
    private bool _templateCaretInDescription;

    /// <summary>
    /// Bumped on every successful template apply so the view can react
    /// even when the caret position itself didn't change. Without this
    /// the second apply of the same template wouldn't trigger any DP
    /// change notification (same int + same bool = no event).
    /// </summary>
    [ObservableProperty]
    private int _templateApplyTick;

    /// <summary>
    /// Apply a template. Resolves placeholders, splits the result into
    /// subject + description on the first blank line, and exposes the
    /// caret target on <see cref="TemplateCaretIndex"/> /
    /// <see cref="TemplateCaretInDescription"/> for the view to snap to.
    /// </summary>
    /// <remarks>
    /// Replace mode discards the current text. Append mode keeps the
    /// existing subject untouched and appends to the description (or
    /// promotes resolved text into the subject when the subject was
    /// blank — that handles "fresh apply of a template into an empty
    /// commit panel" without forcing the user to think about modes).
    /// </remarks>
    [RelayCommand]
    private async Task ApplyCommitTemplateAsync(CommitTemplateApplyRequest? request)
    {
        if (request?.Template is null) return;

        // user.name / user.email come from git config — local first,
        // then global. The repo path may be null when the user opens
        // the picker before a repo is selected; in that case we just
        // hand null to the resolver and {user.*} resolves to empty.
        string? userName = null;
        string? userEmail = null;
        if (!string.IsNullOrEmpty(_repositoryPath))
        {
            try
            {
                userName = await _gitService.GetConfigAsync(
                    _repositoryPath, "user.name",
                    GitConfigScope.Local, SessionToken);
                userEmail = await _gitService.GetConfigAsync(
                    _repositoryPath, "user.email",
                    GitConfigScope.Local, SessionToken);
            }
            catch (OperationCanceledException)
            {
                // Repo switched mid-apply — bail without writing anything.
                return;
            }
        }

        var branchName = WorkingChanges?.BranchName;

        var resolved = _commitTemplateService.Resolve(
            request.Template,
            branchName,
            userName,
            userEmail,
            out var cursorOffset);

        var (subject, body) = SplitSubjectAndBody(resolved);

        if (request.Mode == CommitTemplateApplyMode.Replace)
        {
            CommitMessage = subject;
            CommitDescription = body;
            ApplyCaretTarget(cursorOffset, subject, body);
        }
        else
        {
            // Append — preserve subject when it has content, otherwise
            // promote the template's subject as a freebie. The body
            // gets concatenated with a separator only when both sides
            // already have content (no leading blank lines on first
            // append).
            if (string.IsNullOrEmpty(CommitMessage))
            {
                CommitMessage = subject;
            }

            string newBody;
            if (string.IsNullOrEmpty(CommitDescription))
            {
                newBody = body;
            }
            else if (string.IsNullOrEmpty(body))
            {
                newBody = CommitDescription;
            }
            else
            {
                newBody = CommitDescription.TrimEnd() + "\n\n" + body;
            }

            CommitDescription = newBody;
            // Caret in append mode lands at the end of whatever we just
            // wrote in the description.
            TemplateCaretInDescription = true;
            TemplateCaretIndex = newBody.Length;
        }

        // Persist last-used template id for the "default Ctrl+T target"
        // future enhancement and so the settings UI can highlight it.
        var settings = _settingsService.LoadSettings();
        if (!string.Equals(settings.LastUsedCommitTemplateId, request.Template.Id, StringComparison.Ordinal))
        {
            settings.LastUsedCommitTemplateId = request.Template.Id;
            _settingsService.SaveSettings(settings);
        }

        // Conventional Commits mode: the structured fields drive
        // CommitMessage/CommitDescription via Rebuild on every field
        // change, so writing those properties directly leaves the form
        // out of sync. Without this sync, the user's next keystroke in
        // any structured field would Rebuild from stale fields and
        // wipe the template output. Sync now so Rebuild is a no-op.
        SyncConventionalFieldsFromFreeform();

        TemplateApplyTick++;
    }

    /// <summary>
    /// Split a resolved template body into the subject (first line) and
    /// the description (everything after the first blank line, or after
    /// the first single newline when the template has no blank-line
    /// separator). Mirrors how WorkingChangesView already presents the
    /// CommitInputControl's two text boxes.
    /// </summary>
    internal static (string subject, string body) SplitSubjectAndBody(string resolved)
    {
        if (string.IsNullOrEmpty(resolved)) return (string.Empty, string.Empty);

        // Normalise line endings to \n once up front so the blank-line
        // search doesn't have to know about \r\n vs \n. Windows users
        // type \r\n\r\n in their templates, Conventional Commits expects
        // a blank line, and the canonical-form lookup needs both to mean
        // the same thing.
        var normalised = resolved.Replace("\r\n", "\n", StringComparison.Ordinal);

        // Blank-line split (Conventional Commits style). When present,
        // it's the canonical separator and we use it.
        var blankIdx = normalised.IndexOf("\n\n", StringComparison.Ordinal);
        if (blankIdx >= 0)
        {
            var subject = normalised[..blankIdx];
            var body = normalised[(blankIdx + 2)..];
            return (subject, body);
        }

        // No blank-line separator — treat the first single newline as the
        // boundary. Templates that fit on one line yield an empty body.
        var nlIdx = normalised.IndexOf('\n');
        if (nlIdx < 0) return (normalised, string.Empty);
        return (normalised[..nlIdx], normalised[(nlIdx + 1)..]);
    }

    private void ApplyCaretTarget(int cursorOffset, string subject, string body)
    {
        // The resolver returns a single offset into the joined string;
        // translate it to a (TextBox, index) pair. Use the same boundary
        // logic as SplitSubjectAndBody so the conversion is exact.
        var subjectEndInclusive = subject.Length;
        if (cursorOffset <= subjectEndInclusive)
        {
            TemplateCaretInDescription = false;
            TemplateCaretIndex = cursorOffset;
            return;
        }

        // cursorOffset is in the body. The resolver's joined string is
        // subject + (separator) + body, where the separator is whatever
        // the template's body contained between the blank-line / single
        // newline. Recompute by subtracting the subject + separator
        // length we discarded; safer than guessing the separator.
        var fullResolved = string.IsNullOrEmpty(body)
            ? subject
            : (subject + "\n\n" + body); // SplitSubjectAndBody's canonical reform
        // Map the original cursor index into the body half — anything past
        // the joined subject + "\n\n" boundary belongs in the body box.
        var bodyStart = subject.Length + 2; // length of "\n\n"
        TemplateCaretInDescription = true;
        TemplateCaretIndex = Math.Max(0, Math.Min(body.Length, cursorOffset - bodyStart));
        _ = fullResolved; // intentional: kept for clarity in case the layout shifts later
    }
}

/// <summary>
/// Apply request shape. The picker passes one of these to
/// <see cref="WorkingChangesViewModel.ApplyCommitTemplateCommand"/> via
/// command parameter — bundling Template and Mode lets the keyboard path
/// (Enter = Replace) and modifier path (Shift+Enter = Append) share a
/// single command without overloads.
/// </summary>
public sealed record CommitTemplateApplyRequest(
    CommitTemplate Template,
    WorkingChangesViewModel.CommitTemplateApplyMode Mode);
