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
            _ = TryDeleteTempDirAsync(tempDir);
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
    /// Every auto-merged line must appear at the current cursor position of at least one
    /// of ours/theirs (and possibly base, if unchanged). Breaking this invariant is a
    /// fatal engine error.
    /// </summary>
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
        bool matchesOurs = oursCursor < oursLines.Count
            && string.Equals(oursLines[oursCursor], outputLine, StringComparison.Ordinal);
        bool matchesTheirs = theirsCursor < theirsLines.Count
            && string.Equals(theirsLines[theirsCursor], outputLine, StringComparison.Ordinal);

        if (!matchesOurs && !matchesTheirs)
        {
            throw new MergeEngineException(
                $"Merge invariant violated at output line {outputLineNumber}: auto-merged line " +
                "appears in neither ours nor theirs at the current cursor positions. " +
                "The engine output does not correspond to its inputs.");
        }

        if (matchesOurs) oursCursor++;
        if (matchesTheirs) theirsCursor++;

        // Base advances when the line is unchanged vs. base on at least one side —
        // i.e. the same line exists at baseCursor. When both sides modified context
        // identically we still advance base. When only one side modified and the
        // other carried base verbatim, base advances iff the carried line matches.
        if (baseCursor < baseLines.Count
            && string.Equals(baseLines[baseCursor], outputLine, StringComparison.Ordinal))
        {
            baseCursor++;
        }
    }

    /// <summary>
    /// Carve a slice of length <c>needle.Count</c> from <paramref name="haystack"/>
    /// starting at <paramref name="cursor"/>, verifying content match. Throws on mismatch.
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

        if (cursor + needle.Count > haystack.Count)
        {
            throw new MergeEngineException(
                $"Merge invariant violated: conflict block at output line {conflictStartOutputLine} " +
                $"reports {needle.Count} line(s) of '{sideName}' content, but only " +
                $"{haystack.Count - cursor} line(s) remain in the '{sideName}' input at cursor {cursor + 1}.");
        }

        for (int j = 0; j < needle.Count; j++)
        {
            if (!string.Equals(haystack[cursor + j], needle[j], StringComparison.Ordinal))
            {
                throw new MergeEngineException(
                    $"Merge invariant violated: conflict block at output line {conflictStartOutputLine} " +
                    $"reports '{sideName}' content that does not match the '{sideName}' input at cursor " +
                    $"{cursor + j + 1}. Engine output does not correspond to its inputs.");
            }
        }

        return new LineRange(cursor + 1, cursor + 1 + needle.Count);
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
