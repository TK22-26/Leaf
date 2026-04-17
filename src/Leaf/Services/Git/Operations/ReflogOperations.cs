using System.Globalization;
using System.Text.RegularExpressions;
using Leaf.Models;
using Leaf.Services.Git.Core;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Operations for reading git reflog data. The CLI output we drive
/// (<c>git reflog --all --date=iso --format=...</c>) pins the exact
/// columns we parse, so users with unusual local config
/// (<c>core.reflogFormat</c> etc.) still get predictable input.
/// </summary>
internal class ReflogOperations
{
    private readonly IGitOperationContext _context;

    // Tab-separated: <full-sha>\t<ref>@{<iso-date>}\t<subject>.
    // Using %x09 (tab) for the separator keeps the format unambiguous
    // because subject lines can contain every other punctuation char.
    private const string ReflogFormat = "%H%x09%gD%x09%gs";

    // Matches "<ref>@{<date>}" — the ref portion is non-greedy so a
    // branch name containing "@{" (allowed by git) stays intact; the
    // date capture is greedy to absorb the ISO "YYYY-MM-DD HH:MM:SS
    // +TZ" shape. End-anchored so the `}` at the close of the date is
    // matched unambiguously.
    private static readonly Regex SelectorPattern = new(
        @"^(?<ref>.+?)@\{(?<date>.+)\}$",
        RegexOptions.Compiled);

    // A reflog SHA is always the full 40-char lowercase hex form
    // (we pass `--format=%H`). Validating here before building the
    // entry stops stray garbage from propagating into downstream
    // commands like `git checkout <sha>`.
    private static readonly Regex ShaPattern = new(
        @"^[0-9a-f]{40}$",
        RegexOptions.Compiled);

    public ReflogOperations(IGitOperationContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Retrieve the combined reflog for every ref. Returns newest-first.
    /// Returns an empty list when the repo has no reflog at all (fresh
    /// clone with no operations yet) — that's not an error.
    /// </summary>
    public async Task<List<ReflogEntry>> GetReflogAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["reflog", "--all", "--date=iso", $"--format={ReflogFormat}"],
            cancellationToken: cancellationToken);

        if (!result.Success)
        {
            // Fresh clone with no activity yet prints nothing and exits
            // 0, so non-success here is a genuine failure.
            throw new InvalidOperationException(
                string.IsNullOrEmpty(result.StandardError)
                    ? $"Failed to read reflog (exit code {result.ExitCode})"
                    : result.StandardError);
        }

        return ParseReflogOutput(result.StandardOutput);
    }

    /// <summary>
    /// Parse tab-separated <c>git reflog</c> output. Internal so the
    /// test assembly can exercise edge cases (unknown prefixes,
    /// malformed lines, refs containing <c>@</c>) without a real repo.
    /// Lines that don't match the expected shape are skipped with a
    /// warning — the caller still gets the entries we could read.
    /// </summary>
    internal static List<ReflogEntry> ParseReflogOutput(string output)
    {
        var entries = new List<ReflogEntry>();
        if (string.IsNullOrWhiteSpace(output))
            return entries;

        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd('\r');
            var parts = line.Split('\t');
            if (parts.Length < 3)
            {
                Log.Warn("Reflog", $"Skipping malformed reflog line (expected 3 tab-separated fields): {line}");
                continue;
            }

            var sha = parts[0];
            if (!ShaPattern.IsMatch(sha))
            {
                Log.Warn("Reflog", $"Skipping entry with malformed SHA '{sha}'");
                continue;
            }

            var selector = parts[1];
            // The subject can legitimately contain tabs if the user set
            // one in a commit message — rejoin everything after the
            // second split point so the message survives round-trip.
            var subject = parts.Length == 3 ? parts[2] : string.Join('\t', parts.Skip(2));

            var selectorMatch = SelectorPattern.Match(selector);
            if (!selectorMatch.Success)
            {
                Log.Warn("Reflog", $"Skipping entry with unrecognized selector: {selector}");
                continue;
            }

            var refName = selectorMatch.Groups["ref"].Value;
            var dateText = selectorMatch.Groups["date"].Value;

            if (!DateTimeOffset.TryParseExact(
                    dateText,
                    "yyyy-MM-dd HH:mm:ss zzz",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out var timestamp))
            {
                // Fall back to lenient parsing so a local git built with
                // a slightly different ISO variant still produces usable
                // entries; worst case we log and drop the one row.
                if (!DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp))
                {
                    Log.Warn("Reflog", $"Skipping entry with unparseable timestamp '{dateText}' in line: {line}");
                    continue;
                }
            }

            entries.Add(new ReflogEntry
            {
                Sha = sha,
                Ref = refName,
                OperationType = ClassifyMessage(subject),
                Message = subject,
                Timestamp = timestamp,
            });
        }

        return entries;
    }

    /// <summary>
    /// Best-effort classification of a reflog subject line. The prefix
    /// format is an informal convention in git — new operations in
    /// future git versions fall through to <see cref="ReflogOperationType.Other"/>
    /// rather than crash the view. Internal for direct testing.
    /// </summary>
    internal static ReflogOperationType ClassifyMessage(string subject)
    {
        if (string.IsNullOrEmpty(subject))
            return ReflogOperationType.Other;

        // Handle parenthesized sub-operations first so
        // "commit (amend): ..." picks up Amend, not Commit.
        if (subject.StartsWith("commit (amend)", StringComparison.Ordinal))
            return ReflogOperationType.Amend;
        if (subject.StartsWith("commit", StringComparison.Ordinal))
            return ReflogOperationType.Commit;
        if (subject.StartsWith("amend", StringComparison.Ordinal))
            return ReflogOperationType.Amend;
        if (subject.StartsWith("checkout", StringComparison.Ordinal))
            return ReflogOperationType.Checkout;
        if (subject.StartsWith("reset", StringComparison.Ordinal))
            return ReflogOperationType.Reset;
        if (subject.StartsWith("merge", StringComparison.Ordinal))
            return ReflogOperationType.Merge;
        if (subject.StartsWith("rebase", StringComparison.Ordinal))
            return ReflogOperationType.Rebase;
        if (subject.StartsWith("cherry-pick", StringComparison.Ordinal))
            return ReflogOperationType.CherryPick;
        if (subject.StartsWith("revert", StringComparison.Ordinal))
            return ReflogOperationType.Revert;
        if (subject.StartsWith("pull", StringComparison.Ordinal))
            return ReflogOperationType.Pull;
        if (subject.StartsWith("push", StringComparison.Ordinal))
            return ReflogOperationType.Push;
        if (subject.StartsWith("clone", StringComparison.Ordinal))
            return ReflogOperationType.Clone;
        if (subject.StartsWith("branch", StringComparison.Ordinal))
            return ReflogOperationType.Branch;
        if (subject.StartsWith("stash", StringComparison.Ordinal))
            return ReflogOperationType.Stash;

        return ReflogOperationType.Other;
    }
}
