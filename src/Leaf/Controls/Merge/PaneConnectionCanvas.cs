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

    /// <summary>
    /// Explicit refresh entry point for hosts that mutate RangeStates in
    /// place. Connection ribbons (resolved / unresolved / AcceptBoth) drive
    /// their colour off the dictionary — in-place mutation doesn't fire
    /// the DP change callback, so without this call the ribbon keeps the
    /// pre-action colour until the next layout invalidation. Mirrors the
    /// SegmentedAcceptPillOverlay.RefreshPillStates /
    /// StickyConflictHeader.RefreshState / ConflictMinimap.Refresh pattern.
    /// </summary>
    public void Refresh() => InvalidateVisual();

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

    // Matches the CornerRadius of Merge.PaneCard (MergeCardStyles.xaml).
    // Keeps bezier endpoints clear of the card's rounded-corner clip region
    // when this canvas is wrapped in a PaneCard.
    private const double CardCornerInset = 6.0;

    // Palette-derived curve colours. Each side's border colour is tinted to
    // ~69% alpha so the curve reads as an accent behind the pane backgrounds
    // rather than a dominant graphic element.
    private static readonly Color OursColor = MergePaletteResources.WithAlpha(
        MergePaletteResources.ResolveColor("Merge.Ours.Border.Color"), 0xB0);
    private static readonly Color TheirsColor = MergePaletteResources.WithAlpha(
        MergePaletteResources.ResolveColor("Merge.Theirs.Border.Color"), 0xB0);
    private static readonly Color UnresolvedColor = MergePaletteResources.WithAlpha(
        MergePaletteResources.ResolveColor("Merge.Text.Tertiary.Color"), 0x80);
    private static readonly Color ManualColor = MergePaletteResources.WithAlpha(
        MergePaletteResources.ResolveColor("Merge.State.Warning.Color"), 0xC0);

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
            var yOurs = ComputeEndpointY(range.Ours, lineHeight, OursVerticalOffset);
            var yTheirs = ComputeEndpointY(range.Theirs, lineHeight, TheirsVerticalOffset);

            // Skip entirely off-screen curves (both endpoints outside the canvas).
            if (IsEntirelyOffScreen(yOurs, yTheirs, h, lineHeight)) continue;

            var brush = BrushForState(range, RangeStates);
            if (brush is null) continue;
            var pen = new Pen(brush, 2.0);
            pen.Freeze();

            // Cubic bezier: p0 on the left at yOurs, p3 on the right at yTheirs,
            // controls horizontally at w/3 and 2w/3 with the same Y as their
            // endpoints for a smooth S-curve matching Meld's look.
            //
            // Endpoints are inset by CardCornerInset so curves stay within the
            // rounded PaneCard border region when this canvas is wrapped
            // as a card. Without the inset, beziers near y=0 / y=h whose start
            // or end X lands under the card's rounded corner arc would be
            // clipped by the corner. The visual cost — a ~6 px horizontal gap
            // between each curve and the Ours / Theirs pane edge — is small
            // enough to read as breathing room, not disconnection.
            var figure = new PathFigure { StartPoint = new Point(CardCornerInset, yOurs) };
            figure.Segments.Add(new BezierSegment(
                new Point(w / 3, yOurs),
                new Point(2 * w / 3, yTheirs),
                new Point(w - CardCornerInset, yTheirs),
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
        return BrushForState(state);
    }

    /// <summary>
    /// State → brush kind mapping. Exposed as <c>internal</c> so tests can
    /// verify the colour-coding without standing up a real visual tree.
    /// Returns one of the static Frozen brushes declared above.
    /// </summary>
    internal static Brush? BrushForState(ResolutionState? state) => state switch
    {
        null or ResolutionState.Unresolved => UnresolvedBrush,
        ResolutionState.AcceptOurs => OursBrush,
        ResolutionState.AcceptTheirs => TheirsBrush,
        ResolutionState.AcceptBoth => BothBrush,
        ResolutionState.Manual => ManualBrush,
        _ => null,
    };

    /// <summary>
    /// Compute the Y-coordinate (in canvas space) of a bezier endpoint for
    /// a given side of a <see cref="ModifiedBaseRange"/>. Exposes the pixel
    /// math used by <see cref="OnRender"/> so it can be tested independently.
    /// </summary>
    internal static double ComputeEndpointY(LineRange side, double lineHeight, double paneVerticalOffset)
    {
        // The bezier anchors on the centre of the side's line range, then
        // shifts to the centre of that midline. 1-based lines: StartLine==1
        // means "first line", so the geometric centre offset is (StartLine-1)*lineHeight.
        var midLine = (side.StartLine + side.EndLineExclusive) / 2.0 - 0.5;
        return (midLine - 1) * lineHeight - paneVerticalOffset + lineHeight / 2;
    }

    /// <summary>
    /// Whether a curve with the given endpoint Y-coords is entirely off-screen
    /// (both endpoints above the canvas top, or both below the canvas bottom,
    /// with a one-line padding). Used by <see cref="OnRender"/> to skip
    /// invisible curves and by tests to pin the clipping behaviour.
    /// </summary>
    internal static bool IsEntirelyOffScreen(double yOurs, double yTheirs, double canvasHeight, double lineHeight)
    {
        return (yOurs < -lineHeight && yTheirs < -lineHeight) ||
               (yOurs > canvasHeight + lineHeight && yTheirs > canvasHeight + lineHeight);
    }
}
