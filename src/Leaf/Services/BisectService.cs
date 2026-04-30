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
    /// signals bisect has converged.
    /// </summary>
    private static readonly Regex FirstBadRegex = new(
        @"^([0-9a-f]{7,40})\s+is the first bad commit",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

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

        _eventHub.NotifyCommitHistoryChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
        _eventHub.NotifyBranchesChanged();
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
    public async Task<BisectState> GetStateAsync(IRepositorySession session, CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (!await IsBisectInProgressAsync(session, cancellationToken))
        {
            return new BisectState { IsActive = false };
        }
        return await ReadStateAsync(session, stepsHint: null, firstBadHint: null, cancellationToken);
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

        var state = await ReadStateAsync(session, stepsHint, firstBadSha, cancellationToken);

        return new BisectResult
        {
            Success = true,
            IsTerminating = firstBadSha != null,
            FirstBadSha = firstBadSha,
            State = state,
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
        IRepositorySession session, int? stepsHint, string? firstBadHint, CancellationToken cancellationToken)
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
        string sha = string.Empty, shortSha = string.Empty, subject = string.Empty;
        if (headProbe.Success)
        {
            var fields = headProbe.StandardOutput.Trim().Split(FieldSep);
            if (fields.Length >= 3)
            {
                sha = fields[0];
                shortSha = fields[1];
                subject = fields[2];
            }
        }

        return new BisectState
        {
            IsActive = await IsBisectInProgressAsync(session, cancellationToken),
            CurrentSha = sha,
            CurrentShortSha = shortSha,
            CurrentSubject = subject,
            StepsRemaining = stepsHint ?? 0,
            FirstBadSha = firstBadHint,
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
}
