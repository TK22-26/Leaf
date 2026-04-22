#nullable enable
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Leaf.Models.Merge;
using Leaf.Services.Merge;
using Leaf.TextEdit;
using Leaf.TextEdit.Document;
using Leaf.TextEdit.Highlighting;

namespace Leaf.Controls.Merge;

/// <summary>
/// Read-only text pane used for the Ours / Theirs / Base sides of the merge editor.
/// Renders text directly via <see cref="FormattedText"/> through the shared
/// <see cref="MergePaneGlyphLayout"/> so it aligns pixel-perfectly with the vendored
/// Result pane. Draws its own conflict-region highlights and change-bars
/// — no AvalonEdit margins / renderers / overlays involved.
/// </summary>
/// <remarks>
/// <para>
/// This control is intentionally not a <see cref="Leaf.TextEdit.TextEditor"/>:
/// the merge input panes don't need caret, IME, text selection editing, clipboard,
/// undo, or any editing infrastructure. A purpose-built renderer is simpler, faster,
/// and removes coordinate-translation friction for the editor's chrome
/// (connection lines, minimap, gutter change-bars).
/// </para>
/// <para>
/// Scrolling is handled via <see cref="IScrollInfo"/> so the control plugs into a
/// parent <see cref="System.Windows.Controls.ScrollViewer"/>; the ScrollSynchronizer
/// uses the standard <c>VerticalOffset</c> to keep panes aligned.
/// </para>
/// <para>
/// C2 removed the per-side accept checkbox that this pane used to draw in its
/// left gutter. The three-cell <see cref="SegmentedAcceptPill"/> now lives on
/// the result pane and replaces the ambiguous two-checkbox toggle UX.
/// </para>
/// </remarks>
public sealed class ReadOnlyMergePane : FrameworkElement, IScrollInfo
{
    // ── Inputs: dependency properties so the view can bind via XAML ───────────────

    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(MergePaneGlyphLayout), typeof(ReadOnlyMergePane),
        new FrameworkPropertyMetadata(defaultValue: null,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            OnLayoutChanged));

    public static readonly DependencyProperty LinesProperty = DependencyProperty.Register(
        nameof(Lines), typeof(IReadOnlyList<string>), typeof(ReadOnlyMergePane),
        new FrameworkPropertyMetadata(Array.Empty<string>(),
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            OnFilePathOrLinesChanged));

    public static readonly DependencyProperty RegionsProperty = DependencyProperty.Register(
        nameof(Regions), typeof(IReadOnlyList<ModifiedBaseRange>), typeof(ReadOnlyMergePane),
        new FrameworkPropertyMetadata(Array.Empty<ModifiedBaseRange>(),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RangeStatesProperty = DependencyProperty.Register(
        nameof(RangeStates), typeof(IReadOnlyDictionary<int, ResolutionState>), typeof(ReadOnlyMergePane),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SideProperty = DependencyProperty.Register(
        nameof(Side), typeof(MergePaneSide), typeof(ReadOnlyMergePane),
        new FrameworkPropertyMetadata(MergePaneSide.Ours,
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighlightBrushProperty = DependencyProperty.Register(
        nameof(HighlightBrush), typeof(Brush), typeof(ReadOnlyMergePane),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(ReadOnlyMergePane),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Per-conflict-region word-level diff segments keyed by range index. Populated
    /// by the VM when it constructs a <see cref="MergeDocument"/>; each value is
    /// the list of <see cref="TokenSegment"/> for <em>this</em> side's lines inside
    /// that conflict range. Null entries and missing keys are drawn without
    /// highlights (falls back to region-level background only).
    /// </summary>
    public static readonly DependencyProperty WordDiffsProperty = DependencyProperty.Register(
        nameof(WordDiffs), typeof(IReadOnlyDictionary<int, IReadOnlyList<TokenLine>>), typeof(ReadOnlyMergePane),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// File path of the conflict this pane is displaying. C1 uses this to
    /// resolve an <see cref="IHighlightingDefinition"/> by extension through
    /// <see cref="HighlightingManager.Instance"/> so code lines get token
    /// colours instead of rendering in a single <see cref="Foreground"/>.
    /// Null or an unknown extension disables highlighting.
    /// </summary>
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath), typeof(string), typeof(ReadOnlyMergePane),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender,
            OnFilePathOrLinesChanged));

    /// <summary>Shared <see cref="MergePaneGlyphLayout"/>; required before the pane can render.</summary>
    public MergePaneGlyphLayout? Layout
    {
        get => (MergePaneGlyphLayout?)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    /// <summary>Lines to render. Each element is one logical line, no terminator.</summary>
    public IReadOnlyList<string> Lines
    {
        get => (IReadOnlyList<string>)GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    /// <summary>Conflict ranges that apply to this side; used to draw region backgrounds + change-bars.</summary>
    public IReadOnlyList<ModifiedBaseRange> Regions
    {
        get => (IReadOnlyList<ModifiedBaseRange>)GetValue(RegionsProperty);
        set => SetValue(RegionsProperty, value);
    }

    /// <summary>Current resolution state per <see cref="ModifiedBaseRange.Index"/>.</summary>
    public IReadOnlyDictionary<int, ResolutionState>? RangeStates
    {
        get => (IReadOnlyDictionary<int, ResolutionState>?)GetValue(RangeStatesProperty);
        set => SetValue(RangeStatesProperty, value);
    }

    /// <summary>Which side of the merge this pane shows (<see cref="MergePaneSide"/>).</summary>
    public MergePaneSide Side
    {
        get => (MergePaneSide)GetValue(SideProperty);
        set => SetValue(SideProperty, value);
    }

    /// <summary>Background brush for conflict regions on this side (tinted per-side).</summary>
    public Brush HighlightBrush
    {
        get => (Brush)GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    /// <summary>Foreground brush for text.</summary>
    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Per-conflict-region word-level diff segments keyed by range index.
    /// See <see cref="WordDiffsProperty"/> for semantics.
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<TokenLine>>? WordDiffs
    {
        get => (IReadOnlyDictionary<int, IReadOnlyList<TokenLine>>?)GetValue(WordDiffsProperty);
        set => SetValue(WordDiffsProperty, value);
    }

    /// <summary>
    /// File path of the conflict this pane is displaying. See
    /// <see cref="FilePathProperty"/>.
    /// </summary>
    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    private const double GutterWidth = 48;       // line numbers
    private const double ChangeBarX = 2;         // 2 px inset from the left edge
    private const double ChangeBarWidth = 2;     // 2 px bar per plan
    private const double DeletionCaretHalfHeight = 5; // caret spans 10 px total

    private ScrollViewer? _scrollOwner;
    private double _verticalOffset;
    private double _horizontalOffset;
    private Size _extent;
    private Size _viewport;

    // V5 animation bookkeeping. Tracks 350 ms resolved-overlay fade-in per
    // range (Merge.Motion.RangeResolve); the pre-C2 checkbox bounce + fill
    // crossfade are gone because the checkbox itself is gone (replaced by
    // SegmentedAcceptPill on the result pane). A single DispatcherTimer
    // repaints at ~60 Hz whenever the dictionary has an active entry and
    // stops when empty. Start times use Stopwatch.GetTimestamp() so NTP
    // slews / DST adjustments can't freeze or skip animations.
    private const double RangeResolveDurationMs = 350.0;
    private readonly Dictionary<int, long> _rangeResolveStarts = new();
    private DispatcherTimer? _animationTicker;
    // Reused during OnRender to avoid a per-frame SolidColorBrush allocation
    // while the resolved overlay is fading in. Never frozen — OnRender mutates
    // .Color before each DrawRectangle call.
    private readonly SolidColorBrush _fadedOverlayBrush = new();

    private static long NowTicks() => System.Diagnostics.Stopwatch.GetTimestamp();
    private static double TicksToMs(long ticks) =>
        ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    public ReadOnlyMergePane()
    {
        Focusable = false;
        ClipToBounds = true;
        // Tear the dispatcher-timer ticker down when the pane leaves the
        // visual tree. Without this the timer's Tick handler keeps the pane
        // rooted through the dispatcher's timer queue across repo switches
        // and merge aborts — parity with the ResultPane / StickyConflictHeader
        // detach pattern.
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_animationTicker is not null)
        {
            _animationTicker.Stop();
            _animationTicker.Tick -= OnAnimationTick;
            _animationTicker = null;
        }
        _rangeResolveStarts.Clear();
    }

    // C1 syntax-highlighting bookkeeping. Rebuilt when Lines or FilePath
    // changes; DrawText consumes _documentHighlighter to produce per-line
    // token brushes through FormattedText.SetForegroundBrush. When the file
    // has no known highlighting extension, both fields stay null and the
    // draw path falls back to the plain Foreground colour.
    private TextDocument? _highlightDocument;
    private DocumentHighlighter? _documentHighlighter;

    private static void OnFilePathOrLinesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var pane = (ReadOnlyMergePane)d;
        pane.RebuildHighlighter();
    }

    private void RebuildHighlighter()
    {
        var definition = MergeHighlightingResolver.ByFilePath(FilePath);
        if (definition is null || Lines.Count == 0)
        {
            _highlightDocument = null;
            _documentHighlighter = null;
            return;
        }
        // Stream characters instead of allocating a single joined string —
        // matters for monorepo-scale conflict files (100k+ lines) where a
        // string.Join would burn a second copy of the merged content on every
        // Lines / FilePath change. The highlighter still lazily spans sections
        // per line; the TextDocument's own offset table is the only unavoidable
        // linear walk.
        _highlightDocument = new TextDocument(StreamLinesAsChars(Lines));
        _documentHighlighter = new DocumentHighlighter(_highlightDocument, definition);
    }

    private static IEnumerable<char> StreamLinesAsChars(IReadOnlyList<string> lines)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            foreach (var c in lines[i]) yield return c;
            if (i < lines.Count - 1) yield return '\n';
        }
    }

    /// <summary>
    /// Mark <paramref name="rangeIndex"/> as just-resolved so the next render
    /// fades the resolved overlay in over <see cref="RangeResolveDurationMs"/>
    /// ms. Calling again with the same index before the animation completes
    /// restarts it — matches the user expectation that clicking the accept
    /// button always pulses the region regardless of prior state.
    /// </summary>
    public void StartRangeResolveAnimation(int rangeIndex)
    {
        _rangeResolveStarts[rangeIndex] = NowTicks();
        EnsureAnimationTicker();
    }

    private void EnsureAnimationTicker()
    {
        if (_animationTicker is null)
        {
            _animationTicker = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16),
            };
            _animationTicker.Tick += OnAnimationTick;
        }
        if (!_animationTicker.IsEnabled)
        {
            _animationTicker.Start();
        }
        InvalidateVisual();
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        var now = NowTicks();
        PruneCompleted(_rangeResolveStarts, now, RangeResolveDurationMs);
        if (_rangeResolveStarts.Count == 0)
        {
            _animationTicker?.Stop();
        }
        InvalidateVisual();
    }

    private static void PruneCompleted(Dictionary<int, long> starts, long nowTicks, double durationMs)
    {
        List<int>? completed = null;
        foreach (var kvp in starts)
        {
            if (TicksToMs(nowTicks - kvp.Value) >= durationMs)
            {
                completed ??= new List<int>();
                completed.Add(kvp.Key);
            }
        }
        if (completed is not null)
        {
            foreach (var key in completed) starts.Remove(key);
        }
    }

    /// <summary>
    /// 0→1 alpha multiplier for the resolved overlay on <paramref name="rangeIndex"/>.
    /// Returns 1.0 when no animation is in flight (the overlay renders at its
    /// natural alpha); returns an ease-out-quadratic-interpolated value while
    /// the 350 ms fade is running.
    /// </summary>
    private double ResolvedOverlayAlphaFor(int rangeIndex)
    {
        if (!_rangeResolveStarts.TryGetValue(rangeIndex, out var start)) return 1.0;
        var elapsed = TicksToMs(NowTicks() - start);
        var t = Math.Clamp(elapsed / RangeResolveDurationMs, 0.0, 1.0);
        // Quadratic ease-out: 1 - (1 - t)^2 — matches Merge.Motion.Ease.
        return 1.0 - ((1.0 - t) * (1.0 - t));
    }


    // ── Measurement / rendering ──────────────────────────────────────────────────

    private double LineHeight => Layout?.LineHeight ?? 16;

    private double TotalContentHeight => Lines.Count * LineHeight;

    private double TotalContentWidth
    {
        get
        {
            if (Layout is null) return 0;
            var maxGlyphs = 0;
            foreach (var line in Lines) maxGlyphs = Math.Max(maxGlyphs, line.Length);
            return GutterWidth + maxGlyphs * Layout.AdvanceWidth + 16;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsPositiveInfinity(availableSize.Width)
            ? TotalContentWidth : availableSize.Width;
        var height = double.IsPositiveInfinity(availableSize.Height)
            ? TotalContentHeight : availableSize.Height;

        var desired = new Size(width, height);
        UpdateScrollInfo(desired);
        return desired;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        UpdateScrollInfo(finalSize);
        return finalSize;
    }

    private void UpdateScrollInfo(Size size)
    {
        var newExtent = new Size(TotalContentWidth, TotalContentHeight);
        var newViewport = new Size(size.Width, size.Height);
        if (newExtent != _extent || newViewport != _viewport)
        {
            _extent = newExtent;
            _viewport = newViewport;
            _scrollOwner?.InvalidateScrollInfo();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (Layout is null) return;

        var lineHeight = LineHeight;
        var firstVisible = Math.Max(0, (int)Math.Floor(_verticalOffset / lineHeight));
        var lastVisible = Math.Min(Lines.Count - 1,
            (int)Math.Ceiling((_verticalOffset + _viewport.Height) / lineHeight));
        if (firstVisible > lastVisible) return;

        // 1. Region background highlights.
        DrawRegionBackgrounds(drawingContext, firstVisible, lastVisible);

        // 2. Word-level highlights inside each conflict region on this side.
        DrawWordHighlights(drawingContext, firstVisible, lastVisible);

        // 3. Per-line change-bars (solid for additions on this side, dashed
        //    marker for deletions). Drawn before the gutter so line numbers
        //    sit on top of the bar decoration.
        DrawChangeBars(drawingContext, firstVisible, lastVisible);

        // 4. Line numbers gutter.
        DrawGutter(drawingContext, firstVisible, lastVisible);

        // 5. Text lines.
        DrawText(drawingContext, firstVisible, lastVisible);
    }

    private void DrawChangeBars(DrawingContext dc, int firstVisible, int lastVisible)
    {
        if (Regions is null || Regions.Count == 0) return;

        var barBrush = Side switch
        {
            MergePaneSide.Ours => ChangeBarOursBrush,
            MergePaneSide.Theirs => ChangeBarTheirsBrush,
            MergePaneSide.Base => ChangeBarBaseBrush,
            _ => (Brush)Brushes.Transparent,
        };
        var dashedPen = Side switch
        {
            MergePaneSide.Ours => ChangeBarOursDashedPen,
            MergePaneSide.Theirs => ChangeBarTheirsDashedPen,
            MergePaneSide.Base => ChangeBarBaseDashedPen,
            _ => (Pen?)null,
        };

        foreach (var range in Regions)
        {
            var sideRange = GetSideRange(range);
            if (sideRange.IsEmpty)
            {
                // Deletion marker. sideRange.StartLine is the 1-based line
                // where the deletion occurred on this side — render a short
                // dashed caret between the line above and below so the user
                // sees that lines went missing here.
                var anchorLine0 = sideRange.StartLine - 1;
                if (anchorLine0 < firstVisible || anchorLine0 > lastVisible) continue;
                if (dashedPen is null) continue;

                // Skip deletion markers for auto-merged ranges that also have
                // no content on the other sides — those aren't meaningful
                // signals, just noise.
                if (range.Ours.IsEmpty && range.Theirs.IsEmpty && range.Base.IsEmpty) continue;

                var y = anchorLine0 * LineHeight - _verticalOffset;
                // 10 px dashed caret centred on the line boundary — tall
                // enough that the 2 px dash pattern reads as a sequence of
                // ticks rather than a single blip. Top clamped so the caret
                // never renders above the viewport.
                var top = Math.Max(0, y - DeletionCaretHalfHeight);
                var bottom = y + DeletionCaretHalfHeight;
                dc.DrawLine(dashedPen, new Point(ChangeBarX, top), new Point(ChangeBarX, bottom));
            }
            else
            {
                var firstLine0 = sideRange.StartLine - 1;
                var lastLine0 = sideRange.EndLineExclusive - 2; // inclusive
                if (lastLine0 < firstVisible || firstLine0 > lastVisible) continue;

                var clippedFirst = Math.Max(firstLine0, firstVisible);
                var clippedLast = Math.Min(lastLine0, lastVisible);
                var y = clippedFirst * LineHeight - _verticalOffset;
                var h = (clippedLast - clippedFirst + 1) * LineHeight;
                dc.DrawRectangle(barBrush, pen: null,
                    new Rect(ChangeBarX, y, ChangeBarWidth, h));
            }
        }
    }

    private void DrawWordHighlights(DrawingContext dc, int firstVisible, int lastVisible)
    {
        if (Layout is null || WordDiffs is null) return;
        var textX = GutterWidth + 4 - _horizontalOffset;
        var accent = Side switch
        {
            MergePaneSide.Ours => OursWordAccent,
            MergePaneSide.Theirs => TheirsWordAccent,
            _ => (Brush)Brushes.Transparent,
        };

        foreach (var range in Regions)
        {
            if (!range.IsConflicting) continue;
            if (!WordDiffs.TryGetValue(range.Index, out var tokenLines) || tokenLines is null) continue;

            var sideRange = GetSideRange(range);
            if (sideRange.IsEmpty) continue;
            var firstLine0 = sideRange.StartLine - 1;
            var lastLine0 = sideRange.EndLineExclusive - 1 - 1;
            if (lastLine0 < firstVisible || firstLine0 > lastVisible) continue;

            for (int i = 0; i < tokenLines.Count; i++)
            {
                var line0 = firstLine0 + i;
                if (line0 < firstVisible || line0 > lastVisible) continue;
                var y = line0 * LineHeight - _verticalOffset;
                var lineText = tokenLines[i].Text;
                if (lineText.Length == 0) continue;

                // Use FormattedText.BuildHighlightGeometry for pixel-accurate rects
                // that honor tabs, surrogate pairs, and proportional-font glyphs —
                // the previous (column-index × advance-width) math broke for
                // tab-indented code and supplementary-plane characters.
                var ft = Layout.BuildFormattedText(lineText);
                foreach (var seg in tokenLines[i].Segments)
                {
                    if (seg.Kind == TokenKind.Unchanged) continue;
                    var start0 = seg.StartColumn - 1;
                    var length = seg.EndColumnExclusive - seg.StartColumn;
                    if (length <= 0 || start0 < 0 || start0 + length > lineText.Length) continue;
                    var geom = ft.BuildHighlightGeometry(new Point(textX, y), start0, length);
                    if (geom is not null)
                    {
                        dc.DrawGeometry(accent, pen: null, geom);
                    }
                }
            }
        }
    }

    private void DrawRegionBackgrounds(DrawingContext dc, int firstVisible, int lastVisible)
    {
        foreach (var range in Regions)
        {
            if (!range.IsConflicting) continue;
            var side = GetSideRange(range);
            if (side.IsEmpty) continue;

            var firstLine0 = side.StartLine - 1;
            var lastLine0 = side.EndLineExclusive - 1 - 1; // inclusive
            if (lastLine0 < firstVisible || firstLine0 > lastVisible) continue;

            var y = firstLine0 * LineHeight - _verticalOffset;
            var h = (lastLine0 - firstLine0 + 1) * LineHeight;
            dc.DrawRectangle(HighlightBrush, pen: null, new Rect(0, y, ActualWidth, h));

            // Resolved state: overlay a translucent green tint, faded in if
            // V5's range-resolve animation is running for this range.
            if (RangeStates is not null
                && RangeStates.TryGetValue(range.Index, out var state)
                && state is not ResolutionState.Unresolved)
            {
                var alpha = ResolvedOverlayAlphaFor(range.Index);
                if (alpha >= 0.999)
                {
                    dc.DrawRectangle(ResolvedOverlayBrush, pen: null, new Rect(0, y, ActualWidth, h));
                }
                else
                {
                    // Reuse the pane's pre-allocated mutable brush instead of
                    // allocating a SolidColorBrush per frame per range. Brushes
                    // used inside a single OnRender pass don't need to be frozen
                    // so long as nothing retains a cross-thread reference.
                    var baseColor = ResolvedOverlayBrush.Color;
                    _fadedOverlayBrush.Color = Color.FromArgb(
                        (byte)(baseColor.A * alpha), baseColor.R, baseColor.G, baseColor.B);
                    dc.DrawRectangle(_fadedOverlayBrush, pen: null, new Rect(0, y, ActualWidth, h));
                }
            }
        }
    }

    private LineRange GetSideRange(ModifiedBaseRange range) => Side switch
    {
        MergePaneSide.Ours => range.Ours,
        MergePaneSide.Theirs => range.Theirs,
        MergePaneSide.Base => range.Base,
        MergePaneSide.Result => throw new InvalidOperationException(
            "ReadOnlyMergePane does not render with Side == Result; that enum value is " +
            "for secondary surfaces like StickyConflictHeader. Pane was configured wrong."),
        _ => LineRange.Empty,
    };

    // Resolved-state overlay — pre-blended 0x44-alpha green lives in the
    // palette as Merge.State.Resolved.Overlay so both the alpha and the base
    // colour track theme swaps together. No hex literals in code.
    private static readonly SolidColorBrush ResolvedOverlayBrush =
        MergePaletteResources.ResolveFrozenBrush("Merge.State.Resolved.Overlay.Color");
    private static readonly SolidColorBrush GutterBrush =
        MergePaletteResources.ResolveFrozenBrush("Merge.Text.Tertiary.Color");
    // Word-level highlight accents: drawn on top of the region background.
    // Ours side uses a stronger blue; Theirs uses a stronger green; matched
    // to the existing HighlightBrush palette per side.
    private static readonly SolidColorBrush OursWordAccent =
        MergePaletteResources.ResolveFrozenBrush("Merge.Ours.BgStrong.Color");
    private static readonly SolidColorBrush TheirsWordAccent =
        MergePaletteResources.ResolveFrozenBrush("Merge.Theirs.BgStrong.Color");

    // Change-bar brushes + pens per side. Solid for additions, 2-on / 2-off
    // dashed for the small deletion caret. The dashed pens intentionally reuse
    // the same brush so the visual language stays coherent across both states.
    private static readonly SolidColorBrush ChangeBarOursBrush =
        MergePaletteResources.ResolveFrozenBrush("Merge.Ours.Accent.Color");
    private static readonly SolidColorBrush ChangeBarTheirsBrush =
        MergePaletteResources.ResolveFrozenBrush("Merge.Theirs.Accent.Color");
    private static readonly SolidColorBrush ChangeBarBaseBrush =
        MergePaletteResources.ResolveFrozenBrush("Merge.Base.Accent.Color");
    private static readonly Pen ChangeBarOursDashedPen = MakeDashedPen(ChangeBarOursBrush);
    private static readonly Pen ChangeBarTheirsDashedPen = MakeDashedPen(ChangeBarTheirsBrush);
    private static readonly Pen ChangeBarBaseDashedPen = MakeDashedPen(ChangeBarBaseBrush);

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
    private static Pen FreezePen(Pen p) { p.Freeze(); return p; }
    // WPF DashStyle values are multiples of the pen's thickness. Pen thickness
    // is 2 px, so {1.0, 1.0} gives a 2-on / 2-off pattern. Across the 10 px
    // caret that renders as three visible ticks (2 px each) separated by two
    // 2 px gaps — a clearly dashed line, as the plan specifies.
    private static Pen MakeDashedPen(Brush b) => FreezePen(new Pen(b, ChangeBarWidth)
    {
        DashStyle = new DashStyle(new double[] { 1.0, 1.0 }, 0),
        DashCap = PenLineCap.Flat,
    });

    private void DrawGutter(DrawingContext dc, int firstVisible, int lastVisible)
    {
        if (Layout is null) return;
        for (int i = firstVisible; i <= lastVisible; i++)
        {
            var y = i * LineHeight - _verticalOffset;
            var ft = Layout.BuildFormattedText((i + 1).ToString(), GutterBrush);
            var x = GutterWidth - 6 - ft.Width;
            dc.DrawText(ft, new Point(x, y));
        }
    }

    private void DrawText(DrawingContext dc, int firstVisible, int lastVisible)
    {
        if (Layout is null) return;
        var textX = GutterWidth + 4 - _horizontalOffset;
        for (int i = firstVisible; i <= lastVisible; i++)
        {
            var y = i * LineHeight - _verticalOffset;
            var line = Lines[i];
            if (line.Length == 0) continue;
            var ft = Layout.BuildFormattedText(line, Foreground);
            ApplySyntaxHighlighting(ft, lineIndex0: i, lineText: line);
            dc.DrawText(ft, new Point(textX, y));
        }
    }

    private void ApplySyntaxHighlighting(FormattedText ft, int lineIndex0, string lineText)
    {
        if (_documentHighlighter is null || _highlightDocument is null) return;
        // HighlightLine / DocumentLine are 1-based; our Lines index is 0-based.
        var lineNumber1 = lineIndex0 + 1;
        if (lineNumber1 > _highlightDocument.LineCount) return;
        var docLine = _highlightDocument.GetLineByNumber(lineNumber1);
        var highlighted = _documentHighlighter.HighlightLine(lineNumber1);
        foreach (var section in highlighted.Sections)
        {
            var brush = section.Color?.Foreground?.GetBrush(null);
            if (brush is null) continue;
            // Section.Offset is document-wide; map to a line-local index for
            // FormattedText.SetForegroundBrush.
            var localStart = section.Offset - docLine.Offset;
            if (localStart < 0 || localStart >= lineText.Length) continue;
            var localLength = Math.Min(section.Length, lineText.Length - localStart);
            if (localLength <= 0) continue;
            ft.SetForegroundBrush(brush, localStart, localLength);
        }
    }

    // ── Layout-change propagation ────────────────────────────────────────────────

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var pane = (ReadOnlyMergePane)d;
        if (e.OldValue is MergePaneGlyphLayout oldLayout)
            oldLayout.PropertyChanged -= pane.OnLayoutPropertyChanged;
        if (e.NewValue is MergePaneGlyphLayout newLayout)
            newLayout.PropertyChanged += pane.OnLayoutPropertyChanged;
        pane.InvalidateMeasure();
        pane.InvalidateVisual();
    }

    private void OnLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    // ── IScrollInfo — plugs into a parent ScrollViewer ────────────────────────────

    public bool CanVerticallyScroll { get; set; } = true;
    public bool CanHorizontallyScroll { get; set; } = true;
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _horizontalOffset;
    public double VerticalOffset => _verticalOffset;
    public ScrollViewer? ScrollOwner
    {
        get => _scrollOwner;
        set => _scrollOwner = value;
    }

    public void SetVerticalOffset(double offset)
    {
        offset = Math.Max(0, Math.Min(offset, Math.Max(0, _extent.Height - _viewport.Height)));
        if (_verticalOffset != offset)
        {
            _verticalOffset = offset;
            _scrollOwner?.InvalidateScrollInfo();
            InvalidateVisual();
        }
    }

    public void SetHorizontalOffset(double offset)
    {
        offset = Math.Max(0, Math.Min(offset, Math.Max(0, _extent.Width - _viewport.Width)));
        if (_horizontalOffset != offset)
        {
            _horizontalOffset = offset;
            _scrollOwner?.InvalidateScrollInfo();
            InvalidateVisual();
        }
    }

    public void LineUp() => SetVerticalOffset(_verticalOffset - LineHeight);
    public void LineDown() => SetVerticalOffset(_verticalOffset + LineHeight);
    public void LineLeft() => SetHorizontalOffset(_horizontalOffset - 16);
    public void LineRight() => SetHorizontalOffset(_horizontalOffset + 16);
    public void PageUp() => SetVerticalOffset(_verticalOffset - _viewport.Height);
    public void PageDown() => SetVerticalOffset(_verticalOffset + _viewport.Height);
    public void PageLeft() => SetHorizontalOffset(_horizontalOffset - _viewport.Width);
    public void PageRight() => SetHorizontalOffset(_horizontalOffset + _viewport.Width);
    public void MouseWheelUp() => SetVerticalOffset(_verticalOffset - SystemParameters.WheelScrollLines * LineHeight);
    public void MouseWheelDown() => SetVerticalOffset(_verticalOffset + SystemParameters.WheelScrollLines * LineHeight);
    public void MouseWheelLeft() => SetHorizontalOffset(_horizontalOffset - 48);
    public void MouseWheelRight() => SetHorizontalOffset(_horizontalOffset + 48);

    public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;
}

/// <summary>
/// Which side of the merge a pane is displaying.
/// <see cref="Result"/> is only valid on secondary surfaces that track the
/// composed output (notably <see cref="StickyConflictHeader"/>) — <see cref="ReadOnlyMergePane"/>
/// itself never renders with <c>Side == Result</c>.
/// </summary>
public enum MergePaneSide
{
    Ours,
    Theirs,
    Base,
    Result,
}
