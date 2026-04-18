#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Leaf.Controls.Merge;

/// <summary>
/// Compact status indicator — a colored dot followed by a label and a numeric
/// count, used in the merge editor header pills and (from V3 onward) anywhere
/// else a running tally needs a consistent visual language.
/// </summary>
/// <remarks>
/// Three <see cref="DependencyProperty"/> inputs: <see cref="Count"/>,
/// <see cref="Label"/>, <see cref="DotBrush"/>. Hosts bind these; the control
/// takes care of palette-driven typography and dot sizing.
/// </remarks>
public partial class StatusPill : UserControl
{
    public static readonly DependencyProperty CountProperty = DependencyProperty.Register(
        nameof(Count), typeof(int), typeof(StatusPill),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(StatusPill),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DotBrushProperty = DependencyProperty.Register(
        nameof(DotBrush), typeof(Brush), typeof(StatusPill),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public int Count
    {
        get => (int)GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public Brush DotBrush
    {
        get => (Brush)GetValue(DotBrushProperty);
        set => SetValue(DotBrushProperty, value);
    }

    public StatusPill()
    {
        InitializeComponent();
    }
}
