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
                ignoreWhitespace,
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

            var ranges = BuildRanges(parseResult.Conflicts, baseLines, oursLines, theirsLines);

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
            TryDeleteTempDir(tempDir);
        }
    }

    private static IReadOnlyList<string> BuildGitArgs(
        string oursPath, string basePath, string theirsPath,
        bool ignoreWhitespace,
        string oursLabel, string baseLabel, string theirsLabel)
    {
        var args = new List<string>(16)
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
        };
        // Whitespace-sensitivity is handled upfront in MergeAsync; merge-file itself has no such flag.
        _ = ignoreWhitespace;
        args.Add(oursPath);
        args.Add(basePath);
        args.Add(theirsPath);
        return args;
    }

    /// <summary>
    /// Walk the merged output in lock-step with the three input cursors, assigning
    /// real line ranges to each conflict block. The ours/theirs cursors advance as
    /// we encounter auto-merged lines (which are always verbatim from at least one
    /// of those sides). The base cursor is located via content match against the
    /// conflict's base lines, starting from the last known position.
    /// </summary>
    private static IReadOnlyList<ModifiedBaseRange> BuildRanges(
        IReadOnlyList<ConflictMarkerParser.ParsedConflict> conflicts,
        IReadOnlyList<string> baseLines,
        IReadOnlyList<string> oursLines,
        IReadOnlyList<string> theirsLines)
    {
        if (conflicts.Count == 0)
        {
            return Array.Empty<ModifiedBaseRange>();
        }

        var ranges = new List<ModifiedBaseRange>(conflicts.Count);
        int oursCursor = 0;   // 0-based index into oursLines
        int theirsCursor = 0; // 0-based index into theirsLines
        int baseCursor = 0;   // 0-based index into baseLines

        // We don't walk the output literally (we already parsed it); instead we rely on
        // the invariant that inside a conflict block, the ours/theirs/base content is
        // a contiguous verbatim slice of the respective input. Between blocks, auto-merged
        // lines appear in both ours and theirs in some order — we advance cursors by
        // matching from the current position.
        for (int i = 0; i < conflicts.Count; i++)
        {
            var conflict = conflicts[i];

            // Ours range: find conflict.OursLines as a contiguous slice in oursLines starting at oursCursor.
            var oursRange = LocateSlice(oursLines, oursCursor, conflict.OursLines);
            // Theirs range: same for theirsLines starting at theirsCursor.
            var theirsRange = LocateSlice(theirsLines, theirsCursor, conflict.TheirsLines);
            // Base range: same for baseLines starting at baseCursor.
            var baseRange = LocateSlice(baseLines, baseCursor, conflict.BaseLines);

            ranges.Add(new ModifiedBaseRange(
                Index: i,
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
                IsOrderRelevant: conflict.OursLines.Count > 0 && conflict.TheirsLines.Count > 0));

            // Advance cursors past this conflict block so the next search starts after it.
            oursCursor = oursRange.EndLineExclusive - 1;
            theirsCursor = theirsRange.EndLineExclusive - 1;
            baseCursor = baseRange.EndLineExclusive - 1;
        }

        return ranges;
    }

    /// <summary>
    /// Locate <paramref name="needle"/> as a contiguous slice of <paramref name="haystack"/>,
    /// searching forward from <paramref name="startIndex"/>. Returns the 1-based, half-open
    /// <see cref="LineRange"/> of the first match, or an empty range anchored at
    /// <paramref name="startIndex"/> when <paramref name="needle"/> is empty (pure add/delete
    /// on this side). Throws if <paramref name="needle"/> is non-empty but cannot be located —
    /// that would be a consistency failure between parser and input, never a recoverable state.
    /// </summary>
    private static LineRange LocateSlice(IReadOnlyList<string> haystack, int startIndex, IReadOnlyList<string> needle)
    {
        if (needle.Count == 0)
        {
            // Empty slice — anchor at the current cursor.
            return new LineRange(startIndex + 1, startIndex + 1);
        }

        for (int i = startIndex; i <= haystack.Count - needle.Count; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Count; j++)
            {
                if (!string.Equals(haystack[i + j], needle[j], StringComparison.Ordinal))
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                return new LineRange(i + 1, i + 1 + needle.Count);
            }
        }

        throw new MergeEngineException(
            $"Merge-file output refers to a slice of {needle.Count} line(s) that cannot be located in the source " +
            $"starting from line {startIndex + 1}. This indicates a corruption or encoding mismatch between the " +
            "merge engine inputs and its output.");
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
    {
        if (string.IsNullOrEmpty(lfText)) return Array.Empty<string>();

        var hasTrailingNewline = lfText[lfText.Length - 1] == '\n';
        var raw = lfText.Split('\n');
        var count = hasTrailingNewline && raw.Length > 0 && raw[^1].Length == 0 ? raw.Length - 1 : raw.Length;
        var lines = new string[count];
        Array.Copy(raw, lines, count);
        return lines;
    }

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
    /// hold file handles open after our process releases them, so we retry. Never
    /// throw — a cleanup failure must not abort the merge.
    /// </summary>
    private static void TryDeleteTempDir(string dir)
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
                Thread.Sleep(50 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(50 * attempt);
            }
            catch (Exception ex)
            {
                Log.Warn("Merge", $"Failed to clean temp merge dir {dir}: {ex.GetType().Name}: {ex.Message}");
                return;
            }
        }
    }
}
