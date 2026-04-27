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
    private readonly Brush _markerStripBg;
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
        _markerStripBg = MergePaletteResources.ResolveFrozenBrush("Merge.Surface.3.Color");
        _markerBorder = MergePaletteResources.ResolveFrozenBrush("Merge.Border.Subtle.Color");
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var doc = _getDocument();
        if (doc is null) return;
        textView.EnsureVisualLines();
        if (!textView.VisualLinesValid) return;

        var states = _getRangeStates();
        var width = textView.ActualWidth;
        // Paint past the visible viewport edges so a partially-scrolled
        // strip doesn't show a visible seam at the right margin.
        var paintWidth = Math.Max(width, textView.RenderSize.Width);

        foreach (var visualLine in textView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            var y = visualLine.VisualTop - textView.VerticalOffset;
            var rect = new Rect(0, y, paintWidth, visualLine.Height);

            if (IsMarkerLine(doc, lineNumber))
            {
                // Marker chrome: full-width Surface-3 strip + 1 px hairline
                // top + bottom borders. The InlineObjectElement (the toolbar
                // / caption) renders its content over this surface, so the
                // strip extends beyond the natural width of the buttons.
                drawingContext.DrawRectangle(_markerStripBg, pen: null, rect);
                drawingContext.DrawRectangle(_markerBorder, pen: null,
                    new Rect(0, y, paintWidth, 1));
                drawingContext.DrawRectangle(_markerBorder, pen: null,
                    new Rect(0, y + visualLine.Height - 1, paintWidth, 1));
                continue;
            }

            var brush = ClassifyLine(doc, states, lineNumber);
            if (brush is null) continue;
            drawingContext.DrawRectangle(brush, pen: null, rect);
        }
    }

    /// <summary>
    /// True when <paramref name="lineNumber"/> is one of the four zdiff3
    /// marker lines inside any conflict's marked range. Read directly from
    /// the document text rather than the parser output so this stays in
    /// sync with the result-pane's actual rendered text.
    /// </summary>
    private static bool IsMarkerLine(MergeDocument doc, int lineNumber)
    {
        if (lineNumber < 1 || lineNumber > doc.InitialMergedLines.Count) return false;
        var text = doc.InitialMergedLines[lineNumber - 1];
        if (string.IsNullOrEmpty(text)) return false;
        return text.StartsWith("<<<<<<<", StringComparison.Ordinal)
            || text.StartsWith(">>>>>>>", StringComparison.Ordinal)
            || text.StartsWith("|||||||", StringComparison.Ordinal)
            || text == "=======";
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
