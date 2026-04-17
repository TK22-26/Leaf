using System.Text.RegularExpressions;
using Leaf.Models.Merge;

namespace Leaf.Services.Merge;

/// <summary>
/// Parses zdiff3-formatted conflict markers as emitted by
/// <c>git merge-file --zdiff3</c>. The input is the raw merged output; the output
/// is the list of parsed conflict blocks in document order along with their line
/// ranges in the input.
/// </summary>
/// <remarks>
/// zdiff3 marker syntax (seven-character runs at the start of a line):
/// <code>
/// &lt;&lt;&lt;&lt;&lt;&lt;&lt; ours_label
/// ours content ...
/// ||||||| base_label
/// base content ...
/// =======
/// theirs content ...
/// &gt;&gt;&gt;&gt;&gt;&gt;&gt; theirs_label
/// </code>
/// The parser ignores the trailing labels (they are informational only) and
/// captures the inner content runs. Nested or malformed markers raise
/// <see cref="MergeEngineException"/> — we surface the problem rather than silently
/// mis-resolving conflicts.
/// </remarks>
public static class ConflictMarkerParser
{
    private static readonly Regex OursOpen = new(@"^<{7}(\s.*)?$", RegexOptions.Compiled);
    private static readonly Regex BaseMiddle = new(@"^\|{7}(\s.*)?$", RegexOptions.Compiled);
    private static readonly Regex Separator = new(@"^={7}$", RegexOptions.Compiled);
    private static readonly Regex TheirsClose = new(@"^>{7}(\s.*)?$", RegexOptions.Compiled);

    /// <summary>
    /// A single conflict block found in the merged output.
    /// </summary>
    /// <param name="MarkedRange">
    /// Line range in the merged output covering the entire conflict block, from the opening
    /// <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> line through the closing <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c> line inclusive.
    /// 1-based, half-open end.
    /// </param>
    /// <param name="OursLines">Content between the opening marker and the base separator.</param>
    /// <param name="BaseLines">Content between the base separator and the <c>=======</c> line.</param>
    /// <param name="TheirsLines">Content between the <c>=======</c> line and the closing marker.</param>
    public sealed record ParsedConflict(
        LineRange MarkedRange,
        IReadOnlyList<string> OursLines,
        IReadOnlyList<string> BaseLines,
        IReadOnlyList<string> TheirsLines);

    /// <summary>
    /// Result of parsing a merged output.
    /// </summary>
    /// <param name="OutputLines">
    /// The input split into lines (no trailing CR, no trailing LF). Lines are 1-based via the
    /// <see cref="ParsedConflict.MarkedRange"/>.
    /// </param>
    /// <param name="HasTrailingNewline">Whether the input ended with a newline.</param>
    /// <param name="Conflicts">Conflict blocks in document order.</param>
    public sealed record ParseResult(
        IReadOnlyList<string> OutputLines,
        bool HasTrailingNewline,
        IReadOnlyList<ParsedConflict> Conflicts);

    /// <summary>
    /// Parse a zdiff3-formatted merged output.
    /// </summary>
    /// <exception cref="MergeEngineException">
    /// Markers are nested, missing the expected base/separator/closing section, or otherwise
    /// malformed. Failing loudly is intentional — silently ignoring a broken conflict block
    /// would produce an incorrect merge.
    /// </exception>
    public static ParseResult Parse(string mergedOutput)
    {
        ArgumentNullException.ThrowIfNull(mergedOutput);

        if (mergedOutput.Length == 0)
        {
            return new ParseResult(Array.Empty<string>(), HasTrailingNewline: false, Array.Empty<ParsedConflict>());
        }

        var hasTrailingNewline = mergedOutput[mergedOutput.Length - 1] == '\n';

        // Split into lines without any line-ending bytes. Normalise CRLF defensively even
        // though the engine invocation passes `-c core.autocrlf=false` — a corrupt repo
        // config could still inject CRs and we must not leak them downstream.
        var normalized = mergedOutput.Replace("\r\n", "\n").Replace("\r", "\n");
        var rawLines = normalized.Split('\n');
        // When the text ends with '\n', Split produces a trailing empty element we must drop.
        var lineCount = hasTrailingNewline && rawLines.Length > 0 && rawLines[^1].Length == 0
            ? rawLines.Length - 1
            : rawLines.Length;

        var lines = new string[lineCount];
        Array.Copy(rawLines, lines, lineCount);

        var conflicts = new List<ParsedConflict>();
        int i = 0;
        while (i < lines.Length)
        {
            if (OursOpen.IsMatch(lines[i]))
            {
                var parsed = ParseConflictBlock(lines, i, out int consumed);
                conflicts.Add(parsed);
                i += consumed;
            }
            else
            {
                // Defensive: an orphaned base/separator/close marker is malformed input.
                if (BaseMiddle.IsMatch(lines[i]) || Separator.IsMatch(lines[i]) || TheirsClose.IsMatch(lines[i]))
                {
                    throw new MergeEngineException(
                        $"Malformed zdiff3 output: stray marker at line {i + 1}: {Truncate(lines[i])}");
                }
                i++;
            }
        }

        return new ParseResult(lines, hasTrailingNewline, conflicts);
    }

    private static ParsedConflict ParseConflictBlock(string[] lines, int startIdx, out int consumed)
    {
        // Starting at the opening '<<<<<<<' line. Find each transition.
        int oursStart = startIdx + 1;
        int baseMarker = -1;
        int separator = -1;
        int closeMarker = -1;

        for (int i = oursStart; i < lines.Length; i++)
        {
            var line = lines[i];

            // Nested open marker is always an error. Never try to "recover".
            if (OursOpen.IsMatch(line))
            {
                throw new MergeEngineException(
                    $"Malformed zdiff3 output: nested '<<<<<<<' at line {i + 1} inside block starting at line {startIdx + 1}.");
            }

            if (baseMarker < 0 && BaseMiddle.IsMatch(line))
            {
                baseMarker = i;
                continue;
            }

            if (baseMarker >= 0 && separator < 0 && Separator.IsMatch(line))
            {
                separator = i;
                continue;
            }

            if (separator >= 0 && TheirsClose.IsMatch(line))
            {
                closeMarker = i;
                break;
            }
        }

        if (baseMarker < 0 || separator < 0 || closeMarker < 0)
        {
            throw new MergeEngineException(
                $"Malformed zdiff3 output: block starting at line {startIdx + 1} is missing " +
                (baseMarker < 0 ? "'|||||||' base marker" :
                 separator < 0 ? "'=======' separator" : "'>>>>>>>' close marker"));
        }

        var oursLines = Slice(lines, oursStart, baseMarker);
        var baseLines = Slice(lines, baseMarker + 1, separator);
        var theirsLines = Slice(lines, separator + 1, closeMarker);

        var marked = new LineRange(startIdx + 1, closeMarker + 2); // 1-based, half-open
        consumed = closeMarker + 1 - startIdx;
        return new ParsedConflict(marked, oursLines, baseLines, theirsLines);
    }

    private static IReadOnlyList<string> Slice(string[] source, int startInclusive, int endExclusive)
    {
        if (endExclusive <= startInclusive) return Array.Empty<string>();
        var len = endExclusive - startInclusive;
        var result = new string[len];
        Array.Copy(source, startInclusive, result, 0, len);
        return result;
    }

    private static string Truncate(string s) => s.Length <= 80 ? s : s.Substring(0, 77) + "...";
}
