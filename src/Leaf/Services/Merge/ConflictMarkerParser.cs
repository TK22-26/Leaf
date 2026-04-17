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
/// Disambiguation rules for <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c>-lookalikes:
/// </para>
/// <list type="bullet">
/// <item><description>
/// When the line immediately after the outer opener is also a
/// <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> line, the outer IS the real conflict opener —
/// ours content happens to start with a literal marker-lookalike. Git emits this
/// shape when the user's ours input begins with <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c>.
/// </description></item>
/// <item><description>
/// Otherwise, a nested <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> seen BEFORE the block's
/// <c>|||||||</c> base marker means the outer opener was content (probably a doc
/// line); the outer is treated as content and the inner is retried as the real opener.
/// </description></item>
/// <item><description>
/// After <c>|||||||</c> is found we are inside base/theirs content and all further
/// marker-like lines (including inner <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c>) are treated
/// as content.
/// </description></item>
/// </list>
/// <para>
/// Known limitation: when user content legitimately contains a literal
/// <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c> line inside theirs content, the parser cannot
/// distinguish it from the close marker and will truncate the conflict. This
/// unresolved ambiguity of the zdiff3 text format is detectable by the downstream
/// commit gate (any remaining marker in the resolved text blocks commit).
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
    /// Parse a zdiff3-formatted merged output. Never throws on pathological input —
    /// unparseable marker lookalikes are treated as content.
    /// </summary>
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
        var lines = LineSplitter.Split(normalized, out var hasTrailingNewline);

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
    /// <para>Scan states:</para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Before baseMarker:</b> we are tentatively inside ours content. A second
    /// <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> here means the outer line is almost certainly
    /// content (git's merge-file never emits nested openers); the caller retries at
    /// the inner opener.
    /// </description></item>
    /// <item><description>
    /// <b>Between baseMarker and separator:</b> we are inside base content. Marker-like
    /// lines other than the first <c>=======</c> are treated as content.
    /// </description></item>
    /// <item><description>
    /// <b>Between separator and close:</b> we are inside theirs content. Marker-like
    /// lines other than the first <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c> are treated as content.
    /// This lets git's legitimate output containing literal <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c>
    /// lines inside theirs content parse correctly.
    /// </description></item>
    /// </list>
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

        // Disambiguation: if the VERY NEXT line after the outer opener is also
        // `<<<<<<<`, git is emitting a legitimate conflict whose ours content starts
        // with a marker-lookalike line. In that case, the outer IS the real opener
        // and all subsequent marker-lookalikes inside the block are content —
        // including the first one (the one that fooled us into returning false before).
        bool oursStartsWithLookalike = oursStart < lines.Length && OursOpen.IsMatch(lines[oursStart]);

        for (int i = oursStart; i < lines.Length; i++)
        {
            var line = lines[i];

            if (baseMarker < 0)
            {
                if (OursOpen.IsMatch(line))
                {
                    if (oursStartsWithLookalike)
                    {
                        // First line of ours is a marker-lookalike — keep outer as the
                        // real opener, treat this (and any further <<<<<<<) as content.
                        continue;
                    }
                    // Outer opener followed by non-marker content then a nested <<<<<<<
                    // before baseMarker → outer is very likely a documentation line.
                    return false;
                }
                var m = BaseMiddle.Match(line);
                if (m.Success)
                {
                    baseMarker = i;
                    baseMatch = m;
                }
                continue;
            }

            if (separator < 0)
            {
                if (Separator.IsMatch(line))
                {
                    separator = i;
                }
                // All other marker-lookalikes (including <<<<<<< or |||||||) are content here.
                continue;
            }

            var cm = TheirsClose.Match(line);
            if (cm.Success)
            {
                closeMarker = i;
                closeMatch = cm;
                break;
            }
            // Other marker-lookalikes inside theirs content are treated as content.
        }

        if (baseMarker < 0 || separator < 0 || closeMarker < 0)
        {
            return false;
        }

        var oursLines = Slice(lines, oursStart, baseMarker);
        var baseLines = Slice(lines, baseMarker + 1, separator);
        var theirsLines = Slice(lines, separator + 1, closeMarker);

        // Git never emits a fully-empty conflict block — identical or both-deleted
        // content auto-merges. A parsed block with zero content on all three sides is
        // always user documentation that happens to contain the full zdiff3 triad
        // (e.g. markdown describing conflict markers). Treat as content.
        if (oursLines.Count == 0 && baseLines.Count == 0 && theirsLines.Count == 0)
        {
            return false;
        }

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
