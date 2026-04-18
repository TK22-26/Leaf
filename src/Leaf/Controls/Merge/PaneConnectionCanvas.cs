#nullable enable
using System.Windows;
using System.Windows.Media;
using Leaf.Models.Merge;
using Leaf.TextEdit;

namespace Leaf.Controls.Merge;

/// <summary>
/// Meld-inspired connection-line canvas that draws bezier curves between the
/// two input panes of the merge editor. Each unresolved conflict gets a curve
/// linking the Ours line-range to the Theirs line-range; resolved conflicts
/// change colour to match the chosen side (blue-only, green-only, or a
/// blue-to-green gradient for AcceptBoth).
/// </summary>
/// <remarks>
/// <para>
/// The canvas does not render per-line — it's a zero-width visual overlay
/// that sits in the column between Ours and Theirs and draws curves across
/// it. The host view supplies <see cref="OursVerticalOffset"/> and
/// <see cref="TheirsVerticalOffset"/> so the curves track the scroll state
/// of each pane independently.
/// </para>
/// <para>
/// Coordinates use <see cref="MergePaneGlyphLayout"/>'s LineHeight so all
/// endpoints are pixel-accurate against the actual rendered glyphs.
/// Off-screen endpoints are clipped naturally by <c>ClipToBounds</c>.
/// </para>
/// </remarks>
public sealed class PaneConnectionCanvas : FrameworkElement
{
    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(MergePaneGlyphLayout), typeof(PaneConnectionCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RegionsProperty = DependencyProperty.Register(
        nameof(Regions), typeof(IReadOnlyList<ModifiedBaseRange>), typeof(PaneConnectionCanvas),
        new FrameworkPropertyMetadata(Array.Empty<ModifiedBaseRange>(),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RangeStatesProperty = DependencyProperty.Register(
        nameof(RangeStates), typeof(IReadOnlyDictionary<int, ResolutionState>), typeof(PaneConnectionCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OursVerticalOffsetProperty = DependencyProperty.Register(
        nameof(OursVerticalOffset), typeof(double), typeof(PaneConnectionCanvas),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TheirsVerticalOffsetProperty = DependencyProperty.Register(
        nameof(TheirsVerticalOffset), typeof(double), typeof(PaneConnectionCanvas),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public MergePaneGlyphLayout? Layout
    {
        get => (MergePaneGlyphLayout?)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public IReadOnlyList<ModifiedBaseRange> Regions
    {
        get => (IReadOnlyList<ModifiedBaseRange>)GetValue(RegionsProperty);
        set => SetValue(RegionsProperty, value);
    }

    public IReadOnlyDictionary<int, ResolutionState>? RangeStates
    {
        get => (IReadOnlyDictionary<int, ResolutionState>?)GetValue(RangeStatesProperty);
        set => SetValue(RangeStatesProperty, value);
    }

    public double OursVerticalOffset
    {
        get => (double)GetValue(OursVerticalOffsetProperty);
        set => SetValue(OursVerticalOffsetProperty, value);
    }

    public double TheirsVerticalOffset
    {
        get => (double)GetValue(TheirsVerticalOffsetProperty);
        set => SetValue(TheirsVerticalOffsetProperty, value);
    }

    private static readonly Color OursColor = Color.FromArgb(0xB0, 0x2B, 0x4A, 0x6E);
    private static readonly Color TheirsColor = Color.FromArgb(0xB0, 0x1A, 0x50, 0x35);
    private static readonly Color UnresolvedColor = Color.FromArgb(0x80, 0x88, 0x88, 0x88);
    private static readonly Color ManualColor = Color.FromArgb(0xC0, 0xFF, 0xC1, 0x07);

    // Frozen brushes — allocating one per-range per-frame was both wasteful
    // and inconsistent with the pattern used by ConflictMinimap.
    private static readonly SolidColorBrush UnresolvedBrush = Freeze(new SolidColorBrush(UnresolvedColor));
    private static readonly SolidColorBrush OursBrush = Freeze(new SolidColorBrush(OursColor));
    private static readonly SolidColorBrush TheirsBrush = Freeze(new SolidColorBrush(TheirsColor));
    private static readonly SolidColorBrush ManualBrush = Freeze(new SolidColorBrush(ManualColor));
    private static readonly LinearGradientBrush BothBrush = FreezeGradient(
        new LinearGradientBrush(OursColor, TheirsColor, angle: 0));

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
    private static LinearGradientBrush FreezeGradient(LinearGradientBrush b) { b.Freeze(); return b; }

    public PaneConnectionCanvas()
    {
        ClipToBounds = true;
        IsHitTestVisible = false; // curves are visual-only; clicks pass through
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Layout is null || Regions is null) return;
        var lineHeight = Layout.LineHeight;
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        foreach (var range in Regions)
        {
            if (!range.IsConflicting) continue;

            // Y-coordinates on each side, accounting for pane scroll offsets.
            // The curve goes from the centre of the ours region to the centre of
            // the theirs region on the left/right edges of this canvas.
            var oursMidLine = (range.Ours.StartLine + range.Ours.EndLineExclusive) / 2.0 - 0.5;
            var theirsMidLine = (range.Theirs.StartLine + range.Theirs.EndLineExclusive) / 2.0 - 0.5;
            var yOurs = (oursMidLine - 1) * lineHeight - OursVerticalOffset + lineHeight / 2;
            var yTheirs = (theirsMidLine - 1) * lineHeight - TheirsVerticalOffset + lineHeight / 2;

            // Skip entirely off-screen curves (both endpoints outside the canvas).
            if ((yOurs < -lineHeight && yTheirs < -lineHeight) ||
                (yOurs > h + lineHeight && yTheirs > h + lineHeight))
                continue;

            var brush = BrushForState(range, RangeStates);
            if (brush is null) continue;
            var pen = new Pen(brush, 2.0);
            pen.Freeze();

            // Cubic bezier: p0 on left edge at yOurs, p3 on right edge at yTheirs,
            // controls horizontally at w/3 and 2w/3 with the same Y as their endpoints
            // to produce a smooth S-curve matching Meld's look.
            var figure = new PathFigure { StartPoint = new Point(0, yOurs) };
            figure.Segments.Add(new BezierSegment(
                new Point(w / 3, yOurs),
                new Point(2 * w / 3, yTheirs),
                new Point(w, yTheirs),
                isStroked: true));
            var geom = new PathGeometry();
            geom.Figures.Add(figure);
            geom.Freeze();
            dc.DrawGeometry(brush: null, pen, geom);
        }
    }

    private static Brush? BrushForState(ModifiedBaseRange range, IReadOnlyDictionary<int, ResolutionState>? rangeStates)
    {
        ResolutionState? state = null;
        rangeStates?.TryGetValue(range.Index, out state);
        return state switch
        {
            null or ResolutionState.Unresolved => UnresolvedBrush,
            ResolutionState.AcceptOurs => OursBrush,
            ResolutionState.AcceptTheirs => TheirsBrush,
            ResolutionState.AcceptBoth => BothBrush,
            ResolutionState.Manual => ManualBrush,
            _ => null,
        };
    }
}
