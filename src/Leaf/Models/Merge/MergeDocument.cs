using System.Text;

namespace Leaf.Models.Merge;

/// <summary>
/// Immutable snapshot of a three-way merge. Holds the original input texts, the
/// initial merged output from the engine (with zdiff3 conflict markers still present
/// for unresolved regions), and the structured list of <see cref="ModifiedBaseRange"/>
/// the UI resolves one by one.
/// </summary>
/// <remarks>
/// The merge engine is deterministic for a given (base, ours, theirs) tuple; two calls
/// with identical inputs produce equal documents. The <see cref="ComposeResolvedText"/>
/// method applies user choices without re-running the engine.
/// </remarks>
public sealed class MergeDocument
{
    public string FilePath { get; }
    public string BaseText { get; }
    public string OursText { get; }
    public string TheirsText { get; }

    /// <summary>
    /// The full merged output as produced by the engine. Auto-mergeable hunks are in
    /// final form; conflicting regions still carry zdiff3 conflict markers.
    /// </summary>
    public string InitialMergedText { get; }

    /// <summary>Lines of <see cref="BaseText"/> (no trailing newlines, no CR).</summary>
    public IReadOnlyList<string> BaseLines { get; }
    /// <summary>Lines of <see cref="OursText"/>.</summary>
    public IReadOnlyList<string> OursLines { get; }
    /// <summary>Lines of <see cref="TheirsText"/>.</summary>
    public IReadOnlyList<string> TheirsLines { get; }
    /// <summary>Lines of <see cref="InitialMergedText"/> (still contains conflict markers).</summary>
    public IReadOnlyList<string> InitialMergedLines { get; }

    public IReadOnlyList<ModifiedBaseRange> Ranges { get; }

    /// <summary>Line-ending style sniffed from the inputs (<c>"\r\n"</c> or <c>"\n"</c>).</summary>
    public string LineEnding { get; }

    /// <summary><c>true</c> iff <see cref="InitialMergedText"/> ends with a newline.</summary>
    public bool HasTrailingNewline { get; }

    public int ConflictCount => Ranges.Count(r => r.IsConflicting);

    public bool HasConflicts => ConflictCount > 0;

    /// <summary>
    /// Filtered view of <see cref="Ranges"/> containing only the conflicting
    /// ones — the regions with zdiff3 markers in <see cref="InitialMergedText"/>
    /// that the user needs to resolve. Auto-merged ranges (<c>IsConflicting == false</c>)
    /// are omitted. Centralised here so every navigation / resolution site
    /// agrees on the filter predicate; without this, drift between
    /// <c>Ranges.Where(r => r.IsConflicting)</c> inlined at each call site would
    /// eventually produce off-by-one navigation and inconsistent composed output.
    /// </summary>
    public IEnumerable<ModifiedBaseRange> ConflictingRanges => Ranges.Where(r => r.IsConflicting);

    public MergeDocument(
        string filePath,
        string baseText,
        string oursText,
        string theirsText,
        string initialMergedText,
        IReadOnlyList<string> baseLines,
        IReadOnlyList<string> oursLines,
        IReadOnlyList<string> theirsLines,
        IReadOnlyList<string> initialMergedLines,
        IReadOnlyList<ModifiedBaseRange> ranges,
        string lineEnding,
        bool hasTrailingNewline)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        BaseText = baseText ?? throw new ArgumentNullException(nameof(baseText));
        OursText = oursText ?? throw new ArgumentNullException(nameof(oursText));
        TheirsText = theirsText ?? throw new ArgumentNullException(nameof(theirsText));
        InitialMergedText = initialMergedText ?? throw new ArgumentNullException(nameof(initialMergedText));
        BaseLines = baseLines ?? throw new ArgumentNullException(nameof(baseLines));
        OursLines = oursLines ?? throw new ArgumentNullException(nameof(oursLines));
        TheirsLines = theirsLines ?? throw new ArgumentNullException(nameof(theirsLines));
        InitialMergedLines = initialMergedLines ?? throw new ArgumentNullException(nameof(initialMergedLines));
        Ranges = ranges ?? throw new ArgumentNullException(nameof(ranges));
        LineEnding = lineEnding ?? throw new ArgumentNullException(nameof(lineEnding));
        HasTrailingNewline = hasTrailingNewline;
    }

    /// <summary>
    /// Compose the final merged text by substituting each range's resolved content into
    /// the initial merged output. Ranges without a state in the dictionary, or with an
    /// <see cref="ResolutionState.Unresolved"/> state, keep their zdiff3 markers.
    /// Ranges that are not conflicting (auto-merged by Git) are preserved verbatim even
    /// when a state is provided — auto-merged regions have no markers to substitute.
    /// </summary>
    public string ComposeResolvedText(IReadOnlyDictionary<int, ResolutionState>? rangeStates)
    {
        if (rangeStates is null || rangeStates.Count == 0)
        {
            return InitialMergedText;
        }

        var sb = new StringBuilder(InitialMergedText.Length);
        var mergedLines = InitialMergedLines;
        int cursor = 0;

        // Operate only on conflicting ranges — they are the ones with markers in InitialMergedText.
        var conflictRanges = ConflictingRanges
                                   .OrderBy(r => r.ResultMarkedRange.StartLine)
                                   .ToList();

        foreach (var range in conflictRanges)
        {
            EmitLines(sb, mergedLines, cursor, range.ResultMarkedRange.StartLine - 1);

            var state = rangeStates.TryGetValue(range.Index, out var s) ? s : ResolutionState.Unresolved.Instance;
            AppendResolution(sb, range, state);

            cursor = range.ResultMarkedRange.EndLineExclusive - 1;
        }

        EmitLines(sb, mergedLines, cursor, mergedLines.Count);

        // Preserve the original trailing-newline convention. EmitLines unconditionally
        // appends '\n' after every emitted line, so we strip the final '\n' when the
        // original input had none — a POSIX convention that matters for tooling
        // (diff, wc -l, some compilers) and for byte-for-byte parity with `git merge`.
        if (!HasTrailingNewline && sb.Length > 0 && sb[sb.Length - 1] == '\n')
        {
            sb.Length--;
        }

        var composed = sb.ToString();

        if (LineEnding == "\r\n")
        {
            composed = composed.Replace("\n", "\r\n");
        }

        return composed;
    }

    private static void EmitLines(StringBuilder sb, IReadOnlyList<string> lines, int startIndex, int endIndexExclusive)
    {
        for (int i = startIndex; i < endIndexExclusive; i++)
        {
            sb.Append(lines[i]);
            sb.Append('\n');
        }
    }

    private static void AppendResolution(StringBuilder sb, ModifiedBaseRange range, ResolutionState state)
    {
        switch (state)
        {
            case ResolutionState.Unresolved:
                // Keep the conflict markers verbatim.
                EmitLines(sb, SliceMarkers(range), 0, MarkerLineCount(range));
                return;

            case ResolutionState.AcceptOurs:
                AppendLines(sb, range.OursLines);
                return;

            case ResolutionState.AcceptTheirs:
                AppendLines(sb, range.TheirsLines);
                return;

            case ResolutionState.AcceptBoth both:
                AppendCombined(sb, range, both);
                return;

            case ResolutionState.Manual manual:
                // Manual text is always emitted as LF-terminated. The CRLF restoration
                // pass at the end of ComposeResolvedText converts the whole buffer back
                // to \r\n when LineEnding == "\r\n"; if the user's Manual text already
                // contained "\r\n" (e.g. Windows clipboard paste), that global Replace
                // would produce "\r\r\n". Normalise here before appending.
                var normalisedManual = NormaliseToLf(manual.Text);
                sb.Append(normalisedManual);
                if (normalisedManual.Length > 0 && normalisedManual[normalisedManual.Length - 1] != '\n')
                {
                    sb.Append('\n');
                }
                return;

            default:
                throw new InvalidOperationException($"Unknown resolution state: {state.GetType().Name}");
        }
    }

    private static void AppendLines(StringBuilder sb, IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            sb.Append(line);
            sb.Append('\n');
        }
    }

    private static void AppendCombined(StringBuilder sb, ModifiedBaseRange range, ResolutionState.AcceptBoth both)
    {
        // "Smart combine" for Phase 1: if one side is empty, use the other; else concatenate
        // in the chosen order. True interleaving (diff-aware) is a Phase 3 upgrade —
        // Phase 1 ships a correct-but-simple variant so the state shape is real from day one.
        if (range.OursLines.Count == 0)
        {
            AppendLines(sb, range.TheirsLines);
            return;
        }
        if (range.TheirsLines.Count == 0)
        {
            AppendLines(sb, range.OursLines);
            return;
        }

        if (both.FirstOurs)
        {
            AppendLines(sb, range.OursLines);
            AppendLines(sb, range.TheirsLines);
        }
        else
        {
            AppendLines(sb, range.TheirsLines);
            AppendLines(sb, range.OursLines);
        }
    }

    /// <summary>
    /// The marker slice for an unresolved range, reconstructed from the range's own lines
    /// and labels so custom labels (branch names, commit SHAs) round-trip verbatim.
    /// </summary>
    private static IReadOnlyList<string> SliceMarkers(ModifiedBaseRange range)
    {
        var lines = new List<string>(range.OursLines.Count + range.BaseLines.Count + range.TheirsLines.Count + 4);
        lines.Add(FormatOpenMarker(range.OursLabel ?? "ours"));
        lines.AddRange(range.OursLines);
        lines.Add(FormatBaseMarker(range.BaseLabel ?? "base"));
        lines.AddRange(range.BaseLines);
        lines.Add("=======");
        lines.AddRange(range.TheirsLines);
        lines.Add(FormatCloseMarker(range.TheirsLabel ?? "theirs"));
        return lines;
    }

    private static string FormatOpenMarker(string label) => "<<<<<<< " + label;
    private static string FormatBaseMarker(string label) => "||||||| " + label;
    private static string FormatCloseMarker(string label) => ">>>>>>> " + label;

    private static int MarkerLineCount(ModifiedBaseRange range)
        => 4 + range.OursLines.Count + range.BaseLines.Count + range.TheirsLines.Count;

    private static string NormaliseToLf(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('\r') < 0) return text;
        var step1 = text.Replace("\r\n", "\n");
        return step1.IndexOf('\r') < 0 ? step1 : step1.Replace('\r', '\n');
    }
}
