#nullable enable
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Leaf.Models.Merge;
using Leaf.TextEdit.Document;
using Leaf.TextEdit.Editing;
using Leaf.TextEdit.Rendering;

namespace Leaf.Controls.Merge;

/// <summary>
/// Result-pane line-number gutter that mirrors VS Code's merge editor: marker
/// lines (<c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c>, <c>|||||||</c>, <c>=======</c>,
/// <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c>) get NO number, and content lines inside
/// a conflict show the line number from the side they belong to — Ours / Base /
/// Theirs — rather than a sequential count of the displayed buffer. Outside any
/// conflict the gutter shows the file's natural ours-side line number.
/// </summary>
/// <remarks>
/// <para>
/// Why a custom margin: AvalonEdit's stock <see cref="LineNumberMargin"/>
/// renders the document line number 1:1, which would label every marker line
/// (visible-as-toolbar via <see cref="ConflictMarkerInlineGenerator"/>) and
/// give the false impression that the in-conflict content runs sequentially
/// past the surrounding code. The mismatch read as a bug — users expect the
/// numbers to indicate "which line in MY file is this" so divergence is
/// obvious at a glance.
/// </para>
/// <para>
/// The mapping is computed once per document/state change as a flat
/// <c>int?[]</c> indexed by 1-based document line number. <c>null</c> entries
/// mean "draw nothing" (marker lines and Manual-resolution lines, where no
/// canonical file-side number exists).
/// </para>
/// </remarks>
public sealed class MergeResultLineNumberMargin : LineNumberMargin
{
    private readonly Func<MergeDocument?> _getDocument;
    private readonly Func<IReadOnlyDictionary<int, ResolutionState>?> _getStates;

    private int?[] _displayMap = Array.Empty<int?>();
    private int _maxDisplayedDigits = 2;
    private TextDocument? _subscribedDocument;

    /// <summary>
    /// Pixels of breathing room between the rightmost digit and the editor
    /// text. The stock <see cref="LineNumberMargin"/> renders flush against
    /// the gutter edge, which on this app's dark theme reads as visually
    /// crowded — numbers and the first character of code text touch.
    /// </summary>
    private const double RightPadding = 8.0;

    public MergeResultLineNumberMargin(
        Func<MergeDocument?> getDocument,
        Func<IReadOnlyDictionary<int, ResolutionState>?> getStates)
    {
        _getDocument = getDocument;
        _getStates = getStates;
    }

    /// <summary>
    /// Recompute the display-map and trigger a redraw. Called by the host
    /// after a property change that could invalidate the mapping —
    /// <see cref="ResultPane.MergeDocument"/> / <see cref="ResultPane.RangeStates"/>
    /// assignments, or an in-place mutation of the RangeStates dictionary.
    /// </summary>
    public void Refresh()
    {
        RebuildDisplayMap();
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnDocumentChanged(TextDocument oldDocument, TextDocument newDocument)
    {
        base.OnDocumentChanged(oldDocument, newDocument);
        // Rebuild on the document SWAP itself (e.g. ResultPane assigns a
        // fresh TextDocument when the bound file changes). Per-text-change
        // rebuilds are NOT subscribed here — ResultPane.OnTextChanged is
        // the single trigger for that, calling Refresh() in lockstep with
        // the BG layer invalidation. Subscribing to TextChanged would
        // double the rebuild work on every state-mutation cycle.
        _subscribedDocument = newDocument;
        RebuildDisplayMap();
    }

    /// <summary>
    /// Recompute <see cref="_displayMap"/> from the current document text
    /// and the bound merge document / range states. Robust to a null
    /// merge document (returns a 1:1 sequential map matching the base
    /// margin's behaviour).
    /// </summary>
    private void RebuildDisplayMap()
    {
        var doc = Document;
        var lineCount = doc?.LineCount ?? 0;
        var mergeDoc = _getDocument();
        var states = _getStates();
        _displayMap = BuildDisplayMap(lineCount, mergeDoc, states);

        // Re-derive width-driving digit count: base class sizes the gutter to
        // hold `'9' * maxLineNumberLength`. With file-side numbers some entries
        // can exceed the doc's own line count (e.g. when the result pane shows
        // a small slice of a long ours-file). Track the actual maximum.
        int maxDigits = 2;
        for (int i = 1; i < _displayMap.Length; i++)
        {
            var n = _displayMap[i];
            if (n is null) continue;
            int digits = n.Value.ToString(CultureInfo.CurrentCulture).Length;
            if (digits > maxDigits) maxDigits = digits;
        }
        if (maxDigits != _maxDisplayedDigits)
        {
            _maxDisplayedDigits = maxDigits;
            // Forward to the protected base field so MeasureOverride sizes
            // the gutter to fit the widest displayed number.
            maxLineNumberLength = maxDigits;
        }
    }

    /// <summary>
    /// Pure walker that produces a per-document-line display number map
    /// (<c>null</c> = no number drawn). Exposed as <c>internal static</c> so
    /// unit tests can pin its behaviour without standing up a WPF visual
    /// tree. The walker iterates <see cref="MergeDocument.Ranges"/> in
    /// <see cref="ModifiedBaseRange.ResultMarkedRange"/> order, emitting:
    /// <list type="bullet">
    /// <item>Pre-range context: ours-pointer numbers (one per displayed line).</item>
    /// <item>Auto-merged ranges (<c>!IsConflicting</c>): ours-pointer numbers,
    /// one per <see cref="ModifiedBaseRange.ResultMarkedRange"/> line, then
    /// snap ours-pointer to <c>r.Ours.EndLineExclusive</c>.</item>
    /// <item>Unresolved conflicts: 4 marker lines (null) + ours/base/theirs
    /// content runs numbered from each side's <c>StartLine</c>.</item>
    /// <item>Resolved conflicts: the chosen side's lines numbered from that
    /// side's <c>StartLine</c>; AcceptBoth concatenates ours-then-theirs (or
    /// theirs-then-ours if <c>FirstOurs == false</c>); Manual gets nulls
    /// because the typed text has no canonical mapping.</item>
    /// </list>
    /// </summary>
    /// <param name="docLineCount">Total number of lines in the displayed result document (1-based count).</param>
    /// <param name="mergeDoc">The bound merge document, or <c>null</c> for a fallback 1:1 map.</param>
    /// <param name="states">Resolution states, or <c>null</c> to treat all conflicts as Unresolved.</param>
    internal static int?[] BuildDisplayMap(
        int docLineCount,
        MergeDocument? mergeDoc,
        IReadOnlyDictionary<int, ResolutionState>? states)
    {
        // Index 0 is unused — line numbers are 1-based to match AvalonEdit.
        var map = new int?[docLineCount + 1];
        if (docLineCount <= 0) return map;

        if (mergeDoc is null || mergeDoc.Ranges.Count == 0)
        {
            for (int i = 1; i <= docLineCount; i++) map[i] = i;
            return map;
        }

        // Iterate ONLY conflicting ranges. Auto-merged (!IsConflicting)
        // ranges live inside the result-pane's displayed text as plain
        // context lines — same content for both sides, no markers — so they
        // need no special handling beyond the ours-pointer counting that
        // every other context line gets. Treating them as ranges (the
        // earlier walker did) drove a "flipped numbering on conflict 2+"
        // bug: an auto-merged range with mismatched Ours.Length vs
        // ResultMarkedRange.Length advanced ours-pointer through its body
        // and the post-range snap couldn't fully undo the drift, shifting
        // every later conflict's marker pattern by N rows.
        //
        // Anchor positions in the displayed text via ResultMarkedRange,
        // which describes line spans in InitialMergedText. Context lines
        // (everything outside any conflicting range) are copied verbatim
        // from InitialMergedText into the result-pane document, so
        // displayed-line-count == InitialMergedText-line-count for any
        // gap between two conflicting ranges.
        var conflicts = mergeDoc.Ranges
            .Where(r => r.IsConflicting)
            .OrderBy(r => r.ResultMarkedRange.StartLine)
            .ToList();

        int oursPtr = 1;
        int docLine = 1;
        // Tracks where the previous conflict's marker block ended in
        // displayed-text coordinates. Starts at line 1 so the first
        // conflict's pre-context length resolves to (StartLine - 1).
        int prevDisplayedEndExclusive = 1;

        foreach (var r in conflicts)
        {
            // Pre-context: displayed lines between the previous conflict's
            // end and this conflict's start. These are file-context lines
            // (and any auto-merged regions inlined verbatim) — number them
            // sequentially with the ours-pointer. Math.Max guards against
            // a stale anchor producing a negative count (which would
            // otherwise be a no-op but crashes the algebra below).
            int contextLines = Math.Max(0, r.ResultMarkedRange.StartLine - prevDisplayedEndExclusive);
            for (int j = 0; j < contextLines && docLine <= docLineCount; j++)
            {
                map[docLine++] = oursPtr++;
            }

            if (docLine > docLineCount) break;

            // Body
            var state = states is not null && states.TryGetValue(r.Index, out var s)
                ? s
                : ResolutionState.Unresolved.Instance;
            int docLineBeforeBody = docLine;
            int slotStartNumber = r.Ours.StartLine > 0 ? r.Ours.StartLine : oursPtr;
            EmitConflictBody(map, ref docLine, docLineCount, r, state);
            int bodyDisplayedLines = docLine - docLineBeforeBody;

            // Snap ours-pointer past this range's body.
            //
            // Unresolved bodies: ours-coverage equals Ours.EndLineExclusive
            // — markers are still visible, the displayed body matches
            // InitialMergedText positions exactly.
            //
            // Resolved bodies: snap to slotStart + actual displayed body
            // lines. This is a deliberate trade-off — labels diverge from
            // "real ours-file line" coordinates but the gutter stays
            // monotonic. The alternative (always snap to
            // Ours.EndLineExclusive) keeps labels file-accurate but
            // introduces backward jumps in the gutter when the accepted
            // side is shorter than ours: e.g. ours.Length=3, AcceptTheirs
            // emits 1 line. With file-accurate snap, the body labels go
            // [slot, slot, slot] truncated to [slot] then post-conflict
            // jumps to slot+3 — gutter reads "5, 8, 9" with no 6 or 7.
            // Users called this "impossible" so we picked monotonic.
            // Math.Max forbids rewind for empty / out-of-order ranges.
            int snapTarget = state is ResolutionState.Unresolved
                ? r.Ours.EndLineExclusive
                : slotStartNumber + bodyDisplayedLines;
            if (snapTarget > 0)
            {
                oursPtr = Math.Max(oursPtr, snapTarget);
            }

            // Anchor the next pre-context calc against this conflict's
            // marked-range end (in InitialMergedText coords) for Unresolved
            // bodies; for resolved bodies whose displayed length differs,
            // the docLine delta is the source of truth. The contextLines
            // computation above uses the docLine delta implicitly because
            // docLine has advanced through this body — so the next
            // iteration's pre-context length lands correctly.
            prevDisplayedEndExclusive = r.ResultMarkedRange.EndLineExclusive;
        }

        // Trailing context: post-conflict file content.
        while (docLine <= docLineCount)
        {
            map[docLine++] = oursPtr++;
        }

        return map;
    }

    private static void EmitConflictBody(
        int?[] map, ref int docLine, int docLineCount,
        ModifiedBaseRange r, ResolutionState state)
    {
        switch (state)
        {
            case ResolutionState.Unresolved:
                // All three sections (ours / base / theirs) number from
                // Ours.StartLine — they're ALTERNATIVE content for the same
                // slot in the merged result file, not three separate file
                // positions. Numbering each section by its source file's
                // line counts produces non-monotonic gutters when ours is
                // longer than base/theirs (e.g. ours-content shows "52"
                // but base-content jumps backward to "48" because the base
                // file is shorter), which read as impossible to users.
                // Result-file slot numbering keeps the gutter monotonic
                // and matches "where this line would land if accepted".
                if (docLine <= docLineCount) map[docLine++] = null;     // <<<<<<<
                for (int j = 0; j < r.Ours.Length && docLine <= docLineCount; j++)
                    map[docLine++] = r.Ours.StartLine + j;
                if (docLine <= docLineCount) map[docLine++] = null;     // |||||||
                for (int j = 0; j < r.Base.Length && docLine <= docLineCount; j++)
                    map[docLine++] = r.Ours.StartLine + j;
                if (docLine <= docLineCount) map[docLine++] = null;     // =======
                for (int j = 0; j < r.Theirs.Length && docLine <= docLineCount; j++)
                    map[docLine++] = r.Ours.StartLine + j;
                if (docLine <= docLineCount) map[docLine++] = null;     // >>>>>>>
                return;

            case ResolutionState.AcceptOurs:
                for (int j = 0; j < r.Ours.Length && docLine <= docLineCount; j++)
                    map[docLine++] = r.Ours.StartLine + j;
                return;

            case ResolutionState.AcceptTheirs:
                // Theirs content occupies the same result-file slot as ours
                // would, so number from Ours.StartLine to keep the gutter
                // monotonic with surrounding context.
                for (int j = 0; j < r.Theirs.Length && docLine <= docLineCount; j++)
                    map[docLine++] = r.Ours.StartLine + j;
                return;

            case ResolutionState.AcceptBoth both:
                int totalBoth = (r.OursLines.Count == 0 ? r.Theirs.Length
                                : r.TheirsLines.Count == 0 ? r.Ours.Length
                                : r.Ours.Length + r.Theirs.Length);
                for (int j = 0; j < totalBoth && docLine <= docLineCount; j++)
                    map[docLine++] = r.Ours.StartLine + j;
                _ = both; // ordering doesn't change line-number sequencing
                return;

            case ResolutionState.Manual manual:
                // No canonical file-side number for free-form text. Skip
                // numbering for each manual line so the gutter reads as
                // "user-authored" rather than mis-attributing to ours/theirs.
                int manualLines = CountLines(manual.Text);
                for (int j = 0; j < manualLines && docLine <= docLineCount; j++)
                    map[docLine++] = null;
                return;

            default:
                // Match MergeDocument.AppendResolution's exhaustive-switch
                // convention — a future ResolutionState variant must update
                // this walker explicitly rather than silently fall through.
                throw new InvalidOperationException(
                    $"Unknown resolution state: {state.GetType().Name}");
        }
    }

    /// <summary>
    /// Count the displayed-line span of a free-form Manual resolution. Mirrors
    /// the line-emission convention in <see cref="MergeDocument.ComposeResolvedText"/>
    /// — including its <c>NormaliseToLf</c> pass that converts CR-only and
    /// CRLF endings to LF before counting. Without that normalisation a
    /// legacy-Mac-classic <c>"foo\rbar"</c> Manual paste counts as 1 line
    /// here but the composer emits 2, off-setting every conflict below.
    /// </summary>
    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int count = 1;
        char prev = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            // Match NormaliseToLf semantics: CRLF → \n, CR-only → \n.
            // Handle both by counting line breaks at any \n OR a \r not
            // followed by \n.
            if (c == '\n') count++;
            else if (c == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')) count++;
            prev = c;
        }
        // Trailing line terminator: composer treats a newline-terminated
        // text as N lines, not N+1. Cover \n, \r, and CRLF (the trailing
        // \n already accounted for by the \n branch).
        char last = text[text.Length - 1];
        if (last == '\n' || last == '\r') count--;
        _ = prev;
        return count == 0 ? 1 : count;
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        // Reserve RightPadding pixels of slack on top of the digit width so
        // the rendered numbers don't sit flush against the editor's first
        // text column. Re-uses the base class's typeface / emSize fields,
        // which it populates from the inherited TextBlock.FontSize property.
        var baseSize = base.MeasureOverride(availableSize);
        return new Size(baseSize.Width + RightPadding, baseSize.Height);
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawingContext)
    {
        TextView textView = TextView;
        Size renderSize = RenderSize;
        if (textView is null || !textView.VisualLinesValid) return;

        var foreground = (Brush)GetValue(Control.ForegroundProperty);
        foreach (VisualLine line in textView.VisualLines)
        {
            int lineNumber = line.FirstDocumentLine.LineNumber;
            // Out-of-range guard: TextChanged events can fire before our
            // RebuildDisplayMap pass; render nothing rather than crash on a
            // stale map.
            if (lineNumber < 1 || lineNumber >= _displayMap.Length) continue;
            var displayed = _displayMap[lineNumber];
            if (displayed is null) continue;

            var formatted = new FormattedText(
                displayed.Value.ToString(CultureInfo.CurrentCulture),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                emSize,
                foreground,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double y = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextTop);
            drawingContext.DrawText(formatted,
                new Point(renderSize.Width - formatted.Width - RightPadding, y - textView.VerticalOffset));
        }
    }
}
