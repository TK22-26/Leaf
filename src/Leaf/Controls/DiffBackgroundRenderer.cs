using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using Leaf.Models;

namespace Leaf.Controls;

/// <summary>
/// Background renderer for AvalonEdit that colors lines based on diff status.
/// AvalonEdit is licensed under MIT - see THIRD_PARTY_LICENSES.txt
/// </summary>
public class DiffBackgroundRenderer : IBackgroundRenderer
{
    private IReadOnlyList<DiffLine>? _lines;

    // Colors matching the existing app theme
    private static readonly SolidColorBrush AddedBrush = new(Color.FromArgb(0xB3, 0x14, 0x53, 0x2D));
    private static readonly SolidColorBrush DeletedBrush = new(Color.FromArgb(0xB3, 0x5F, 0x1E, 0x1E));
    private static readonly SolidColorBrush ImaginaryBrush = new(Color.FromArgb(0x40, 0x80, 0x80, 0x80));

    static DiffBackgroundRenderer()
    {
        AddedBrush.Freeze();
        DeletedBrush.Freeze();
        ImaginaryBrush.Freeze();
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void SetLines(IReadOnlyList<DiffLine>? lines)
    {
        _lines = lines;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_lines == null || _lines.Count == 0)
            return;

        textView.EnsureVisualLines();

        if (!textView.VisualLinesValid)
            return;

        foreach (var visualLine in textView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            if (lineNumber < 1 || lineNumber > _lines.Count)
                continue;

            var diffLine = _lines[lineNumber - 1];
            var brush = GetBrushForType(diffLine.Type);

            if (brush == null)
                continue;

            var y = visualLine.VisualTop - textView.VerticalOffset;
            var rect = new Rect(0, y, textView.ActualWidth, visualLine.Height);
            drawingContext.DrawRectangle(brush, null, rect);
        }
    }

    private static Brush? GetBrushForType(DiffLineType type)
    {
        return type switch
        {
            DiffLineType.Added => AddedBrush,
            DiffLineType.Deleted => DeletedBrush,
            DiffLineType.Imaginary => ImaginaryBrush,
            _ => null
        };
    }
}
