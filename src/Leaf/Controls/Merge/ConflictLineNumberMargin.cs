using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Leaf.TextEdit.Editing;

namespace Leaf.Controls.Merge;

/// <summary>
/// Custom line number margin that skips header lines inserted before conflict regions.
/// Uses ConflictSideLineMapping to remap editor line numbers to display line numbers.
/// </summary>
public sealed class ConflictLineNumberMargin : AbstractMargin
{
    private ConflictSideLineMapping? _mapping;
    private static readonly Typeface DefaultTypeface = new("Consolas");
    private const double FontSize = 12.5;
    private const double MarginWidth = 36;
    private const double RightPadding = 6;

    public void Configure(ConflictSideLineMapping? mapping)
    {
        _mapping = mapping;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(MarginWidth + RightPadding, 0);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_mapping == null || TextView == null || !TextView.VisualLinesValid)
            return;

        var foreground = (Brush)FindResource("TextFillColorTertiaryBrush");
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        foreach (var visualLine in TextView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            if (lineNumber < 1 || lineNumber > _mapping.TotalLines) continue;

            // Skip header and spacer lines entirely
            if (_mapping.IsHiddenMarginLine(lineNumber)) continue;

            var displayNumber = _mapping.GetDisplayLineNumber(lineNumber);
            if (displayNumber < 1) continue;

            var y = visualLine.VisualTop - TextView.VerticalOffset;
            var text = new FormattedText(
                displayNumber.ToString(),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                DefaultTypeface,
                FontSize,
                foreground,
                pixelsPerDip);

            // Right-align within the margin width
            var x = MarginWidth - text.Width;
            drawingContext.DrawText(text, new Point(x, y));
        }
    }
}
