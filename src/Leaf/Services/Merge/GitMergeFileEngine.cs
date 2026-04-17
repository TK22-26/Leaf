using System.IO;
using System.Text;
using Leaf.Models.Merge;

namespace Leaf.Services.Merge;

/// <summary>
/// Three-way merge engine that delegates the authoritative merge computation to
/// <c>git merge-file --diff-algorithm=histogram --zdiff3</c>. This guarantees
/// Leaf's merge output is byte-identical to <c>git merge</c> on the command line.
/// </summary>
/// <remarks>
/// <para>
/// The engine:
/// <list type="number">
/// <item>Sniffs the input line-ending style (LF vs CRLF) so the composed output
/// can be restored to the user's convention.</item>
/// <item>Normalises all three inputs to LF and writes them as UTF-8 (no BOM) to a
/// unique temporary directory.</item>
/// <item>Invokes <c>git -c core.autocrlf=false merge-file</c> with explicit
/// algorithm and conflict-style flags so repo-level config cannot silently change
/// the result.</item>
/// <item>Parses the stdout through <see cref="ConflictMarkerParser"/>.</item>
/// <item>Computes exact input line ranges for each conflict by walking the merged
/// output in lock-step with the input cursors.</item>
/// <item>Cleans up the temporary directory in a <c>finally</c>, retrying on
/// Windows file-lock races (AV scanners, indexer).</item>
/// </list>
/// </para>
/// </remarks>
public sealed class GitMergeFileEngine : IMergeEngine
{
    private static readonly UTF8Encoding StrictUtf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IGitCommandRunner _gitRunner;
    private readonly string _tempDirRoot;

    // Fire-and-forget cleanup tasks tracked so tests can deterministically drain them.
    // In production nothing awaits this — the caller returns immediately after MergeAsync.
    private readonly List<Task> _pendingCleanupTasks = new();
    private readonly object _cleanupLock = new();

    public GitMergeFileEngine(IGitCommandRunner gitRunner)
        : this(gitRunner, Path.GetTempPath())
    {
    }

    /// <summary>Constructor exposed for tests; allows redirecting the temp directory root.</summary>
    internal GitMergeFileEngine(IGitCommandRunner gitRunner, string tempDirRoot)
    {
        _gitRunner = gitRunner ?? throw new ArgumentNullException(nameof(gitRunner));
        _tempDirRoot = tempDirRoot ?? throw new ArgumentNullException(nameof(tempDirRoot));
    }

    /// <summary>
    /// Test-only: wait for all outstanding fire-and-forget cleanup tasks to complete.
    /// Allows deterministic assertion that temp directories have been reclaimed.
    /// Never throws — cleanup tasks are designed to absorb all exceptions.
    /// </summary>
    internal async Task WaitForPendingCleanupAsync()
    {
        Task[] snapshot;
        lock (_cleanupLock)
        {
            snapshot = _pendingCleanupTasks.ToArray();
            _pendingCleanupTasks.Clear();
        }
        if (snapshot.Length == 0) return;
        try { await Task.WhenAll(snapshot).ConfigureAwait(false); }
        catch { /* cleanup tasks never throw, but be defensive */ }
    }

    public async Task<MergeDocument> MergeAsync(
        string filePath,
        string baseText,
        string oursText,
        string theirsText,
        bool ignoreWhitespace = false,
        string? oursLabel = null,
        string? theirsLabel = null,
        string? baseLabel = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(baseText);
        ArgumentNullException.ThrowIfNull(oursText);
        ArgumentNullException.ThrowIfNull(theirsText);

        cancellationToken.ThrowIfCancellationRequested();

        if (ignoreWhitespace)
        {
            // git merge-file has no --ignore-*-space flags (unlike git diff / git merge).
            // Whitespace-insensitive three-way merge requires pre-normalising all three inputs
            // and then projecting the result back onto the original whitespace — a Phase 2+
            // concern. Surface the limitation loudly so callers can't silently produce wrong
            // merges.
            throw new NotSupportedException(
                "ignoreWhitespace is not supported by the Phase 1 git-merge-file engine. " +
                "Planned for a future phase once the new UI surfaces the option; no existing " +
                "call site sets it to true.");
        }

        var lineEnding = DetectLineEnding(oursText, theirsText, baseText);
        var baseLf = NormaliseToLf(baseText);
        var oursLf = NormaliseToLf(oursText);
        var theirsLf = NormaliseToLf(theirsText);

        var baseLines = SplitLines(baseLf);
        var oursLines = SplitLines(oursLf);
        var theirsLines = SplitLines(theirsLf);

        var tempDir = CreateUniqueTempDir();
        try
        {
            var basePath = Path.Combine(tempDir, "base");
            var oursPath = Path.Combine(tempDir, "ours");
            var theirsPath = Path.Combine(tempDir, "theirs");

            await WriteTempAsync(basePath, baseLf, cancellationToken).ConfigureAwait(false);
            await WriteTempAsync(oursPath, oursLf, cancellationToken).ConfigureAwait(false);
            await WriteTempAsync(theirsPath, theirsLf, cancellationToken).ConfigureAwait(false);

            var args = BuildGitArgs(
                oursPath, basePath, theirsPath,
                oursLabel ?? "ours",
                baseLabel ?? "base",
                theirsLabel ?? "theirs");

            var result = await _gitRunner.RunAsync(tempDir, args, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // git merge-file contract:
            //   exit 0           => no conflicts, clean merge
            //   exit N > 0       => N conflicts were left in the output
            //   exit < 0 / huge  => fatal error
            // Distinguish "clean output with conflict markers" (happy path) from "fatal" by
            // looking at stderr + whether stdout is present. git always emits stdout on success.
            if (string.IsNullOrEmpty(result.StandardOutput) && !string.IsNullOrWhiteSpace(result.StandardError))
            {
                throw new MergeEngineException(
                    $"git merge-file failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
            }

            var mergedLf = result.StandardOutput;
            var parseResult = ConflictMarkerParser.Parse(mergedLf);

            var ranges = BuildRanges(parseResult, baseLines, oursLines, theirsLines);

            return new MergeDocument(
                filePath,
                baseText,
                oursText,
                theirsText,
                mergedLf,
                baseLines,
                oursLines,
                theirsLines,
                parseResult.OutputLines,
                ranges,
                lineEnding,
                parseResult.HasTrailingNewline);
        }
        finally
        {
            // Cleanup must never throw. Fire-and-forget to a background task so the
            // 50-150ms retry ladder doesn't block the caller's async path if the
            // first delete attempt hits a transient file lock (AV, indexer).
            // The task itself catches and logs everything.
            var cleanup = TryDeleteTempDirAsync(tempDir);
            lock (_cleanupLock) { _pendingCleanupTasks.Add(cleanup); }
        }
    }

    private static IReadOnlyList<string> BuildGitArgs(
        string oursPath, string basePath, string theirsPath,
        string oursLabel, string baseLabel, string theirsLabel)
    {
        return new[]
        {
            // Belt-and-braces: ignore any repo-level autocrlf that could silently
            // convert output line endings and break ConflictMarkerParser.
            "-c", "core.autocrlf=false",
            "merge-file",
            "--diff-algorithm=histogram",
            "--zdiff3",
            "-p", // print to stdout
            "-L", oursLabel,
            "-L", baseLabel,
            "-L", theirsLabel,
            oursPath, basePath, theirsPath,
        };
    }

    /// <summary>
    /// Walk the merged output lines in lock-step with the three input cursors,
    /// assigning exact line ranges to each conflict block.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The key invariant: every line in the merged output between conflict blocks is
    /// a line that appears verbatim in <em>both</em> <c>oursLines</c> and
    /// <c>theirsLines</c> at their respective current cursors (auto-merged context)
    /// or in exactly one side (a one-sided change auto-resolved by git). The three
    /// cursors advance accordingly for each merged-output line.
    /// </para>
    /// <para>
    /// Inside a conflict block, ours/theirs/base content is reported by the parser
    /// and appears verbatim at the current cursors of each input. We slice directly
    /// at the cursor position — no search, no ambiguity on repeated content.
    /// </para>
    /// <para>
    /// If the walk diverges from the inputs at any point (invariant broken), we throw
    /// <see cref="MergeEngineException"/> rather than silently producing wrong ranges.
    /// Failing loudly is a non-negotiable part of the engineering-software policy.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ModifiedBaseRange> BuildRanges(
        ConflictMarkerParser.ParseResult parseResult,
        IReadOnlyList<string> baseLines,
        IReadOnlyList<string> oursLines,
        IReadOnlyList<string> theirsLines)
    {
        var conflicts = parseResult.Conflicts;
        if (conflicts.Count == 0)
        {
            return Array.Empty<ModifiedBaseRange>();
        }

        var mergedLines = parseResult.OutputLines;
        var ranges = new List<ModifiedBaseRange>(conflicts.Count);

        int outputIdx = 0;    // 0-based cursor into mergedLines
        int oursCursor = 0;   // 0-based cursor into oursLines
        int theirsCursor = 0; // 0-based cursor into theirsLines
        int baseCursor = 0;   // 0-based cursor into baseLines

        foreach (var conflict in conflicts)
        {
            // Walk auto-merged output lines between the previous position and this conflict's start.
            int conflictStart = conflict.MarkedRange.StartLine - 1;
            while (outputIdx < conflictStart)
            {
                AdvanceCursorsForAutoMergedLine(
                    mergedLines[outputIdx], oursLines, theirsLines, baseLines,
                    ref oursCursor, ref theirsCursor, ref baseCursor,
                    outputIdx + 1);
                outputIdx++;
            }

            // Conflict block: the cursors are now positioned at the start of this region
            // in each input. Validate and carve out ranges directly.
            var oursRange = CarveSlice(oursLines, oursCursor, conflict.OursLines, "ours", conflict.MarkedRange.StartLine);
            var theirsRange = CarveSlice(theirsLines, theirsCursor, conflict.TheirsLines, "theirs", conflict.MarkedRange.StartLine);
            var baseRange = CarveSlice(baseLines, baseCursor, conflict.BaseLines, "base", conflict.MarkedRange.StartLine);

            ranges.Add(new ModifiedBaseRange(
                Index: ranges.Count,
                Base: baseRange,
                Ours: oursRange,
                Theirs: theirsRange,
                ResultMarkedRange: conflict.MarkedRange,
                BaseLines: conflict.BaseLines,
                OursLines: conflict.OursLines,
                TheirsLines: conflict.TheirsLines,
                OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
                TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
                IsConflicting: true,
                IsOrderRelevant: conflict.OursLines.Count > 0 && conflict.TheirsLines.Count > 0,
                OursLabel: conflict.OursLabel,
                BaseLabel: conflict.BaseLabel,
                TheirsLabel: conflict.TheirsLabel));

            // Advance past this block in all four coordinates.
            outputIdx = conflict.MarkedRange.EndLineExclusive - 1;
            oursCursor = oursRange.EndLineExclusive - 1;
            theirsCursor = theirsRange.EndLineExclusive - 1;
            baseCursor = baseRange.EndLineExclusive - 1;
        }

        return ranges;
    }

    /// <summary>
    /// Advance the ours/theirs/base cursors to consume a single auto-merged output line.
    /// Uses a fast-path exact-cursor-match (correct for symmetric edits and repeated content),
    /// falling back to forward-search when the current cursor doesn't match (correct for
    /// one-sided deletions where the other cursor needs to skip past a removed line).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Structural-line defense:</b> if the output line looks like a zdiff3 marker
    /// (<c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c>, <c>|||||||</c>, <c>=======</c>, or
    /// <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c>) and does not appear anywhere at-or-after the
    /// current cursors of ours and theirs, the parser must have misidentified a conflict
    /// block boundary. Git does not emit structural marker lines in auto-merged output
    /// — every such line comes from user content. Failing loudly converts silent corruption
    /// (parser anchors in the wrong place, AcceptOurs emits stray structural lines) into
    /// a user-visible <see cref="MergeEngineException"/> that the VM's engine-error overlay
    /// handles gracefully (Use Ours / Use Theirs / external merge tool).
    /// </para>
    /// </remarks>
    private static void AdvanceCursorsForAutoMergedLine(
        string outputLine,
        IReadOnlyList<string> oursLines,
        IReadOnlyList<string> theirsLines,
        IReadOnlyList<string> baseLines,
        ref int oursCursor,
        ref int theirsCursor,
        ref int baseCursor,
        int outputLineNumber)
    {
        if (LooksLikeMarker(outputLine))
        {
            bool inOurs = ContainsAtOrAfter(oursLines, oursCursor, outputLine);
            bool inTheirs = ContainsAtOrAfter(theirsLines, theirsCursor, outputLine);
            if (!inOurs && !inTheirs)
            {
                throw new MergeEngineException(
                    $"Merge engine output contains a zdiff3 marker line ('{outputLine}') at output " +
                    $"line {outputLineNumber} that does not appear in either ours or theirs at or " +
                    "after the current cursor. The parser likely misidentified a conflict block " +
                    "boundary due to marker-lookalike content in one of the input sides. Resolve " +
                    "this file using 'Use Ours', 'Use Theirs', or an external merge tool.");
            }
        }

        TryAdvanceCursor(ref oursCursor, oursLines, outputLine);
        TryAdvanceCursor(ref theirsCursor, theirsLines, outputLine);
        TryAdvanceCursor(ref baseCursor, baseLines, outputLine);
    }

    /// <summary>
    /// Returns <c>true</c> when the line begins with a seven-character zdiff3 marker run
    /// (<c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c>, <c>|||||||</c>, <c>=======</c>, or
    /// <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c>). The separator marker must be exactly seven
    /// <c>=</c>s with nothing after; openers and closers may carry a label.
    /// </summary>
    private static bool LooksLikeMarker(string line)
    {
        if (line.Length < 7) return false;
        char c = line[0];
        if (c != '<' && c != '>' && c != '|' && c != '=') return false;
        for (int i = 1; i < 7; i++)
        {
            if (line[i] != c) return false;
        }
        if (c == '=' && line.Length != 7) return false;
        if (c != '=' && line.Length > 7 && line[7] != ' ') return false;
        return true;
    }

    private static bool ContainsAtOrAfter(IReadOnlyList<string> lines, int cursor, string target)
    {
        for (int i = cursor; i < lines.Count; i++)
        {
            if (string.Equals(lines[i], target, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static void TryAdvanceCursor(ref int cursor, IReadOnlyList<string> lines, string outputLine)
    {
        // Fast path: exact match at the current cursor (keeps cursors aligned across
        // repeated content and prevents jumps that would skip real occurrences).
        if (cursor < lines.Count && string.Equals(lines[cursor], outputLine, StringComparison.Ordinal))
        {
            cursor++;
            return;
        }

        // Slow path: forward-search from the current cursor. Handles one-sided
        // deletions where the output moved past a line that was removed from this side.
        for (int i = cursor + 1; i < lines.Count; i++)
        {
            if (string.Equals(lines[i], outputLine, StringComparison.Ordinal))
            {
                cursor = i + 1;
                return;
            }
        }
        // Not found anywhere ahead — leave cursor alone. CarveSlice has its own
        // forward-search fallback for conflict-boundary anchoring.
    }

    /// <summary>
    /// Carve a slice of length <c>needle.Count</c> from <paramref name="haystack"/> for a
    /// conflict's side. Tries an exact match at <paramref name="cursor"/> first (correct when
    /// the walker stayed in sync); falls back to forward-search from <paramref name="cursor"/>
    /// (correct for one-sided deletions that leave the cursor behind). Throws only when the
    /// needle cannot be found at or after the cursor — that indicates a real engine-output vs.
    /// input mismatch, not a recoverable condition.
    /// </summary>
    private static LineRange CarveSlice(
        IReadOnlyList<string> haystack,
        int cursor,
        IReadOnlyList<string> needle,
        string sideName,
        int conflictStartOutputLine)
    {
        if (needle.Count == 0)
        {
            return new LineRange(cursor + 1, cursor + 1);
        }

        if (MatchesAt(haystack, cursor, needle))
        {
            return new LineRange(cursor + 1, cursor + 1 + needle.Count);
        }

        var maxStart = haystack.Count - needle.Count;
        for (int i = cursor + 1; i <= maxStart; i++)
        {
            if (MatchesAt(haystack, i, needle))
            {
                return new LineRange(i + 1, i + 1 + needle.Count);
            }
        }

        throw new MergeEngineException(
            $"Merge invariant violated: conflict block at output line {conflictStartOutputLine} " +
            $"reports '{sideName}' content of {needle.Count} line(s) that cannot be located in " +
            $"the '{sideName}' input starting from cursor {cursor + 1}. The engine's output does " +
            "not correspond to its inputs.");
    }

    private static bool MatchesAt(IReadOnlyList<string> haystack, int pos, IReadOnlyList<string> needle)
    {
        if (pos < 0 || pos + needle.Count > haystack.Count) return false;
        for (int j = 0; j < needle.Count; j++)
        {
            if (!string.Equals(haystack[pos + j], needle[j], StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    private static string DetectLineEnding(params string[] candidates)
    {
        // First input that contains "\r\n" wins. Fall back to "\n".
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate) && candidate.Contains("\r\n", StringComparison.Ordinal))
            {
                return "\r\n";
            }
        }
        return "\n";
    }

    private static string NormaliseToLf(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        // Normalise \r\n then stray \r (old Mac style) to \n.
        if (text.IndexOf('\r') < 0) return text;
        var step1 = text.Replace("\r\n", "\n");
        return step1.IndexOf('\r') < 0 ? step1 : step1.Replace('\r', '\n');
    }

    private static IReadOnlyList<string> SplitLines(string lfText)
        => LineSplitter.Split(lfText, out _);

    private string CreateUniqueTempDir()
    {
        var dir = Path.Combine(_tempDirRoot, $"leaf-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static async Task WriteTempAsync(string path, string content, CancellationToken cancellationToken)
    {
        // Explicit stream to guarantee no BOM regardless of .NET defaults.
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream, StrictUtf8NoBom);
        await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Delete the temp directory. On Windows, antivirus and the indexer can briefly
    /// hold file handles open after our process releases them, so we retry with async
    /// backoff. Never throws — a cleanup failure must not abort the merge.
    /// </summary>
    private static async Task TryDeleteTempDirAsync(string dir)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                await Task.Delay(50 * attempt).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                await Task.Delay(50 * attempt).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warn("Merge", $"Failed to clean temp merge dir {dir}: {ex.GetType().Name}: {ex.Message}");
                return;
            }
        }
    }
}
