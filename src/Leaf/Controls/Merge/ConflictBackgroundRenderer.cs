using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;

namespace Leaf.Controls.Merge;

/// <summary>
/// IBackgroundRenderer that colors lines based on conflict state for side panes.
/// Follows the DiffBackgroundRenderer pattern with frozen static brushes.
/// </summary>
public sealed class ConflictBackgroundRenderer : IBackgroundRenderer
{
    private ConflictSideLineMapping? _mapping;
    private ConflictSide _side;
    private int _hoverLine = -1;

    // Ours palette (blue)
    private static readonly Brush OursConflictBrush = CreateFrozen(Color.FromArgb(0x1A, 0x2B, 0x4A, 0x6E));
    private static readonly Brush OursSelectedBrush = CreateFrozen(Color.FromArgb(0x99, 0x2B, 0x4A, 0x6E));
    private static readonly Brush OursHoverBrush = CreateFrozen(Color.FromArgb(0x44, 0x2B, 0x4A, 0x6E));

    // Theirs palette (green)
    private static readonly Brush TheirsConflictBrush = CreateFrozen(Color.FromArgb(0x1A, 0x1A, 0x50, 0x35));
    private static readonly Brush TheirsSelectedBrush = CreateFrozen(Color.FromArgb(0x99, 0x1A, 0x50, 0x35));
    private static readonly Brush TheirsHoverBrush = CreateFrozen(Color.FromArgb(0x44, 0x1A, 0x50, 0x35));

    // Resolved (shared)
    private static readonly Brush ResolvedBrush = CreateFrozen(Color.FromArgb(0x66, 0x22, 0xC5, 0x5E));

    public KnownLayer Layer => KnownLayer.Background;

    public void Configure(ConflictSideLineMapping? mapping, ConflictSide side)
    {
        _mapping = mapping;
        _side = side;
    }

    public void SetHoverLine(int line)
    {
        _hoverLine = line;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_mapping == null) return;

        if (!textView.VisualLinesValid) return;

        foreach (var visualLine in textView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            if (lineNumber < 1 || lineNumber > _mapping.TotalLines) continue;

            if (_mapping.IsHiddenMarginLine(lineNumber)) continue;
            var region = _mapping.GetRegionForLine(lineNumber);
            if (region == null || !region.IsConflict) continue;

            var selectable = _mapping.GetSelectableLineForLine(lineNumber);
            var brush = GetBrush(region, selectable, lineNumber);
            if (brush == null) continue;

            var y = visualLine.VisualTop - textView.VerticalOffset;
            var rect = new Rect(0, y, textView.ActualWidth, visualLine.Height);
            drawingContext.DrawRectangle(brush, null, rect);
        }
    }

    private Brush? GetBrush(Models.MergeRegion region, Models.SelectableLine? selectable, int lineNumber)
    {
        bool isOurs = _side == ConflictSide.Ours;

        // Resolved tint overrides all
        if (region.IsResolved)
            return ResolvedBrush;

        // Hover
        if (lineNumber == _hoverLine && selectable != null)
            return isOurs ? OursHoverBrush : TheirsHoverBrush;

        // Selected line
        if (selectable is { IsSelected: true })
            return isOurs ? OursSelectedBrush : TheirsSelectedBrush;

        // Base conflict tint
        return isOurs ? OursConflictBrush : TheirsConflictBrush;
    }

    private static Brush CreateFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
