using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Leaf.ViewModels;

namespace Leaf.Behaviors;

public static class TextBlockHelper
{
    public static readonly DependencyProperty HighlightSegmentsProperty =
        DependencyProperty.RegisterAttached(
            "HighlightSegments",
            typeof(List<HighlightSegment>),
            typeof(TextBlockHelper),
            new PropertyMetadata(null, OnHighlightSegmentsChanged));

    public static List<HighlightSegment>? GetHighlightSegments(DependencyObject obj) =>
        (List<HighlightSegment>?)obj.GetValue(HighlightSegmentsProperty);

    public static void SetHighlightSegments(DependencyObject obj, List<HighlightSegment>? value) =>
        obj.SetValue(HighlightSegmentsProperty, value);

    private static readonly SolidColorBrush MatchBrush = CreateFrozenBrush(0x28, 0xA7, 0x45);

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static void OnHighlightSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock) return;

        textBlock.Inlines.Clear();

        if (e.NewValue is not List<HighlightSegment> segments || segments.Count == 0)
            return;

        foreach (var segment in segments)
        {
            var run = new Run(segment.Text);
            if (segment.IsMatch)
            {
                run.FontWeight = FontWeights.Bold;
                run.Foreground = MatchBrush;
            }

            textBlock.Inlines.Add(run);
        }
    }
}
