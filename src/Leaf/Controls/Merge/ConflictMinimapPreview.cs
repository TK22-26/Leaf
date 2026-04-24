#nullable enable
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Leaf.Models.Merge;
using Leaf.TextEdit;

namespace Leaf.Controls.Merge;

/// <summary>
/// 60 px content-preview minimap (V6). Renders the file's actual text at
/// ~2 px line height alongside a translucent viewport rectangle that
/// shows what's currently visible in the paired <see cref="ReadOnlyMergePane"/>.
/// Click jumps to the clicked line; drag scrubs.
/// </summary>
/// <remarks>
/// <para>
/// Companion surface to <see cref="ConflictOverviewRuler"/>, not a
/// replacement. The ruler (12 px) shows conflict state ticks; this
/// preview (60 px) shows the actual content so users can orient by
/// visible code shape rather than just coloured ticks. Matches the
/// VS Code <c>editor.minimap</c> / Sublime Text minimap pattern.
/// </para>
/// <para>
/// <b>Rendering model.</b> Each line of <see cref="Lines"/> is drawn as a
/// single row at <see cref="LineRowHeight"/> pixels. Rather than a full
/// text layout pass, we sketch each line's non-whitespace runs as grey
/// rectangles — a per-char pixel is close to the actual type colour but
/// hundreds of times faster than a real <c>FormattedText</c> per line.
/// Conflict-range background tints overlay on top of the sketch so the
/// user can spot where conflicts live without reading individual chars.
/// </para>
/// <para>
/// <b>Viewport rect.</b> The host writes <see cref="VerticalOffset"/> and
/// <see cref="ViewportHeight"/> on every scroll. The preview paints a
/// translucent rectangle covering the Y range that maps to
/// (scrollOffset, scrollOffset + viewportHeight) in pane-pixel space.
/// Click/drag inside it calls <see cref="JumpRequested"/> with the
/// target line the user wants as the viewport midpoint.
/// </para>
/// </remarks>
public sealed class ConflictMinimapPreview : FrameworkElement
{
    /// <summary>One document line becomes this many preview pixels.</summary>
    internal const double LineRowHeight = 2.0;

    /// <summary>Fixed control width — the VS Code minimap reference point is 60 px.</summary>
    internal const double PreviewWidth = 60.0;

    public static readonly DependencyProperty LinesProperty = DependencyProperty.Register(
        nameof(Lines), typeof(IReadOnlyList<string>), typeof(ConflictMinimapPreview),
        new FrameworkPropertyMetadata(Array.Empty<string>(),
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RegionsProperty = DependencyProperty.Register(
        nameof(Regions), typeof(IReadOnlyList<ModifiedBaseRange>), typeof(ConflictMinimapPreview),
        new FrameworkPropertyMetadata(Array.Empty<ModifiedBaseRange>(),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RangeStatesProperty = DependencyProperty.Register(
        nameof(RangeStates), typeof(IReadOnlyDictionary<int, ResolutionState>), typeof(ConflictMinimapPreview),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SideProperty = DependencyProperty.Register(
        nameof(Side), typeof(MergePaneSide), typeof(ConflictMinimapPreview),
        new FrameworkPropertyMetadata(MergePaneSide.Ours,
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(MergePaneGlyphLayout), typeof(ConflictMinimapPreview),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.Register(
        nameof(VerticalOffset), typeof(double), typeof(ConflictMinimapPreview),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ViewportHeightProperty = DependencyProperty.Register(
        nameof(ViewportHeight), typeof(double), typeof(ConflictMinimapPreview),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<string> Lines
    {
        get => (IReadOnlyList<string>)GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
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

    public MergePaneSide Side
    {
        get => (MergePaneSide)GetValue(SideProperty);
        set => SetValue(SideProperty, value);
    }

    public MergePaneGlyphLayout? Layout
    {
        get => (MergePaneGlyphLayout?)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    /// <summary>Paired ScrollViewer's vertical offset in pane-pixel space.</summary>
    public double VerticalOffset
    {
        get => (double)GetValue(VerticalOffsetProperty);
        set => SetValue(VerticalOffsetProperty, value);
    }

    /// <summary>Paired ScrollViewer's visible height in pane-pixel space.</summary>
    public double ViewportHeight
    {
        get => (double)GetValue(ViewportHeightProperty);
        set => SetValue(ViewportHeightProperty, value);
    }

    /// <summary>
    /// Fires when the user clicks or drags the preview. Target line is
    /// passed as a 1-based index — the host scrolls the paired pane so
    /// that line is centred in its viewport (same contract as
    /// <see cref="ConflictOverviewRuler"/>).
    /// </summary>
    public event EventHandler<MinimapJumpEventArgs>? JumpRequested;

    /// <summary>Explicit refresh entry point mirroring the other overlay Refresh() methods.</summary>
    public void Refresh() => InvalidateVisual();

    // Palette-derived brushes. Sketch = flat dim foreground for non-
    // whitespace character runs; region overlays = same translucent tints
    // the ConflictOverviewRuler uses so the two surfaces read consistently.
    // Wrapped in a ThemeBrushes bundle so V8's MergeThemeSwitcher.PaletteChanged
    // event can invalidate and re-resolve on a runtime theme flip.
    private sealed class ThemeBrushes
    {
        public Brush Sketch = null!;
        public Brush Viewport = null!;
        public Pen ViewportBorderPen = null!;
        public Brush OursRegion = null!;
        public Brush TheirsRegion = null!;
        public Brush ResolvedRegion = null!;
        public Brush UnresolvedRegion = null!;

        public static ThemeBrushes Build() => new()
        {
            Sketch = FreezeBrush(new SolidColorBrush(MergePaletteResources.WithAlpha(
                MergePaletteResources.ResolveColor("Merge.Text.Secondary.Color"), 0x88))),
            Viewport = FreezeBrush(new SolidColorBrush(MergePaletteResources.WithAlpha(
                MergePaletteResources.ResolveColor("Merge.Text.Primary.Color"), 0x22))),
            ViewportBorderPen = FreezePen(new Pen(
                new SolidColorBrush(MergePaletteResources.WithAlpha(
                    MergePaletteResources.ResolveColor("Merge.Border.Strong.Color"), 0xAA)),
                thickness: 1.0)),
            OursRegion = FreezeBrush(new SolidColorBrush(MergePaletteResources.WithAlpha(
                MergePaletteResources.ResolveColor("Merge.Ours.Border.Color"), 0x55))),
            TheirsRegion = FreezeBrush(new SolidColorBrush(MergePaletteResources.WithAlpha(
                MergePaletteResources.ResolveColor("Merge.Theirs.Border.Color"), 0x55))),
            ResolvedRegion = FreezeBrush(new SolidColorBrush(MergePaletteResources.WithAlpha(
                MergePaletteResources.ResolveColor("Merge.State.Resolved.Color"), 0x55))),
            UnresolvedRegion = FreezeBrush(new SolidColorBrush(MergePaletteResources.WithAlpha(
                MergePaletteResources.ResolveColor("Merge.State.Unresolved.Color"), 0x55))),
        };
    }

    // Volatile so the UserPreferenceChanged-driven rebuild publishes the
    // new bundle safely to concurrent OnRender readers. Reassignment is
    // atomic on reference-sized fields, so OnRender sees either the old
    // or new bundle — never a torn value.
    private static volatile ThemeBrushes _brushes = ThemeBrushes.Build();

    static ConflictMinimapPreview()
    {
        // Subscribe once at type-init. MergeThemeSwitcher's lifetime is
        // the app; no unsubscribe needed.
        Leaf.Services.MergeThemeSwitcher.PaletteChanged += (_, _) =>
            _brushes = ThemeBrushes.Build();
    }

    private static SolidColorBrush FreezeBrush(SolidColorBrush b) { b.Freeze(); return b; }
    private static Pen FreezePen(Pen p) { p.Freeze(); return p; }

    public ConflictMinimapPreview()
    {
        ClipToBounds = true;
        Focusable = false;
        Width = PreviewWidth;
        Cursor = Cursors.Hand;
        // Per-instance subscription repaints the control after a theme
        // swap. Static type-init rebuilds the shared brush bundle; this
        // tells every live instance to re-render with the new bundle.
        Leaf.Services.MergeThemeSwitcher.PaletteChanged += OnPaletteChanged;
        Unloaded += (_, _) =>
            Leaf.Services.MergeThemeSwitcher.PaletteChanged -= OnPaletteChanged;
    }

    private void OnPaletteChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected override Size MeasureOverride(Size availableSize)
    {
        var h = double.IsPositiveInfinity(availableSize.Height) ? 0 : availableSize.Height;
        return new Size(PreviewWidth, h);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Lines.Count == 0 || ActualHeight <= 0) return;

        // Sketch: one 2px-tall row per line. Each non-whitespace run becomes a
        // dim rectangle at the run's relative column position. Width caps at
        // PreviewWidth so long lines don't spill.
        var charWidth = PreviewWidth / 80.0; // target 80-col display
        int renderableLines = Math.Min(Lines.Count, (int)(ActualHeight / LineRowHeight));
        for (int i = 0; i < renderableLines; i++)
        {
            var y = i * LineRowHeight;
            var line = Lines[i];
            int runStart = -1;
            for (int col = 0; col <= line.Length; col++)
            {
                bool isNonWs = col < line.Length && !char.IsWhiteSpace(line[col]);
                if (isNonWs && runStart < 0) runStart = col;
                else if (!isNonWs && runStart >= 0)
                {
                    var x0 = Math.Min(runStart * charWidth, PreviewWidth);
                    var x1 = Math.Min(col * charWidth, PreviewWidth);
                    dc.DrawRectangle(_brushes.Sketch, pen: null,
                        new Rect(x0, y, Math.Max(0.5, x1 - x0), LineRowHeight - 0.5));
                    runStart = -1;
                }
            }
        }

        // Conflict region overlays. Paint on top of the sketch so the
        // conflict block reads as tinted code, not just a colour band.
        foreach (var range in Regions)
        {
            var sideRange = Side switch
            {
                MergePaneSide.Ours => range.Ours,
                MergePaneSide.Theirs => range.Theirs,
                MergePaneSide.Base => range.Base,
                _ => LineRange.Empty,
            };
            if (sideRange.IsEmpty) continue;
            var y0 = (sideRange.StartLine - 1) * LineRowHeight;
            var y1 = (sideRange.EndLineExclusive - 1) * LineRowHeight;
            if (y1 <= 0 || y0 >= ActualHeight) continue;

            Brush regionBrush;
            if (!range.IsConflicting)
            {
                // Auto-merged region — tint in the side's colour at low alpha.
                regionBrush = Side == MergePaneSide.Ours ? _brushes.OursRegion : _brushes.TheirsRegion;
            }
            else
            {
                var resolved = RangeStates is not null
                    && RangeStates.TryGetValue(range.Index, out var state)
                    && state is not ResolutionState.Unresolved;
                regionBrush = resolved ? _brushes.ResolvedRegion : _brushes.UnresolvedRegion;
            }
            dc.DrawRectangle(regionBrush, pen: null, new Rect(0, y0, ActualWidth, Math.Max(1.0, y1 - y0)));
        }

        // Viewport rectangle. LineHeight on the paired pane maps
        // pane-pixel offsets to line indices; preview-pixel Y = lineIndex
        // × LineRowHeight.
        if (Layout is { LineHeight: > 0 } layout && ViewportHeight > 0)
        {
            var topLineIdx = VerticalOffset / layout.LineHeight;
            var visibleLines = ViewportHeight / layout.LineHeight;
            var viewY = topLineIdx * LineRowHeight;
            var viewH = Math.Max(LineRowHeight, visibleLines * LineRowHeight);
            // Clip to the preview's own height so a large document doesn't
            // draw a rectangle outside the visual bounds (ClipToBounds does
            // this too, but explicit clamp keeps the drawn rect honest).
            viewH = Math.Min(viewH, ActualHeight - viewY);
            if (viewH > 0)
            {
                dc.DrawRectangle(_brushes.Viewport, _brushes.ViewportBorderPen,
                    new Rect(0.5, viewY + 0.5, ActualWidth - 1, viewH - 1));
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse();
        RaiseJumpForPointer(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            RaiseJumpForPointer(e.GetPosition(this));
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private void RaiseJumpForPointer(Point pos)
    {
        if (Lines.Count == 0 || ActualHeight <= 0) return;
        var line = PointerYToLine(pos.Y, Lines.Count);
        JumpRequested?.Invoke(this, new MinimapJumpEventArgs(line));
    }

    /// <summary>
    /// Map a pointer Y to a 1-based line index in the preview. Uses the
    /// same <see cref="LineRowHeight"/> constant the renderer uses so a
    /// click on visible content lands on that line. Out-of-bounds Y
    /// values clamp to the first or last line — matches the
    /// <see cref="ConflictOverviewRuler.PointerYToLine"/> contract so
    /// handlers can be shared between the two surfaces.
    /// </summary>
    public static int PointerYToLine(double y, int lineCount)
    {
        if (lineCount <= 0) return 1;
        if (y <= 0) return 1;
        var line = (int)Math.Floor(y / LineRowHeight) + 1;
        return Math.Clamp(line, 1, lineCount);
    }
}
