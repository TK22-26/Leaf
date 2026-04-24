#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    /// StickyConflictHeader.RefreshState / ConflictOverviewRuler.Refresh pattern.
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

    // Palette-derived curve chrome, wrapped for V8 runtime theme swap.
    // Previously separate static readonly fields; now rebuilt atomically
    // on MergeThemeSwitcher.PaletteChanged so a runtime theme flip
    // repaints the connection ribbons correctly. Each side's border
    // colour is tinted to ~69 % alpha so the curve reads as an accent
    // behind the pane backgrounds rather than a dominant graphic element.
    private const double CurvePenThickness = 2.0;

    private sealed class ThemeBrushes
    {
        public Brush Unresolved = null!;
        public Brush Ours = null!;
        public Brush Theirs = null!;
        public Brush Manual = null!;
        public Brush Both = null!;
        public Pen UnresolvedPen = null!;
        public Pen OursPen = null!;
        public Pen TheirsPen = null!;
        public Pen ManualPen = null!;
        public Pen BothPen = null!;

        public static ThemeBrushes Build()
        {
            var oursColor = MergePaletteResources.WithAlpha(
                MergePaletteResources.ResolveColor("Merge.Ours.Border.Color"), 0xB0);
            var theirsColor = MergePaletteResources.WithAlpha(
                MergePaletteResources.ResolveColor("Merge.Theirs.Border.Color"), 0xB0);
            var unresolvedColor = MergePaletteResources.WithAlpha(
                MergePaletteResources.ResolveColor("Merge.Text.Tertiary.Color"), 0x80);
            var manualColor = MergePaletteResources.WithAlpha(
                MergePaletteResources.ResolveColor("Merge.State.Warning.Color"), 0xC0);

            var unresolved = FreezeBrush(new SolidColorBrush(unresolvedColor));
            var ours = FreezeBrush(new SolidColorBrush(oursColor));
            var theirs = FreezeBrush(new SolidColorBrush(theirsColor));
            var manual = FreezeBrush(new SolidColorBrush(manualColor));
            var both = FreezeGradient(new LinearGradientBrush(oursColor, theirsColor, angle: 0));
            return new ThemeBrushes
            {
                Unresolved = unresolved,
                Ours = ours,
                Theirs = theirs,
                Manual = manual,
                Both = both,
                UnresolvedPen = FreezePen(MakeCurvePen(unresolved)),
                OursPen = FreezePen(MakeCurvePen(ours)),
                TheirsPen = FreezePen(MakeCurvePen(theirs)),
                ManualPen = FreezePen(MakeCurvePen(manual)),
                BothPen = FreezePen(MakeCurvePen(both)),
            };
        }
    }

    private static volatile ThemeBrushes _brushes = ThemeBrushes.Build();

    static PaneConnectionCanvas()
    {
        Leaf.Services.MergeThemeSwitcher.PaletteChanged += (_, _) =>
            _brushes = ThemeBrushes.Build();
    }

    private static Pen MakeCurvePen(Brush brush) => new(brush, CurvePenThickness)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
    };

    private static SolidColorBrush FreezeBrush(SolidColorBrush b) { b.Freeze(); return b; }
    private static LinearGradientBrush FreezeGradient(LinearGradientBrush b) { b.Freeze(); return b; }
    private static Pen FreezePen(Pen p) { p.Freeze(); return p; }

    // V7 hover-tooltip pipeline: the canvas records a list of curve
    // geometries + their associated range data on each OnRender pass, and
    // OnMouseMove walks them to find the curve under the pointer. Hit-test
    // is a StrokeContains check against a widened pen so fine bezier
    // arcs are still findable at normal pointer speeds.
    private readonly List<(Geometry Geom, ModifiedBaseRange Range)> _hoverableCurves = new();
    // Widened hit-test pen — the visible curves are 2 px wide but the user
    // doesn't need to hover on the exact stroke to get the tooltip.
    private static readonly Pen HoverHitTestPen = FreezePen(new Pen(Brushes.Black, thickness: 12.0));
    private int _hoveredRangeIndex = -1;

    /// <summary>
    /// Build the vertical opacity mask that fades curves near the canvas
    /// top and bottom into transparency so off-screen ends don't hard-cut
    /// against <see cref="FrameworkElement.ClipToBounds"/>. The fade range
    /// is a constant slice of the canvas, re-generated on height change.
    /// </summary>
    private static LinearGradientBrush BuildEdgeFadeMask(double canvasHeight)
    {
        // Fade over the first / last 16 px of the canvas. Values outside
        // that band are fully opaque (alpha 1.0). This matches the plan's
        // "LinearGradientBrush mask at top/bottom to fade off-screen ends"
        // — a 16 px ramp reads as a smooth fade without eating too much
        // of the visible connection area on short canvases.
        const double FadePx = 16.0;
        if (canvasHeight <= FadePx * 2)
        {
            // Too short for a two-sided fade: use a single symmetric
            // fade centred on the visible range. Prevents negative stops
            // on very narrow canvases (e.g. design-time preview).
            var simple = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Colors.Transparent, 0.0),
                    new(Colors.Black, 0.5),
                    new(Colors.Transparent, 1.0),
                },
                startPoint: new Point(0, 0),
                endPoint: new Point(0, 1));
            simple.Freeze();
            return simple;
        }

        var topStop = FadePx / canvasHeight;
        var bottomStop = 1.0 - topStop;
        var mask = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Colors.Transparent, 0.0),
                new(Colors.Black, topStop),
                new(Colors.Black, bottomStop),
                new(Colors.Transparent, 1.0),
            },
            startPoint: new Point(0, 0),
            endPoint: new Point(0, 1));
        mask.Freeze();
        return mask;
    }

    public PaneConnectionCanvas()
    {
        ClipToBounds = true;
        // V7: hit-test so curves can show hover tooltips. The canvas sits
        // in its own fixed-width column between Ours and Theirs — there
        // is no click target behind it to block. Tooltip strings are
        // range-specific and rebuilt each hover-change.
        IsHitTestVisible = true;
        SizeChanged += OnSizeChanged;
        // V8: static _brushes bundle rebuilds on palette swap; each live
        // instance still needs an explicit InvalidateVisual to repaint
        // with the new colours. Mirrors the ConflictMinimapPreview /
        // ConflictOverviewRuler subscribe-on-ctor, unsubscribe-on-Unloaded
        // pattern so the event doesn't retain detached canvases.
        Leaf.Services.MergeThemeSwitcher.PaletteChanged += OnPaletteChanged;
        Unloaded += (_, _) =>
            Leaf.Services.MergeThemeSwitcher.PaletteChanged -= OnPaletteChanged;
    }

    private void OnPaletteChanged(object? sender, EventArgs e) => InvalidateVisual();

    private double _lastMaskHeight = -1;

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Gate the mask rebuild on height-only changes. SizeChanged fires
        // for width deltas too (Ours/Theirs splitter drags, window
        // resizes affecting horizontal layout) — the fade mask is a
        // vertical gradient and only cares about height. Without this
        // gate a splitter drag would allocate+freeze a new LinearGradientBrush
        // every frame even though the mask shape is unchanged.
        if (Math.Abs(ActualHeight - _lastMaskHeight) < 0.5) return;
        _lastMaskHeight = ActualHeight;
        OpacityMask = BuildEdgeFadeMask(ActualHeight);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        _hoverableCurves.Clear();
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

            ResolutionState? state = null;
            RangeStates?.TryGetValue(range.Index, out state);
            var brush = BrushForState(state);
            var pen = PenForState(state);
            if (brush is null || pen is null) continue;

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
            var startPoint = new Point(CardCornerInset, yOurs);
            var endPoint = new Point(w - CardCornerInset, yTheirs);
            var figure = new PathFigure { StartPoint = startPoint };
            figure.Segments.Add(new BezierSegment(
                new Point(w / 3, yOurs),
                new Point(2 * w / 3, yTheirs),
                endPoint,
                isStroked: true));
            var geom = new PathGeometry();
            geom.Figures.Add(figure);
            geom.Freeze();
            dc.DrawGeometry(brush: null, pen, geom);

            // V7 arrowhead caps at both endpoints. Read as anchor anchors
            // rather than directional arrows since bezier direction is
            // ambiguous on an S-curve; the caps emphasise that the line
            // is tethered to both pane edges. Drawn with the same brush
            // as the curve so colour-coding carries to the cap tips.
            DrawArrowheadCap(dc, brush, pointingRight: false, at: startPoint);
            DrawArrowheadCap(dc, brush, pointingRight: true, at: endPoint);

            _hoverableCurves.Add((geom, range));
        }
    }

    /// <summary>
    /// Draw a small filled triangle capping a curve endpoint. The triangle
    /// base sits at <paramref name="at"/> and the tip projects outward
    /// (away from the canvas centre) so the arrowhead reads as a cap
    /// tethering the curve to the pane edge, not a harpoon-like tip
    /// pointing back into the ribbon. Sized to complement the 2 px curve
    /// stroke — large enough to read at a glance, small enough not to
    /// crowd the bezier.
    /// </summary>
    private static void DrawArrowheadCap(DrawingContext dc, Brush brush, bool pointingRight, Point at)
    {
        const double Size = 5.0;
        var dir = pointingRight ? 1 : -1;
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            // Tip projects outward by `Size` px; the base sits on the
            // curve endpoint at `at` (±Size/2 vertically). This caps the
            // stroke cleanly instead of overlapping it.
            ctx.BeginFigure(new Point(at.X + dir * Size, at.Y), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(at.X, at.Y - Size / 2), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(at.X, at.Y + Size / 2), isStroked: false, isSmoothJoin: false);
        }
        geom.Freeze();
        dc.DrawGeometry(brush, pen: null, geom);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        // Walk the hoverable-curve list in reverse so the last-drawn curve
        // (topmost visually) wins hit-test ties. StrokeContains with a
        // widened pen absorbs the small miss the user makes aiming at a
        // 2 px bezier.
        var point = e.GetPosition(this);
        int matchedIndex = -1;
        ModifiedBaseRange? matchedRange = null;
        for (int i = _hoverableCurves.Count - 1; i >= 0; i--)
        {
            var (geom, range) = _hoverableCurves[i];
            if (geom.StrokeContains(HoverHitTestPen, point))
            {
                matchedIndex = range.Index;
                matchedRange = range;
                break;
            }
        }

        if (matchedIndex == _hoveredRangeIndex) return;
        _hoveredRangeIndex = matchedIndex;

        // Use the tuple captured in the hit-test loop above instead of a
        // LINQ re-scan — avoids an enumerator allocation per hover change.
        ToolTip = matchedRange is null ? null : BuildHoverTooltip(matchedRange);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveredRangeIndex = -1;
        ToolTip = null;
    }

    /// <summary>
    /// Build the tooltip shown when the pointer hovers over a connection
    /// curve. Format: "Conflict N · Ours L1–L2 ↔ Theirs L3–L4", optionally
    /// followed by the first line snippet from each side so the user can
    /// identify the region without scrolling. Exposed <c>internal</c> so
    /// tests can assert the formatting without mounting a visual tree.
    /// </summary>
    internal static string BuildHoverTooltip(ModifiedBaseRange range)
    {
        static string FormatRange(LineRange r) =>
            r.IsEmpty ? "-"
            : r.StartLine == r.EndLineExclusive - 1
                ? r.StartLine.ToString()
                : $"{r.StartLine}–{r.EndLineExclusive - 1}";
        var header = $"Conflict {range.Index + 1} · Ours {FormatRange(range.Ours)} ↔ Theirs {FormatRange(range.Theirs)}";
        string? snippet = null;
        if (range.OursLines.Count > 0)
        {
            snippet = "Ours: " + Truncate(range.OursLines[0], maxChars: 80);
        }
        if (range.TheirsLines.Count > 0)
        {
            var theirs = "Theirs: " + Truncate(range.TheirsLines[0], maxChars: 80);
            snippet = snippet is null ? theirs : snippet + "\n" + theirs;
        }
        return snippet is null ? header : header + "\n" + snippet;
    }

    private static string Truncate(string s, int maxChars) =>
        s.Length <= maxChars ? s : s[..maxChars] + "…";

    private static Brush? BrushForState(ModifiedBaseRange range, IReadOnlyDictionary<int, ResolutionState>? rangeStates)
    {
        ResolutionState? state = null;
        rangeStates?.TryGetValue(range.Index, out state);
        return BrushForState(state);
    }

    /// <summary>
    /// State → brush kind mapping. Exposed as <c>internal</c> so tests can
    /// verify the colour-coding without standing up a real visual tree.
    /// Returns a brush from the current <see cref="ThemeBrushes"/> bundle,
    /// which is rebuilt atomically on a runtime palette swap.
    /// </summary>
    internal static Brush? BrushForState(ResolutionState? state)
    {
        var br = _brushes;
        return state switch
        {
            null or ResolutionState.Unresolved => br.Unresolved,
            ResolutionState.AcceptOurs => br.Ours,
            ResolutionState.AcceptTheirs => br.Theirs,
            ResolutionState.AcceptBoth => br.Both,
            ResolutionState.Manual => br.Manual,
            _ => null,
        };
    }

    /// <summary>Cached-pen lookup paired with <see cref="BrushForState"/>.</summary>
    private static Pen? PenForState(ResolutionState? state)
    {
        var br = _brushes;
        return state switch
        {
            null or ResolutionState.Unresolved => br.UnresolvedPen,
            ResolutionState.AcceptOurs => br.OursPen,
            ResolutionState.AcceptTheirs => br.TheirsPen,
            ResolutionState.AcceptBoth => br.BothPen,
            ResolutionState.Manual => br.ManualPen,
            _ => null,
        };
    }

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
