using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Default <see cref="IBisectService"/>. Pure CLI via
/// <see cref="IGitCommandRunner"/> — <c>git bisect</c> fires the
/// <c>post-checkout</c> hook on every step, so LibGit2Sharp (which
/// bypasses hooks) is not an option. State is read from the standard
/// <c>BISECT_*</c> sentinels under the resolved git directory; we use
/// <see cref="IRepositorySession.GitDirectory"/> rather than reasoning
/// about <c>repoPath/.git</c> ourselves so linked worktrees work too.
/// </summary>
public class BisectService : IBisectService
{
    /// <summary>
    /// Regex over git's "Bisecting: N revisions left to test after this
    /// (roughly K steps)" line. We only capture K (the visible step
    /// count) since N is just K-1 / log redundancy.
    /// </summary>
    private static readonly Regex BisectingProgressRegex = new(
        @"\(roughly\s+(\d+)\s+steps?\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Regex over git's "&lt;sha&gt; is the first bad commit" line that
    /// signals bisect has converged. Requires the SHA at column 0
    /// (Multiline) and the literal " is the first bad commit" tail
    /// followed by end-of-line — without the EOL anchor a commit subject
    /// containing the phrase could false-positive. We also require the
    /// canonical 40-char hex form git emits, not the 7+ short form, so
    /// stray short SHAs in subjects can't trip it.
    /// </summary>
    private static readonly Regex FirstBadRegex = new(
        @"^([0-9a-f]{40}) is the first bad commit$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    /// <summary>
    /// Regex over git's "There are only 'skip'ped commits left to test"
    /// terminal line. Bisect can't narrow further when every untested
    /// commit was skipped — we surface this as a non-converging-but-done
    /// state so the banner stops asking for verdicts.
    /// </summary>
    private static readonly Regex AllSkippedRegex = new(
        @"There are only 'skip'ped commits left to test",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IGitCommandRunner _commandRunner;
    private readonly IRepositoryEventHub _eventHub;

    public BisectService(IGitCommandRunner commandRunner, IRepositoryEventHub eventHub)
    {
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
        _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
    }

    /// <inheritdoc />
    public async Task<BisectResult> StartAsync(
        IRepositorySession session,
        string badCommitSha,
        string goodCommitSha,
        CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (string.IsNullOrWhiteSpace(badCommitSha))
            throw new ArgumentException("badCommitSha is required.", nameof(badCommitSha));
        if (string.IsNullOrWhiteSpace(goodCommitSha))
            throw new ArgumentException("goodCommitSha is required.", nameof(goodCommitSha));

        Log.Info("Bisect", $"Start: bad={badCommitSha} good={goodCommitSha}");
        var result = await _commandRunner.RunAsync(
            session.RepositoryPath,
            ["bisect", "start", badCommitSha, goodCommitSha],
            cancellationToken: cancellationToken);

        return await BuildResultAsync(session, result, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<BisectResult> MarkAsync(
        IRepositorySession session,
        BisectVerdict verdict,
        CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));

        var verb = verdict switch
        {
            BisectVerdict.Good => "good",
            BisectVerdict.Bad => "bad",
            BisectVerdict.Skip => "skip",
            _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, null),
        };

        Log.Info("Bisect", $"Mark: {verb}");
        var result = await _commandRunner.RunAsync(
            session.RepositoryPath,
            ["bisect", verb],
            cancellationToken: cancellationToken);

        return await BuildResultAsync(session, result, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<BisectResult> ResetAsync(
        IRepositorySession session,
        CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));

        Log.Info("Bisect", "Reset");
        var result = await _commandRunner.RunAsync(
            session.RepositoryPath,
            ["bisect", "reset"],
            cancellationToken: cancellationToken);

        // Reset moves HEAD back to its pre-bisect ref. That doesn't
        // mutate any branch — bisect never wrote to refs/heads — so we
        // skip NotifyBranchesChanged. CommitHistory + WorkingDirectory +
        // ConflictState are enough; the branch sidebar already reflects
        // the new HEAD ref via the commit-history refresh.
        _eventHub.NotifyCommitHistoryChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
        _eventHub.NotifyConflictStateChanged();

        if (!result.Success)
        {
            Log.Error("Bisect", $"Reset failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
            return new BisectResult
            {
                Success = false,
                ErrorMessage = string.IsNullOrWhiteSpace(result.StandardError)
                    ? $"git bisect reset exited with code {result.ExitCode}."
                    : result.StandardError.Trim(),
            };
        }

        return new BisectResult { Success = true, State = new BisectState { IsActive = false } };
    }

    /// <inheritdoc />
    public Task<bool> IsBisectInProgressAsync(IRepositorySession session, CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        // BISECT_START is the canonical "bisect in progress" marker — git
        // writes the saved-HEAD ref name here on `bisect start` and
        // removes it on `bisect reset`. BISECT_LOG can linger after a
        // crashed bisect; BISECT_START is more reliable.
        var path = Path.Combine(session.GitDirectory, "BISECT_START");
        return Task.FromResult(File.Exists(path));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BisectLogEntry>> GetLogAsync(
        IRepositorySession session, CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (!await IsBisectInProgressAsync(session, cancellationToken))
        {
            return Array.Empty<BisectLogEntry>();
        }

        var probe = await _commandRunner.RunAsync(
            session.RepositoryPath, ["bisect", "log"], cancellationToken: cancellationToken);
        if (!probe.Success)
        {
            Log.Info("Bisect", $"GetLog: bisect log failed (exit {probe.ExitCode}); returning empty.");
            return Array.Empty<BisectLogEntry>();
        }
        return ParseLog(probe.StandardOutput);
    }

    /// <inheritdoc />
    public async Task<BisectResult> UndoLastVerdictAsync(
        IRepositorySession session, CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (!await IsBisectInProgressAsync(session, cancellationToken))
        {
            return new BisectResult
            {
                Success = false,
                ErrorMessage = "No bisect is in progress.",
            };
        }

        // Step 1: capture current log.
        var logProbe = await _commandRunner.RunAsync(
            session.RepositoryPath, ["bisect", "log"], cancellationToken: cancellationToken);
        if (!logProbe.Success)
        {
            return new BisectResult
            {
                Success = false,
                ErrorMessage = "Could not read bisect log; refusing to undo.",
            };
        }

        // Step 2: drop the last `git bisect good|bad|skip` command line.
        // Comment lines (starting with #) are descriptive and don't affect
        // replay state — only the bare `git bisect <verb> <sha>` lines do.
        var truncated = TruncateLastVerdict(logProbe.StandardOutput);
        if (truncated == null)
        {
            return new BisectResult
            {
                Success = false,
                ErrorMessage = "Nothing to undo — no verdicts have been issued in this bisect yet.",
            };
        }

        // Step 3: write the truncated log to a temp file, reset the
        // bisect, replay. Atomic from the user's perspective even
        // though it's three commands underneath.
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"leaf-bisect-replay-{Guid.NewGuid():N}.log");
        try
        {
            await File.WriteAllTextAsync(tempPath, truncated, cancellationToken);

            var resetResult = await _commandRunner.RunAsync(
                session.RepositoryPath, ["bisect", "reset"], cancellationToken: cancellationToken);
            if (!resetResult.Success)
            {
                Log.Error("Bisect", $"Undo: reset failed (exit {resetResult.ExitCode}): {resetResult.StandardError.Trim()}");
                return new BisectResult
                {
                    Success = false,
                    ErrorMessage = "Could not reset bisect during undo. The bisect state may be in a partial state — try aborting and starting fresh.",
                };
            }

            var replayResult = await _commandRunner.RunAsync(
                session.RepositoryPath, ["bisect", "replay", tempPath], cancellationToken: cancellationToken);

            // BuildResultAsync also fires event-hub notifications and
            // parses the standard "first bad commit" / "Bisecting:" lines.
            return await BuildResultAsync(session, replayResult, cancellationToken);
        }
        finally
        {
            try { File.Delete(tempPath); }
            catch (IOException) { /* best-effort */ }
            catch (UnauthorizedAccessException) { /* best-effort */ }
        }
    }

    /// <inheritdoc />
    public async Task<BisectState> GetStateAsync(IRepositorySession session, CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (!await IsBisectInProgressAsync(session, cancellationToken))
        {
            return new BisectState { IsActive = false };
        }

        // If the bisect already converged on a previous run and the user
        // hasn't reset yet, BISECT_LOG carries a "# first bad commit:
        // [<sha>] ..." trailer. Without this, a cold open would always
        // show the "Testing X" banner with no steps remaining — even
        // when bisect has nothing left to do. We read the log file
        // best-effort; failure falls through to the regular probe.
        var converged = TryReadConvergedShaFromLog(session.GitDirectory);
        // Cold-open path: a converged SHA in BISECT_LOG implies a normal
        // termination, not the all-skipped variant; the latter has no
        // first-bad SHA to record so the log wouldn't carry one anyway.
        return await ReadStateAsync(
            session, stepsHint: null, firstBadHint: converged,
            allSkippedHint: false, cancellationToken);
    }

    /// <summary>
    /// Read the converging "first bad commit" SHA out of <c>.git/BISECT_LOG</c>
    /// when a prior run converged but the user hasn't called
    /// <c>git bisect reset</c> yet. Returns null when the log doesn't
    /// exist, can't be read, or doesn't contain the trailer.
    /// </summary>
    private static string? TryReadConvergedShaFromLog(string gitDirectory)
    {
        try
        {
            var path = Path.Combine(gitDirectory, "BISECT_LOG");
            if (!File.Exists(path)) return null;

            // The trailer git writes on convergence:
            //   # first bad commit: [<sha>] <subject>
            // We tolerate either a leading `#` comment or no comment
            // marker since older gits varied the form.
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.TrimStart('#', ' ');
                if (!trimmed.StartsWith("first bad commit:", StringComparison.OrdinalIgnoreCase)) continue;
                var open = trimmed.IndexOf('[');
                var close = trimmed.IndexOf(']');
                if (open < 0 || close <= open) continue;
                var sha = trimmed[(open + 1)..close].Trim();
                if (sha.Length is >= 7 and <= 40 && sha.All(c => Uri.IsHexDigit(c))) return sha;
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info("Bisect", $"Could not read BISECT_LOG: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Translate a raw <c>git bisect</c> command result into a
    /// <see cref="BisectResult"/>: parse the "first bad commit" terminator
    /// from stdout, parse the "roughly K steps" hint, and compose the
    /// updated <see cref="BisectState"/>. Fires repository event hub
    /// notifications so the graph + status pane refresh.
    /// </summary>
    private async Task<BisectResult> BuildResultAsync(
        IRepositorySession session, GitCommandResult result, CancellationToken cancellationToken)
    {
        // Bisect mutates HEAD on every step (start / good / bad / skip),
        // so we always notify even on failure — the caller's UI will
        // re-read state and clear stale banners. Tests are signed off
        // by these notifications too.
        _eventHub.NotifyCommitHistoryChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
        _eventHub.NotifyConflictStateChanged();

        if (!result.Success)
        {
            Log.Error("Bisect", $"Command failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
            return new BisectResult
            {
                Success = false,
                ErrorMessage = string.IsNullOrWhiteSpace(result.StandardError)
                    ? $"git bisect exited with code {result.ExitCode}."
                    : result.StandardError.Trim(),
            };
        }

        var combined = result.StandardOutput + "\n" + result.StandardError;
        var firstBadSha = ParseFirstBadSha(combined);
        var stepsHint = ParseStepsRemaining(combined);

        // "All skipped" is a real terminal state: git can't narrow the
        // search further because every remaining candidate was skipped.
        // Treat it as terminating with no FirstBadSha — the banner
        // surfaces the situation and stops asking for verdicts.
        var isAllSkipped = firstBadSha == null && IsAllSkippedTerminator(combined);

        var state = await ReadStateAsync(
            session, stepsHint, firstBadSha,
            allSkippedHint: isAllSkipped, cancellationToken);

        return new BisectResult
        {
            Success = true,
            IsTerminating = firstBadSha != null || isAllSkipped,
            FirstBadSha = firstBadSha,
            State = state,
            ErrorMessage = isAllSkipped
                ? "Bisect ended: every remaining candidate was skipped — the first bad commit could not be narrowed further."
                : null,
        };
    }

    /// <summary>
    /// Read HEAD-anchored state for the current bisect: the checked-out
    /// commit's sha + subject, plus optional hints from the most recent
    /// command's stdout. Both hints are best-effort — we always fall
    /// back to a fresh probe so the returned state is honest about
    /// what's on disk now, not what we hoped a few milliseconds ago.
    /// </summary>
    private async Task<BisectState> ReadStateAsync(
        IRepositorySession session, int? stepsHint, string? firstBadHint, bool allSkippedHint, CancellationToken cancellationToken)
    {
        var headProbe = await _commandRunner.RunAsync(
            session.RepositoryPath,
            ["log", "-1", "--pretty=format:%H%x1F%h%x1F%s"],
            cancellationToken: cancellationToken);

        // %x1F is git's format escape for the U+001F unit-separator byte —
        // an ASCII control char that cannot appear in a commit subject,
        // so the split is robust against subjects containing whitespace,
        // pipes, quotes, etc.
        const char FieldSep = (char)0x1F;
        if (!headProbe.Success)
        {
            // git log -1 should always succeed during an active bisect —
            // BISECT_START existing implies HEAD is checked out at a real
            // commit. A failure here means git's behaving abnormally
            // (broken HEAD, repo lock, permission issue). Per engineering
            // policy: fail loud. The caller surfaces the message.
            throw new InvalidOperationException(
                $"Bisect: git log -1 failed during state read (exit {headProbe.ExitCode}): " +
                (string.IsNullOrWhiteSpace(headProbe.StandardError)
                    ? "no stderr"
                    : headProbe.StandardError.Trim()));
        }
        // We allow an empty subject (--allow-empty-message commits are
        // legal) but require the SHA fields to be present. Three fields
        // even when the third is empty look like "abc1F1234567x1Fx1F".
        // Using StringSplitOptions.None preserves trailing empties.
        var fields = headProbe.StandardOutput.TrimEnd('\r', '\n').Split(FieldSep, StringSplitOptions.None);
        if (fields.Length < 3 || string.IsNullOrEmpty(fields[0]))
        {
            throw new InvalidOperationException(
                "Bisect: could not parse current HEAD information from git log output.");
        }
        var sha = fields[0];
        var shortSha = fields[1];
        var subject = fields[2];

        return new BisectState
        {
            IsActive = await IsBisectInProgressAsync(session, cancellationToken),
            CurrentSha = sha,
            CurrentShortSha = shortSha,
            CurrentSubject = subject,
            StepsRemaining = stepsHint,
            FirstBadSha = firstBadHint,
            AllSkippedTerminator = allSkippedHint,
        };
    }

    /// <summary>
    /// Pull the SHA out of git's terminating "&lt;sha&gt; is the first
    /// bad commit" line. Exposed as <c>internal static</c> so the parser
    /// can be unit-tested without needing an <see cref="IGitCommandRunner"/>.
    /// </summary>
    internal static string? ParseFirstBadSha(string output)
    {
        if (string.IsNullOrEmpty(output)) return null;
        var m = FirstBadRegex.Match(output);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// Pull the "(roughly K steps)" hint out of git's "Bisecting:" status
    /// line. Returns null when the hint isn't present (start, terminating
    /// step, or one-step-remaining when git omits the parenthetical).
    /// </summary>
    internal static int? ParseStepsRemaining(string output)
    {
        if (string.IsNullOrEmpty(output)) return null;
        var m = BisectingProgressRegex.Match(output);
        if (!m.Success) return null;
        return int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var k)
            ? k : null;
    }

    /// <summary>
    /// Detect git's "There are only 'skip'ped commits left to test"
    /// terminator. Distinct from <see cref="ParseFirstBadSha"/> because
    /// no specific commit was identified — bisect just gave up.
    /// </summary>
    internal static bool IsAllSkippedTerminator(string output)
    {
        if (string.IsNullOrEmpty(output)) return false;
        return AllSkippedRegex.IsMatch(output);
    }

    /// <summary>
    /// Parse <c>git bisect log</c> output into the user-driven verdict
    /// list, most-recent first. The format is well-defined: each verdict
    /// is a comment line of the form <c># good: [&lt;sha&gt;] &lt;subject&gt;</c>
    /// (or <c>bad</c> / <c>skip</c>) followed by the actual command line
    /// <c>git bisect &lt;verb&gt; &lt;sha&gt;</c>. We pair each comment with its
    /// command so we have the full SHA + subject in one record.
    /// </summary>
    /// <remarks>
    /// The first two comment lines (the bookend <c># bad:</c> and
    /// <c># good:</c> from <c>git bisect start</c>) describe the search
    /// range, not user verdicts — they're skipped here because the
    /// banner already shows that information separately and a "log"
    /// of two pre-loaded items would just be noise.
    /// </remarks>
    internal static IReadOnlyList<BisectLogEntry> ParseLog(string output)
    {
        if (string.IsNullOrEmpty(output)) return Array.Empty<BisectLogEntry>();

        var lines = output.Split('\n');
        var entries = new List<BisectLogEntry>();
        bool seenStart = false;
        BisectVerdict? pendingVerdict = null;
        string pendingSha = string.Empty;
        string pendingSubject = string.Empty;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r', ' ', '\t');
            if (line.Length == 0) continue;

            // The "git bisect start" command marks the boundary between
            // the bookend descriptors and the verdict history. Anything
            // before it is just the range we set up; anything after is
            // user-driven.
            if (line.StartsWith("git bisect start", StringComparison.Ordinal))
            {
                seenStart = true;
                continue;
            }
            if (!seenStart) continue;

            if (line.StartsWith("# good: [", StringComparison.Ordinal))
            {
                pendingVerdict = BisectVerdict.Good;
                (pendingSha, pendingSubject) = ParseLogCommentBody(line, "# good: ");
            }
            else if (line.StartsWith("# bad: [", StringComparison.Ordinal))
            {
                pendingVerdict = BisectVerdict.Bad;
                (pendingSha, pendingSubject) = ParseLogCommentBody(line, "# bad: ");
            }
            else if (line.StartsWith("# skip: [", StringComparison.Ordinal))
            {
                pendingVerdict = BisectVerdict.Skip;
                (pendingSha, pendingSubject) = ParseLogCommentBody(line, "# skip: ");
            }
            else if (line.StartsWith("git bisect good ", StringComparison.Ordinal)
                  || line.StartsWith("git bisect bad ", StringComparison.Ordinal)
                  || line.StartsWith("git bisect skip ", StringComparison.Ordinal))
            {
                // The command line confirms the verdict. We pair it with
                // the preceding comment (which gave us the subject); if
                // a comment was missing for some reason (newer git
                // versions, custom log) we still record the verdict
                // with a blank subject rather than dropping it.
                if (pendingVerdict.HasValue && !string.IsNullOrEmpty(pendingSha))
                {
                    var shortSha = pendingSha.Length >= 7 ? pendingSha[..7] : pendingSha;
                    entries.Add(new BisectLogEntry(pendingVerdict.Value, pendingSha, shortSha, pendingSubject));
                }
                pendingVerdict = null;
                pendingSha = string.Empty;
                pendingSubject = string.Empty;
            }
        }

        // Most-recent first matches the visual idiom (Undo applies to the
        // top row); git emits them oldest-first.
        entries.Reverse();
        return entries;
    }

    /// <summary>
    /// Pull the SHA and subject out of a <c># good: [&lt;sha&gt;] &lt;subject&gt;</c>
    /// log comment. Returns empty fields on a malformed line rather than
    /// throwing — we'd rather drop the row than blow up the whole log.
    /// </summary>
    private static (string sha, string subject) ParseLogCommentBody(string line, string prefix)
    {
        // After the prefix we expect "[<sha>] <subject>".
        var rest = line[prefix.Length..];
        var open = rest.IndexOf('[');
        var close = rest.IndexOf(']');
        if (open != 0 || close <= 0) return (string.Empty, string.Empty);
        var sha = rest[1..close];
        var subject = close + 1 < rest.Length ? rest[(close + 1)..].TrimStart() : string.Empty;
        return (sha, subject);
    }

    /// <summary>
    /// Drop the last <c>git bisect good/bad/skip</c> command from a
    /// <c>git bisect log</c> output, returning the truncated log
    /// suitable for <c>git bisect replay</c>. Returns null when no
    /// verdict commands are present (bookends only) — the caller
    /// surfaces that as "nothing to undo." Comment lines and the
    /// <c>git bisect start</c> command are preserved.
    /// </summary>
    internal static string? TruncateLastVerdict(string log)
    {
        if (string.IsNullOrEmpty(log)) return null;

        // We retain every line up to (but not including) the LAST
        // `git bisect <verb> <sha>` line, plus everything else that
        // came after that line that isn't itself a verdict command.
        // Practically that just means dropping the last `git bisect
        // good/bad/skip <sha>` line; preceding lines are unchanged.
        var lines = log.Split('\n').ToList();
        int lastVerdictIdx = -1;
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            var trimmed = lines[i].TrimEnd('\r', ' ', '\t');
            if (trimmed.StartsWith("git bisect good ", StringComparison.Ordinal)
             || trimmed.StartsWith("git bisect bad ", StringComparison.Ordinal)
             || trimmed.StartsWith("git bisect skip ", StringComparison.Ordinal))
            {
                lastVerdictIdx = i;
                break;
            }
        }
        if (lastVerdictIdx < 0) return null;
        lines.RemoveAt(lastVerdictIdx);
        return string.Join("\n", lines);
    }
}
