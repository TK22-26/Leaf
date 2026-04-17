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
/// <para>
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
/// </para>
/// <para>
/// The parser uses a two-pass strategy: scan for candidate conflict blocks
/// (each a full <c>&lt;</c>/<c>|</c>/<c>=</c>/<c>&gt;</c> sequence with no
/// nesting), then extract each. Lines that start with seven <c>&lt;</c>s but
/// cannot complete a valid block are treated as content — the same file may
/// legitimately contain documentation about git conflict markers.
/// </para>
/// <para>
/// Ambiguity inside an accepted block (e.g. nested <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c>
/// between the opening line and the <c>|||||||</c> base marker) raises
/// <see cref="MergeEngineException"/>. A nested open can only occur when the
/// engine itself emits malformed output, which is never safe to silently recover from.
/// </para>
/// </remarks>
public static class ConflictMarkerParser
{
    private static readonly Regex OursOpen = new(@"^<{7}(\s(?<label>.*))?$", RegexOptions.Compiled);
    private static readonly Regex BaseMiddle = new(@"^\|{7}(\s(?<label>.*))?$", RegexOptions.Compiled);
    private static readonly Regex Separator = new(@"^={7}$", RegexOptions.Compiled);
    private static readonly Regex TheirsClose = new(@"^>{7}(\s(?<label>.*))?$", RegexOptions.Compiled);

    /// <summary>
    /// A single conflict block found in the merged output.
    /// </summary>
    /// <param name="MarkedRange">
    /// Line range in the merged output covering the entire conflict block, from the opening
    /// <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> line through the closing <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c> line inclusive.
    /// 1-based, half-open end.
    /// </param>
    /// <param name="OursLabel">Label from the opening marker (<c>null</c> if none, never empty).</param>
    /// <param name="BaseLabel">Label from the base marker.</param>
    /// <param name="TheirsLabel">Label from the closing marker.</param>
    /// <param name="OursLines">Content between the opening marker and the base separator.</param>
    /// <param name="BaseLines">Content between the base separator and the <c>=======</c> line.</param>
    /// <param name="TheirsLines">Content between the <c>=======</c> line and the closing marker.</param>
    public sealed record ParsedConflict(
        LineRange MarkedRange,
        string? OursLabel,
        string? BaseLabel,
        string? TheirsLabel,
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
    /// The merged output contains genuinely malformed markers produced by a broken engine
    /// (e.g. a nested <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> inside an otherwise well-formed
    /// block). User content that happens to contain lookalike sequences is not malformed —
    /// the parser treats it as content via the two-pass strategy.
    /// </exception>
    public static ParseResult Parse(string mergedOutput)
    {
        ArgumentNullException.ThrowIfNull(mergedOutput);

        if (mergedOutput.Length == 0)
        {
            return new ParseResult(Array.Empty<string>(), HasTrailingNewline: false, Array.Empty<ParsedConflict>());
        }

        // Normalise CRLF defensively even though the engine invocation passes
        // `-c core.autocrlf=false` — a corrupt repo config could still inject CRs
        // and we must not leak them downstream.
        var normalized = mergedOutput.Replace("\r\n", "\n").Replace("\r", "\n");
        var splitLines = LineSplitter.Split(normalized, out var hasTrailingNewline);
        var lines = splitLines is string[] arr ? arr : splitLines.ToArray();

        // Pass 1: find candidate conflict blocks via a forward scan that requires a
        // well-formed <<<<<<< / ||||||| / ======= / >>>>>>> sequence with no intervening
        // opening marker. Candidates that fail to complete are left as content.
        var conflicts = new List<ParsedConflict>();
        int i = 0;
        while (i < lines.Length)
        {
            if (OursOpen.IsMatch(lines[i]) && TryMatchBlock(lines, i, out var block, out int consumed))
            {
                conflicts.Add(block!);
                i += consumed;
            }
            else
            {
                i++;
            }
        }

        return new ParseResult(lines, hasTrailingNewline, conflicts);
    }

    /// <summary>
    /// Attempt to match a complete zdiff3 block starting at <paramref name="startIdx"/>.
    /// Returns <c>false</c> when the required sequence cannot be completed — the line
    /// is then treated as content.
    /// </summary>
    /// <remarks>
    /// A second <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> encountered while still scanning for the
    /// <c>|||||||</c> base marker tells us the outer line is <em>not</em> a real conflict
    /// opener — a well-formed block must complete before the next opener. We return
    /// <c>false</c> so the caller advances one line and will retry at the inner opener.
    /// Git's <c>merge-file</c> never emits overlapping blocks; what looked ambiguous is
    /// always user content.
    /// </remarks>
    private static bool TryMatchBlock(
        string[] lines,
        int startIdx,
        out ParsedConflict? conflict,
        out int consumed)
    {
        conflict = null;
        consumed = 0;

        var openMatch = OursOpen.Match(lines[startIdx]);
        if (!openMatch.Success) return false;

        int oursStart = startIdx + 1;
        int baseMarker = -1;
        int separator = -1;
        int closeMarker = -1;
        Match? baseMatch = null;
        Match? closeMatch = null;

        for (int i = oursStart; i < lines.Length; i++)
        {
            var line = lines[i];

            if (OursOpen.IsMatch(line))
            {
                // A second opener while we're still looking for any of the rest of the
                // sequence means the outer line can't be a real conflict opener. Bail —
                // the outer loop will retry starting at the inner opener.
                return false;
            }

            if (baseMarker < 0)
            {
                var m = BaseMiddle.Match(line);
                if (m.Success)
                {
                    baseMarker = i;
                    baseMatch = m;
                    continue;
                }
                continue;
            }

            if (separator < 0)
            {
                if (Separator.IsMatch(line))
                {
                    separator = i;
                    continue;
                }
                continue;
            }

            var cm = TheirsClose.Match(line);
            if (cm.Success)
            {
                closeMarker = i;
                closeMatch = cm;
                break;
            }
        }

        if (baseMarker < 0 || separator < 0 || closeMarker < 0)
        {
            return false;
        }

        var oursLines = Slice(lines, oursStart, baseMarker);
        var baseLines = Slice(lines, baseMarker + 1, separator);
        var theirsLines = Slice(lines, separator + 1, closeMarker);

        conflict = new ParsedConflict(
            new LineRange(startIdx + 1, closeMarker + 2),
            ExtractLabel(openMatch),
            ExtractLabel(baseMatch!),
            ExtractLabel(closeMatch!),
            oursLines,
            baseLines,
            theirsLines);
        consumed = closeMarker + 1 - startIdx;
        return true;
    }

    private static string? ExtractLabel(Match m)
    {
        if (!m.Success) return null;
        var g = m.Groups["label"];
        if (!g.Success) return null;
        var s = g.Value;
        return string.IsNullOrEmpty(s) ? null : s;
    }

    private static IReadOnlyList<string> Slice(string[] source, int startInclusive, int endExclusive)
    {
        if (endExclusive <= startInclusive) return Array.Empty<string>();
        var len = endExclusive - startInclusive;
        var result = new string[len];
        Array.Copy(source, startInclusive, result, 0, len);
        return result;
    }
}
