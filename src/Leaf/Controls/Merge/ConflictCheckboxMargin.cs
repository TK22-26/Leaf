using System.Windows;
using Leaf.Services;
using System.Windows.Input;
using System.Windows.Media;
using Leaf.TextEdit.Editing;
using Leaf.TextEdit.Rendering;
using Leaf.Models;

namespace Leaf.Controls.Merge;

/// <summary>
/// Custom margin that renders checkboxes for selectable conflict lines.
/// Follows AvalonEdit's AbstractMargin pattern (reference: LineNumberMargin).
/// </summary>
public sealed class ConflictCheckboxMargin : AbstractMargin
{
    private ConflictSideLineMapping? _mapping;
    private ConflictSide _side;
    private int _hoverLine = -1;

    private const double MarginWidth = 22;
    private const double BoxSize = 10;

    // Checkbox rendering brushes
    private static readonly Pen OursOutlinePen = CreateFrozenPen(Color.FromArgb(0x88, 0x88, 0xBB, 0xEE));
    private static readonly Pen TheirsOutlinePen = CreateFrozenPen(Color.FromArgb(0x88, 0x66, 0xCC, 0x88));
    private static readonly Brush OursFillBrush = CreateFrozenBrush(Color.FromRgb(0x2B, 0x4A, 0x6E));
    private static readonly Brush TheirsFillBrush = CreateFrozenBrush(Color.FromRgb(0x1A, 0x50, 0x35));
    private static readonly Brush HoverBrush = CreateFrozenBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
    private static readonly Pen CheckPen = CreateFrozenPen(Colors.White, 1.5);

    public void Configure(ConflictSideLineMapping? mapping, ConflictSide side)
    {
        _mapping = mapping;
        _side = side;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(MarginWidth, 0);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_mapping == null || TextView == null || !TextView.VisualLinesValid)
            return;

        foreach (var visualLine in TextView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            if (lineNumber < 1 || lineNumber > _mapping.TotalLines) continue;

            if (_mapping.IsHiddenMarginLine(lineNumber)) continue;
            var selectable = _mapping.GetSelectableLineForLine(lineNumber);
            if (selectable == null) continue;

            var y = visualLine.VisualTop - TextView.VerticalOffset;
            var boxX = (MarginWidth - BoxSize) / 2;
            var boxY = y + (visualLine.Height - BoxSize) / 2;
            var boxRect = new Rect(boxX, boxY, BoxSize, BoxSize);

            bool isOurs = _side == ConflictSide.Ours;
            var outlinePen = isOurs ? OursOutlinePen : TheirsOutlinePen;

            if (selectable.IsSelected)
            {
                var fill = isOurs ? OursFillBrush : TheirsFillBrush;
                drawingContext.DrawRoundedRectangle(fill, outlinePen, boxRect, 2, 2);
                DrawCheckmark(drawingContext, boxRect);
            }
            else
            {
                drawingContext.DrawRoundedRectangle(null, outlinePen, boxRect, 2, 2);
            }

            // Hover highlight
            if (lineNumber == _hoverLine)
            {
                drawingContext.DrawRoundedRectangle(HoverBrush, null, boxRect, 2, 2);
            }
        }
    }

    private static void DrawCheckmark(DrawingContext dc, Rect box)
    {
        // Simple checkmark path within the box
        double x = box.X, y = box.Y, w = box.Width, h = box.Height;
        var p1 = new Point(x + w * 0.22, y + h * 0.5);
        var p2 = new Point(x + w * 0.42, y + h * 0.72);
        var p3 = new Point(x + w * 0.78, y + h * 0.28);
        dc.DrawLine(CheckPen, p1, p2);
        dc.DrawLine(CheckPen, p2, p3);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (_mapping == null || TextView == null || e.ChangedButton != MouseButton.Left) return;

        var line = GetLineFromMousePosition(e);
        if (line < 1) return;

        var selectable = _mapping.GetSelectableLineForLine(line);
        if (selectable == null) return;

        selectable.IsSelected = !selectable.IsSelected;
        Log.Info("MergeUI", $"CheckboxToggle: line={line} selected={selectable.IsSelected} side={_side}");
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_mapping == null || TextView == null) return;

        var line = GetLineFromMousePosition(e);
        var selectable = line >= 1 ? _mapping.GetSelectableLineForLine(line) : null;

        Cursor = selectable != null ? Cursors.Hand : Cursors.Arrow;

        if (line != _hoverLine)
        {
            _hoverLine = line;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverLine != -1)
        {
            _hoverLine = -1;
            InvalidateVisual();
        }
        Cursor = Cursors.Arrow;
    }

    private int GetLineFromMousePosition(MouseEventArgs e)
    {
        if (TextView == null || !TextView.VisualLinesValid) return -1;

        var pos = e.GetPosition(TextView);
        var visualLine = TextView.GetVisualLineFromVisualTop(pos.Y + TextView.VerticalOffset);
        return visualLine?.FirstDocumentLine.LineNumber ?? -1;
    }

    private static Pen CreateFrozenPen(Color color, double thickness = 1)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
