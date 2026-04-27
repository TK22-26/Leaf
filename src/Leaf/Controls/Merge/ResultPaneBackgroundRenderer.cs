#nullable enable
using System.Windows;
using System.Windows.Media;
using Leaf.Models.Merge;
using Leaf.TextEdit.Rendering;

namespace Leaf.Controls.Merge;

/// <summary>
/// AvalonEdit background renderer for the merge editor's Result pane.
/// Paints each conflict region's content lines with a side-tinted
/// background so the user can tell at a glance which lines came from
/// ours, theirs, or base — matching Beyond Compare's color-coded panes
/// and VS Code's merge-editor side tinting.
/// </summary>
/// <remarks>
/// <para>
/// Tinting strategy per conflict region:
/// <list type="bullet">
/// <item><b>Unresolved:</b> ours content gets <c>Merge.Ours.BgSubtle</c>,
/// base content (zdiff3) gets <c>Merge.Base.BgSubtle</c>, theirs content
/// gets <c>Merge.Theirs.BgSubtle</c>. The content boundaries are inferred
/// from marker line positions (<c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> /
/// <c>|||||||</c> / <c>=======</c> / <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c>),
/// not from <c>ModifiedBaseRange</c> directly, because the result-pane
/// document carries inline marker lines that occupy real line numbers.</item>
/// <item><b>Resolved:</b> the chosen side's content (which has REPLACED
/// the markers in <c>ComposeResolvedText</c>'s output) is painted with
/// <c>Merge.State.Resolved.Overlay</c> as a "this conflict is settled"
/// indicator. Locating those lines requires walking the result-pane's
/// document line-by-line because resolved ranges no longer have markers.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class ResultPaneBackgroundRenderer : IBackgroundRenderer
{
    private readonly Func<MergeDocument?> _getDocument;
    private readonly Func<IReadOnlyDictionary<int, ResolutionState>?> _getRangeStates;

    private readonly Brush _oursBg;
    private readonly Brush _theirsBg;
    private readonly Brush _baseBg;
    private readonly Brush _resolvedBg;
    // Marker-row chrome. The Open ("toolbar") and Close ("END") rows paint
    // NEUTRAL (Surface-3) — the toolbar is a command surface, not part of
    // any side, and the END row simply closes the conflict. Only the BASE
    // and EQUALS markers carry their section's tint, since each one
    // introduces a content section the user is about to read.
    private readonly Brush _neutralMarkerBg;
    private readonly Brush _theirsMarkerBg;
    private readonly Brush _baseMarkerBg;
    private readonly Brush _oursStrongBg;
    private readonly Brush _markerBorder;

    public ResultPaneBackgroundRenderer(
        Func<MergeDocument?> getDocument,
        Func<IReadOnlyDictionary<int, ResolutionState>?> getRangeStates)
    {
        _getDocument = getDocument;
        _getRangeStates = getRangeStates;
        _oursBg = MergePaletteResources.ResolveFrozenBrush("Merge.Ours.BgSubtle.Color");
        _theirsBg = MergePaletteResources.ResolveFrozenBrush("Merge.Theirs.BgSubtle.Color");
        _baseBg = MergePaletteResources.ResolveFrozenBrush("Merge.Base.BgSubtle.Color");
        _resolvedBg = MergePaletteResources.ResolveFrozenBrush("Merge.State.Resolved.Overlay.Color");
        _neutralMarkerBg = MergePaletteResources.ResolveFrozenBrush("Merge.Surface.3.Color");
        _theirsMarkerBg = MergePaletteResources.ResolveFrozenBrush("Merge.Theirs.BgStrong.Color");
        _baseMarkerBg = MergePaletteResources.ResolveFrozenBrush("Merge.Base.BgStrong.Color");
        _oursStrongBg = MergePaletteResources.ResolveFrozenBrush("Merge.Ours.BgStrong.Color");
        _markerBorder = MergePaletteResources.ResolveFrozenBrush("Merge.Border.Subtle.Color");
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var mergeDoc = _getDocument();
        if (mergeDoc is null) return;
        var docModel = textView.Document;
        if (docModel is null) return;
        textView.EnsureVisualLines();
        if (!textView.VisualLinesValid) return;

        // Helper: classify a marker line by reading the LIVE displayed
        // document text rather than mergeDoc.InitialMergedLines. After the
        // user accepts a side, the displayed text shrinks but
        // InitialMergedLines still points at the pre-acceptance layout —
        // using that for marker detection paints marker chrome (toolbar /
        // base / theirs / END strip backgrounds) at the OLD line positions
        // in the displayed text, leaving stale colored bands across what
        // are now plain content lines.
        MarkerKind ClassifyDisplayed(int lineNumber)
        {
            if (lineNumber < 1 || lineNumber > docModel.LineCount) return MarkerKind.None;
            var ln = docModel.GetLineByNumber(lineNumber);
            var text = docModel.GetText(ln.Offset, ln.Length);
            if (string.IsNullOrEmpty(text)) return MarkerKind.None;
            if (text.StartsWith("<<<<<<<", StringComparison.Ordinal)) return MarkerKind.Open;
            if (text.StartsWith(">>>>>>>", StringComparison.Ordinal)) return MarkerKind.Close;
            if (text.StartsWith("|||||||", StringComparison.Ordinal)) return MarkerKind.Base;
            if (text == "=======") return MarkerKind.Equals;
            return MarkerKind.None;
        }

        bool IsBaseEmptyDisplayed(int lineNumber)
        {
            // Walk doc text to find the conflict containing this line, count
            // its base content lines (between `|||||||` and `=======`).
            // Returning false on any irregularity is safe — we just keep the
            // base caption visible.
            if (ClassifyDisplayed(lineNumber) != MarkerKind.Base) return false;
            int next = lineNumber + 1;
            if (next > docModel.LineCount) return false;
            return ClassifyDisplayed(next) == MarkerKind.Equals;
        }

        var states = _getRangeStates();
        var width = textView.ActualWidth;
        // Paint past the visible viewport edges so a partially-scrolled
        // strip doesn't show a visible seam at the right margin.
        var paintWidth = Math.Max(width, textView.RenderSize.Width);

        // Per-displayed-line tint map. Built fresh per Draw because both
        // RangeStates and Document.Text can change between renders without
        // notifying the renderer; rebuilding is O(N+M) where N is doc lines
        // and M is conflict count, which is fast enough for typical files.
        // The map encodes resolution-aware positions: a resolved AcceptOurs
        // body shows ours.Length displayed lines tinted resolved-overlay,
        // not ResultMarkedRange.Length lines (which would over-paint
        // post-conflict context that has scrolled up to fill the gap).
        var tintMap = BuildTintMap(docModel.LineCount, mergeDoc, states);

        foreach (var visualLine in textView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            var y = visualLine.VisualTop - textView.VerticalOffset;
            var rect = new Rect(0, y, paintWidth, visualLine.Height);

            var markerKind = ClassifyDisplayed(lineNumber);
            if (markerKind == MarkerKind.Base && IsBaseEmptyDisplayed(lineNumber))
            {
                continue;
            }
            if (markerKind != MarkerKind.None)
            {
                if (markerKind == MarkerKind.Open)
                {
                    double oursStripHeight = Math.Min(ConflictMarkerInlineGenerator.OursRowHeight, visualLine.Height);
                    double topPortion = visualLine.Height - oursStripHeight;
                    if (topPortion > 0)
                    {
                        drawingContext.DrawRectangle(_neutralMarkerBg, pen: null,
                            new Rect(0, y, paintWidth, topPortion));
                    }
                    drawingContext.DrawRectangle(_oursStrongBg, pen: null,
                        new Rect(0, y + topPortion, paintWidth, oursStripHeight));
                }
                else
                {
                    var markerBg = markerKind switch
                    {
                        MarkerKind.Base => _baseMarkerBg,
                        MarkerKind.Equals => _theirsMarkerBg,
                        MarkerKind.Close => _neutralMarkerBg,
                        _ => _neutralMarkerBg,
                    };
                    drawingContext.DrawRectangle(markerBg, pen: null, rect);
                }
                drawingContext.DrawRectangle(_markerBorder, pen: null,
                    new Rect(0, y, paintWidth, 1));
                drawingContext.DrawRectangle(_markerBorder, pen: null,
                    new Rect(0, y + visualLine.Height - 1, paintWidth, 1));
                continue;
            }

            if (lineNumber < 1 || lineNumber >= tintMap.Length) continue;
            var brush = tintMap[lineNumber];
            if (brush is null) continue;
            drawingContext.DrawRectangle(brush, pen: null, rect);
        }
    }

    /// <summary>
    /// Per-displayed-line tint map. Walks the conflicting ranges in
    /// <see cref="ModifiedBaseRange.ResultMarkedRange"/> order and assigns
    /// a brush to every line inside a conflict body, using resolution state
    /// to determine each body's actual displayed line count.
    /// </summary>
    /// <remarks>
    /// Earlier <c>ClassifyLine</c> looked up
    /// <see cref="ModifiedBaseRange.ResultMarkedRange"/> positions directly
    /// — but those are <em>InitialMergedText</em> coordinates, not displayed
    /// coordinates. After the user accepts a side, the displayed body shrinks
    /// (e.g. AcceptOurs replaces the whole marker block with just ours
    /// content) and every subsequent context line shifts up. The classifier
    /// then painted the resolved-overlay tint over those shifted-up context
    /// lines, making swaths of file context look like resolved-conflict
    /// content. This walker tracks the displayed cursor explicitly and
    /// only tints the actual body extent.
    /// </remarks>
    internal Brush?[] BuildTintMap(
        int docLineCount,
        MergeDocument mergeDoc,
        IReadOnlyDictionary<int, ResolutionState>? states)
    {
        var map = new Brush?[docLineCount + 1];
        if (docLineCount <= 0) return map;

        var conflicts = mergeDoc.Ranges
            .Where(r => r.IsConflicting)
            .OrderBy(r => r.ResultMarkedRange.StartLine)
            .ToList();
        if (conflicts.Count == 0) return map;

        int docLine = 1;
        int prevDisplayedEndExclusive = 1;

        foreach (var r in conflicts)
        {
            int contextLines = Math.Max(0, r.ResultMarkedRange.StartLine - prevDisplayedEndExclusive);
            docLine += contextLines;
            if (docLine > docLineCount) break;

            var state = states is not null && states.TryGetValue(r.Index, out var s)
                ? s
                : ResolutionState.Unresolved.Instance;

            if (state is ResolutionState.Unresolved)
            {
                // Marker block: <<<<<<<, ours, |||||||, base, =======, theirs, >>>>>>>.
                // Markers themselves don't get a content tint — Draw paints
                // them via the marker-chrome path. Skip with docLine++.
                if (docLine <= docLineCount) docLine++;                                  // <<<<<<<
                for (int j = 0; j < r.Ours.Length && docLine <= docLineCount; j++)
                    map[docLine++] = _oursBg;
                if (docLine <= docLineCount) docLine++;                                  // |||||||
                for (int j = 0; j < r.Base.Length && docLine <= docLineCount; j++)
                    map[docLine++] = _baseBg;
                if (docLine <= docLineCount) docLine++;                                  // =======
                for (int j = 0; j < r.Theirs.Length && docLine <= docLineCount; j++)
                    map[docLine++] = _theirsBg;
                if (docLine <= docLineCount) docLine++;                                  // >>>>>>>
            }
            else
            {
                // Resolved: tint each emitted line with the SIDE's colour
                // (blue for ours, green for theirs) instead of the generic
                // resolved-overlay green. Earlier every accept tinted green
                // — making AcceptOurs visually indistinguishable from
                // AcceptTheirs, since both ended up green.
                //   AcceptOurs   → all body lines ours-tinted (blue)
                //   AcceptTheirs → all body lines theirs-tinted (green)
                //   AcceptBoth   → ours-lines blue, theirs-lines green
                //                  (in chosen order so the gutter reads as
                //                  "this came from ours, this came from theirs")
                //   Manual       → resolved-overlay (no canonical side)
                switch (state)
                {
                    case ResolutionState.AcceptOurs:
                        for (int j = 0; j < r.Ours.Length && docLine <= docLineCount; j++)
                            map[docLine++] = _oursBg;
                        break;
                    case ResolutionState.AcceptTheirs:
                        for (int j = 0; j < r.Theirs.Length && docLine <= docLineCount; j++)
                            map[docLine++] = _theirsBg;
                        break;
                    case ResolutionState.AcceptBoth ab:
                        if (r.OursLines.Count == 0)
                        {
                            for (int j = 0; j < r.Theirs.Length && docLine <= docLineCount; j++)
                                map[docLine++] = _theirsBg;
                        }
                        else if (r.TheirsLines.Count == 0)
                        {
                            for (int j = 0; j < r.Ours.Length && docLine <= docLineCount; j++)
                                map[docLine++] = _oursBg;
                        }
                        else if (ab.FirstOurs)
                        {
                            for (int j = 0; j < r.Ours.Length && docLine <= docLineCount; j++)
                                map[docLine++] = _oursBg;
                            for (int j = 0; j < r.Theirs.Length && docLine <= docLineCount; j++)
                                map[docLine++] = _theirsBg;
                        }
                        else
                        {
                            for (int j = 0; j < r.Theirs.Length && docLine <= docLineCount; j++)
                                map[docLine++] = _theirsBg;
                            for (int j = 0; j < r.Ours.Length && docLine <= docLineCount; j++)
                                map[docLine++] = _oursBg;
                        }
                        break;
                    case ResolutionState.Manual m:
                        int manualLines = CountManualLines(m.Text);
                        for (int j = 0; j < manualLines && docLine <= docLineCount; j++)
                            map[docLine++] = _resolvedBg;
                        break;
                }
            }

            prevDisplayedEndExclusive = r.ResultMarkedRange.EndLineExclusive;
        }

        return map;
    }

    private static int CountManualLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int count = 1;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') count++;
        if (text[text.Length - 1] == '\n') count--;
        return count == 0 ? 1 : count;
    }

    /// <summary>
    /// Marker categories used to pick the side-specific background tint.
    /// Internal so tests can pin the classification without standing up a
    /// visual tree.
    /// </summary>
    internal enum MarkerKind { None, Open, Base, Equals, Close }

    /// <summary>
    /// Classify <paramref name="lineNumber"/> by its zdiff3 marker prefix.
    /// Reads directly from the merged-text source so this stays in sync
    /// with the result-pane's actual rendered text rather than depending
    /// on a separately-cached parser output.
    /// </summary>
    /// <summary>
    /// True when <paramref name="lineNumber"/> is a <c>|||||||</c> base
    /// marker for a conflicting range whose <see cref="ModifiedBaseRange.BaseLines"/>
    /// is empty. Used to suppress the marker chrome on otherwise-noise
    /// rows that zdiff3 emits even with no base content.
    /// </summary>
    private static bool IsBaseEmptyForLine(MergeDocument doc, int lineNumber)
    {
        foreach (var range in doc.Ranges)
        {
            if (!range.IsConflicting) continue;
            if (lineNumber < range.ResultMarkedRange.StartLine) break;
            if (lineNumber >= range.ResultMarkedRange.EndLineExclusive) continue;
            return range.BaseLines.Count == 0;
        }
        return false;
    }

    internal static MarkerKind ClassifyMarkerLine(MergeDocument doc, int lineNumber)
    {
        if (lineNumber < 1 || lineNumber > doc.InitialMergedLines.Count) return MarkerKind.None;
        var text = doc.InitialMergedLines[lineNumber - 1];
        if (string.IsNullOrEmpty(text)) return MarkerKind.None;
        if (text.StartsWith("<<<<<<<", StringComparison.Ordinal)) return MarkerKind.Open;
        if (text.StartsWith(">>>>>>>", StringComparison.Ordinal)) return MarkerKind.Close;
        if (text.StartsWith("|||||||", StringComparison.Ordinal)) return MarkerKind.Base;
        if (text == "=======") return MarkerKind.Equals;
        return MarkerKind.None;
    }

    /// <summary>
    /// Decide which side-tint brush (if any) applies to <paramref name="lineNumber"/>.
    /// Walks the document's conflict ranges and tests:
    /// <list type="number">
    /// <item>Is the line inside a resolved range? → resolved tint.</item>
    /// <item>Is the line inside an unresolved conflict's <c>ResultMarkedRange</c>?
    /// Sub-classify by marker boundaries (ours / base / theirs).</item>
    /// <item>Otherwise → no tint (transparent).</item>
    /// </list>
    /// Exposed as <c>internal</c> so tests can drive the classification with
    /// synthetic inputs without standing up a real visual tree.
    /// </summary>
    internal Brush? ClassifyLine(
        MergeDocument doc,
        IReadOnlyDictionary<int, ResolutionState>? states,
        int lineNumber)
    {
        // The result-pane text comes from MergeDocument.ComposeResolvedText —
        // which keeps markers verbatim for unresolved ranges and replaces
        // them with the chosen side's text for resolved ones. Walking the
        // document text line-by-line is the only way to know exactly where
        // each conflict's content lives in the COMPOSED output, because
        // resolved ranges shift subsequent line numbers down.
        var allLines = doc.InitialMergedLines;  // backing for unresolved tinting
        if (lineNumber < 1) return null;

        // For UNRESOLVED tinting we use InitialMergedLines (the merged text
        // before any range-state substitution) since that's what the result
        // pane currently renders for unresolved ranges. Resolved ranges that
        // changed the line count would shift things — handled in step 2 below.
        // Fast path: walk conflicts in order and find the one whose
        // ResultMarkedRange covers this line.
        foreach (var range in doc.Ranges)
        {
            if (!range.IsConflicting) continue;
            if (lineNumber < range.ResultMarkedRange.StartLine) break;
            if (lineNumber >= range.ResultMarkedRange.EndLineExclusive) continue;

            // Inside this conflict's marked range. Check resolution.
            if (states is not null && states.TryGetValue(range.Index, out var state)
                && state is not ResolutionState.Unresolved)
            {
                // Resolved range: the user has already chosen. Tint the
                // marker block with the resolved overlay so the user can
                // see at a glance which conflicts are settled.
                return _resolvedBg;
            }

            // Unresolved: classify by which marker section we're in.
            return ClassifyUnresolvedLine(allLines, range, lineNumber);
        }
        return null;
    }

    private Brush? ClassifyUnresolvedLine(
        IReadOnlyList<string> mergedLines,
        ModifiedBaseRange range,
        int lineNumber)
    {
        int oursStart = range.ResultMarkedRange.StartLine;        // line of `<<<<<<<`
        int closeMarker = range.ResultMarkedRange.EndLineExclusive - 1;  // line of `>>>>>>>`

        // Locate optional `|||||||` (zdiff3 base separator) and `=======` between
        // ours and theirs. Exactly seven `=` (no trailing content) is the
        // separator; the base separator starts with seven `|`.
        int baseSeparator = -1;
        int equalsSeparator = -1;
        for (int line = oursStart + 1; line < closeMarker; line++)
        {
            // mergedLines is 0-based.
            if (line < 1 || line > mergedLines.Count) continue;
            var text = mergedLines[line - 1];
            if (string.IsNullOrEmpty(text)) continue;
            if (baseSeparator < 0 && text.StartsWith("|||||||", StringComparison.Ordinal))
            {
                baseSeparator = line;
            }
            else if (equalsSeparator < 0 && text == "=======")
            {
                equalsSeparator = line;
            }
        }

        // The marker lines themselves carry no tint — the inline element
        // generator paints its own toolbar / separator chrome over them.
        if (lineNumber == oursStart) return null;
        if (lineNumber == closeMarker) return null;
        if (lineNumber == baseSeparator) return null;
        if (lineNumber == equalsSeparator) return null;

        // Section boundaries:
        //   ours:    oursStart+1 .. (baseSeparator>0 ? baseSeparator-1 : equalsSeparator-1)
        //   base:    baseSeparator+1 .. equalsSeparator-1     (zdiff3 only)
        //   theirs:  equalsSeparator+1 .. closeMarker-1
        if (lineNumber > oursStart
            && lineNumber < (baseSeparator > 0 ? baseSeparator : equalsSeparator))
            return _oursBg;
        if (baseSeparator > 0 && lineNumber > baseSeparator && lineNumber < equalsSeparator)
            return _baseBg;
        if (equalsSeparator > 0 && lineNumber > equalsSeparator && lineNumber < closeMarker)
            return _theirsBg;
        return null;
    }
}
