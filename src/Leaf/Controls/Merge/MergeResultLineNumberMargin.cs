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
/// Result-pane line-number gutter. Reads its per-line number map from a
/// shared <see cref="MergeDisplayMap"/> built once on
/// <see cref="MergeDocument"/> — the same map the
/// <see cref="ResultPaneBackgroundRenderer"/> and
/// <see cref="ConflictMarkerInlineGenerator"/> consume, eliminating the
/// three duplicated walkers that previously re-derived the same data.
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
/// </remarks>
public sealed class MergeResultLineNumberMargin : LineNumberMargin
{
    private readonly Func<MergeDocument?> _getDocument;
    private readonly Func<IReadOnlyDictionary<int, ResolutionState>?> _getStates;

    private MergeDisplayMap? _displayMap;
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

    private void RebuildDisplayMap()
    {
        var doc = Document;
        var lineCount = doc?.LineCount ?? 0;
        var mergeDoc = _getDocument();
        if (mergeDoc is null || lineCount <= 0)
        {
            _displayMap = null;
            return;
        }

        _displayMap = mergeDoc.BuildDisplayMap(lineCount, _getStates());

        // Re-derive width-driving digit count: base class sizes the gutter
        // to hold `'9' * maxLineNumberLength`. With file-side numbers some
        // entries can exceed the doc's own line count (e.g. when the result
        // pane shows a small slice of a long ours-file). Track the actual
        // maximum.
        int maxDigits = 2;
        for (int i = 1; i <= lineCount; i++)
        {
            var n = _displayMap.GetLine(i).FileLineNumber;
            if (n is null) continue;
            int digits = DigitCount(n.Value);
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
    /// Allocation-free digit count for non-negative ints — used per
    /// per-line lookup during measure, so avoids the ToString/.Length
    /// roundtrip that would otherwise allocate one string per non-null
    /// map entry every rebuild.
    /// </summary>
    private static int DigitCount(int n)
    {
        if (n < 0) n = -n;
        return n < 10 ? 1
            : n < 100 ? 2
            : n < 1000 ? 3
            : n < 10000 ? 4
            : n < 100000 ? 5
            : n < 1000000 ? 6
            : n < 10000000 ? 7
            : n < 100000000 ? 8
            : n < 1000000000 ? 9
            : 10;
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
        var map = _displayMap;
        foreach (VisualLine line in textView.VisualLines)
        {
            int lineNumber = line.FirstDocumentLine.LineNumber;
            int? displayed;
            if (map is null)
            {
                // No merge document bound — fall back to natural numbering
                // so the pane still renders something sensible (matches the
                // stock LineNumberMargin's behaviour).
                displayed = lineNumber;
            }
            else
            {
                displayed = map.GetLine(lineNumber).FileLineNumber;
            }
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
