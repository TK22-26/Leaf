#nullable enable
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Leaf.Models.Merge;
using Leaf.Services.Merge;
using Leaf.TextEdit;

namespace Leaf.Controls.Merge;

/// <summary>
/// Read-only text pane used for the Ours / Theirs / Base sides of the merge editor.
/// Renders text directly via <see cref="FormattedText"/> through the shared
/// <see cref="MergePaneGlyphLayout"/> so it aligns pixel-perfectly with the vendored
/// Result pane. Draws its own conflict-region highlights and accept checkbox for
/// each conflicting range — no AvalonEdit margins / renderers / overlays involved.
/// </summary>
/// <remarks>
/// <para>
/// This control is intentionally not a <see cref="Leaf.TextEdit.TextEditor"/>:
/// the merge input panes don't need caret, IME, text selection editing, clipboard,
/// undo, or any editing infrastructure. A purpose-built renderer is simpler, faster,
/// and removes coordinate-translation friction for Phase 2c+ chrome (checkboxes,
/// connection lines, minimap).
/// </para>
/// <para>
/// Scrolling is handled via <see cref="IScrollInfo"/> so the control plugs into a
/// parent <see cref="System.Windows.Controls.ScrollViewer"/>; the ScrollSynchronizer
/// in Phase 2c uses the standard <c>VerticalOffset</c> to keep panes aligned.
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
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

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

    /// <summary>Conflict ranges that apply to this side; used to draw backgrounds + checkboxes.</summary>
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
    /// Raised when the user toggles the accept-this-side checkbox for a conflict.
    /// Handlers mutate <c>RangeStates</c> on the view-model and push the change
    /// through. The pane does not mutate its own <see cref="RangeStates"/> property.
    /// </summary>
    public event EventHandler<MergePaneCheckboxEventArgs>? AcceptCheckboxToggled;

    private const double GutterWidth = 48;       // line numbers
    private const double CheckboxSize = 14;      // square
    private const double CheckboxMargin = 4;     // space left of line numbers

    private ScrollViewer? _scrollOwner;
    private double _verticalOffset;
    private double _horizontalOffset;
    private Size _extent;
    private Size _viewport;

    public ReadOnlyMergePane()
    {
        Focusable = false;
        ClipToBounds = true;
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
            return GutterWidth + CheckboxSize + CheckboxMargin + maxGlyphs * Layout.AdvanceWidth + 16;
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

        // 3. Line numbers gutter.
        DrawGutter(drawingContext, firstVisible, lastVisible);

        // 4. Checkboxes (one per conflicting range that intersects the viewport).
        DrawCheckboxes(drawingContext, firstVisible, lastVisible);

        // 5. Text lines.
        DrawText(drawingContext, firstVisible, lastVisible);
    }

    private void DrawWordHighlights(DrawingContext dc, int firstVisible, int lastVisible)
    {
        if (Layout is null || WordDiffs is null) return;
        var textX = GutterWidth + CheckboxSize + CheckboxMargin + 4 - _horizontalOffset;
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

            // Resolved state: overlay a translucent green tint.
            if (RangeStates is not null
                && RangeStates.TryGetValue(range.Index, out var state)
                && state is not ResolutionState.Unresolved)
            {
                dc.DrawRectangle(ResolvedOverlayBrush, pen: null, new Rect(0, y, ActualWidth, h));
            }
        }
    }

    private LineRange GetSideRange(ModifiedBaseRange range) => Side switch
    {
        MergePaneSide.Ours => range.Ours,
        MergePaneSide.Theirs => range.Theirs,
        MergePaneSide.Base => range.Base,
        _ => LineRange.Empty,
    };

    // Resolved-state overlay — the Merge.State.Resolved colour dropped over a
    // conflict region at ~27% alpha once the user picks a side. The alpha lives
    // in code rather than in the palette because the overlay is a decoration
    // applied on top of an already-tinted background; the palette only ships
    // the solid state colour.
    private static readonly SolidColorBrush ResolvedOverlayBrush = Freeze(new SolidColorBrush(
        MergePaletteResources.WithAlpha(
            MergePaletteResources.ResolveColor("Merge.State.Resolved.Color", Color.FromRgb(0x22, 0xC5, 0x5E)),
            0x44)));
    private static readonly SolidColorBrush GutterBrush = MergePaletteResources.ResolveFrozenBrush(
        "Merge.Text.Tertiary.Color", Color.FromRgb(0x88, 0x88, 0x88));
    // Word-level highlight accents: drawn on top of the region background.
    // Ours side uses a stronger blue; Theirs uses a stronger green; matched
    // to the existing HighlightBrush palette per side.
    private static readonly SolidColorBrush OursWordAccent = MergePaletteResources.ResolveFrozenBrush(
        "Merge.Ours.BgStrong.Color", Color.FromArgb(0x99, 0x2B, 0x4A, 0x6E));
    private static readonly SolidColorBrush TheirsWordAccent = MergePaletteResources.ResolveFrozenBrush(
        "Merge.Theirs.BgStrong.Color", Color.FromArgb(0x99, 0x1A, 0x50, 0x35));
    private static readonly SolidColorBrush CheckboxFillUnchecked = Freeze(new SolidColorBrush(Colors.Transparent));
    // Checkbox stroke uses Base.Accent (neutral mid-grey) — closer to the
    // pre-V1 #A0A0A0 than Text.Tertiary (#888888, too dark) or Text.Secondary
    // (#D0D0D0, too light). Base is the right group semantically because the
    // checkbox is a neutral UI affordance, not an Ours- or Theirs-tagged one.
    private static readonly SolidColorBrush CheckboxStroke = MergePaletteResources.ResolveFrozenBrush(
        "Merge.Base.Accent.Color", Color.FromRgb(0xB0, 0xB0, 0xB0));
    private static readonly SolidColorBrush CheckboxFillChecked = MergePaletteResources.ResolveFrozenBrush(
        "Merge.State.Resolved.Color", Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly Pen CheckboxStrokePen = FreezePen(new Pen(CheckboxStroke, 1.0));

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
    private static Pen FreezePen(Pen p) { p.Freeze(); return p; }

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

    private void DrawCheckboxes(DrawingContext dc, int firstVisible, int lastVisible)
    {
        if (RangeStates is null) return;
        foreach (var range in Regions)
        {
            if (!range.IsConflicting) continue;
            var side = GetSideRange(range);
            if (side.IsEmpty) continue;
            var firstLine0 = side.StartLine - 1;
            if (firstLine0 < firstVisible || firstLine0 > lastVisible) continue;

            var y = firstLine0 * LineHeight - _verticalOffset + (LineHeight - CheckboxSize) / 2;
            var x = GutterWidth + CheckboxMargin;
            var box = new Rect(x, y, CheckboxSize, CheckboxSize);

            var isAccepted = IsThisSideAccepted(range.Index);
            dc.DrawRectangle(isAccepted ? CheckboxFillChecked : CheckboxFillUnchecked,
                CheckboxStrokePen, box);
            if (isAccepted)
            {
                // Draw a simple check glyph.
                var p1 = new Point(x + 3, y + CheckboxSize / 2);
                var p2 = new Point(x + CheckboxSize / 2, y + CheckboxSize - 3);
                var p3 = new Point(x + CheckboxSize - 2, y + 3);
                dc.DrawLine(CheckboxStrokePen, p1, p2);
                dc.DrawLine(CheckboxStrokePen, p2, p3);
            }
        }
    }

    private bool IsThisSideAccepted(int rangeIndex)
    {
        if (RangeStates is null) return false;
        if (!RangeStates.TryGetValue(rangeIndex, out var state)) return false;
        return IsAcceptedForSide(Side, state);
    }

    /// <summary>
    /// Truth table for "should this pane render its accept-checkbox as checked
    /// for the given state?". Exposed as <c>internal static</c> so tests can
    /// pin the logic without standing up a full pane + visual tree. Base side
    /// is intentionally never accepted — base has no accept-checkbox in the UI.
    /// </summary>
    internal static bool IsAcceptedForSide(MergePaneSide side, ResolutionState state) => (side, state) switch
    {
        (MergePaneSide.Ours, ResolutionState.AcceptOurs) => true,
        (MergePaneSide.Ours, ResolutionState.AcceptBoth) => true,
        (MergePaneSide.Theirs, ResolutionState.AcceptTheirs) => true,
        (MergePaneSide.Theirs, ResolutionState.AcceptBoth) => true,
        _ => false,
    };

    private void DrawText(DrawingContext dc, int firstVisible, int lastVisible)
    {
        if (Layout is null) return;
        var textX = GutterWidth + CheckboxSize + CheckboxMargin + 4 - _horizontalOffset;
        for (int i = firstVisible; i <= lastVisible; i++)
        {
            var y = i * LineHeight - _verticalOffset;
            var line = Lines[i];
            if (line.Length == 0) continue;
            var ft = Layout.BuildFormattedText(line, Foreground);
            dc.DrawText(ft, new Point(textX, y));
        }
    }

    // ── Click routing for the checkbox ────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Layout is null) return;
        var pos = e.GetPosition(this);
        if (pos.X < GutterWidth || pos.X > GutterWidth + CheckboxSize + CheckboxMargin * 2) return;
        var lineIdx0 = (int)Math.Floor((pos.Y + _verticalOffset) / LineHeight);

        foreach (var range in Regions)
        {
            if (!range.IsConflicting) continue;
            var side = GetSideRange(range);
            if (side.IsEmpty) continue;
            if (side.StartLine - 1 == lineIdx0)
            {
                var isAccepted = IsThisSideAccepted(range.Index);
                AcceptCheckboxToggled?.Invoke(this,
                    new MergePaneCheckboxEventArgs(range.Index, Side, !isAccepted));
                e.Handled = true;
                return;
            }
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

/// <summary>Which side of the merge a <see cref="ReadOnlyMergePane"/> is displaying.</summary>
public enum MergePaneSide
{
    Ours,
    Theirs,
    Base,
}

/// <summary>Event args for <see cref="ReadOnlyMergePane.AcceptCheckboxToggled"/>.</summary>
public sealed class MergePaneCheckboxEventArgs : EventArgs
{
    public MergePaneCheckboxEventArgs(int rangeIndex, MergePaneSide side, bool isAccepted)
    {
        RangeIndex = rangeIndex;
        Side = side;
        IsAccepted = isAccepted;
    }
    public int RangeIndex { get; }
    public MergePaneSide Side { get; }
    public bool IsAccepted { get; }
}
