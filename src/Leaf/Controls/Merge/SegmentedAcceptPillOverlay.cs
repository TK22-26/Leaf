#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Leaf.Models.Merge;
using Leaf.TextEdit;

namespace Leaf.Controls.Merge;

/// <summary>
/// Canvas-overlay host that instantiates one <see cref="SegmentedAcceptPill"/>
/// per conflicting <see cref="ModifiedBaseRange"/> and positions it in the
/// right-side margin of the result pane. Each pill binds the host-supplied
/// Accept Ours / Accept Theirs / Accept Both commands with the range index
/// as the command parameter.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the pre-C2 per-side checkbox rendering in ReadOnlyMergePane. The
/// checkbox was ambiguous (click Ours while Theirs was accepted → implicit
/// AcceptBoth). The pill's three dedicated cells make every choice explicit
/// and reduces the UI to one surface per conflict instead of two (one in
/// each of the Ours / Theirs panes).
/// </para>
/// <para>
/// Positioning: <c>Canvas.Top</c> tracks <see cref="VerticalOffset"/> exactly
/// like <see cref="CodeLensActionBar"/>, with the same inset formula shifted
/// by zero (the pill anchors on the range's first line, not above it).
/// <c>Canvas.Left</c> right-aligns the pill within the overlay, subtracting
/// a fixed gutter so AvalonEdit's scroll bar stays clear.
/// </para>
/// </remarks>
public sealed class SegmentedAcceptPillOverlay : Canvas
{
    public static readonly DependencyProperty RangesProperty = DependencyProperty.Register(
        nameof(Ranges), typeof(IReadOnlyList<ModifiedBaseRange>), typeof(SegmentedAcceptPillOverlay),
        new FrameworkPropertyMetadata(Array.Empty<ModifiedBaseRange>(),
            FrameworkPropertyMetadataOptions.AffectsRender, OnRangesOrStatesChanged));

    public static readonly DependencyProperty RangeStatesProperty = DependencyProperty.Register(
        nameof(RangeStates), typeof(IReadOnlyDictionary<int, ResolutionState>), typeof(SegmentedAcceptPillOverlay),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender, OnRangesOrStatesChanged));

    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(MergePaneGlyphLayout), typeof(SegmentedAcceptPillOverlay),
        new FrameworkPropertyMetadata(null, OnLayoutChanged));

    public static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.Register(
        nameof(VerticalOffset), typeof(double), typeof(SegmentedAcceptPillOverlay),
        new FrameworkPropertyMetadata(0.0, OnVerticalOffsetChanged));

    public static readonly DependencyProperty AcceptOursCommandProperty = DependencyProperty.Register(
        nameof(AcceptOursCommand), typeof(ICommand), typeof(SegmentedAcceptPillOverlay),
        new PropertyMetadata(null, OnRangesOrStatesChanged));

    public static readonly DependencyProperty AcceptTheirsCommandProperty = DependencyProperty.Register(
        nameof(AcceptTheirsCommand), typeof(ICommand), typeof(SegmentedAcceptPillOverlay),
        new PropertyMetadata(null, OnRangesOrStatesChanged));

    public static readonly DependencyProperty AcceptBothCommandProperty = DependencyProperty.Register(
        nameof(AcceptBothCommand), typeof(ICommand), typeof(SegmentedAcceptPillOverlay),
        new PropertyMetadata(null, OnRangesOrStatesChanged));

    public IReadOnlyList<ModifiedBaseRange> Ranges
    {
        get => (IReadOnlyList<ModifiedBaseRange>)GetValue(RangesProperty);
        set => SetValue(RangesProperty, value);
    }

    public IReadOnlyDictionary<int, ResolutionState>? RangeStates
    {
        get => (IReadOnlyDictionary<int, ResolutionState>?)GetValue(RangeStatesProperty);
        set => SetValue(RangeStatesProperty, value);
    }

    public MergePaneGlyphLayout? Layout
    {
        get => (MergePaneGlyphLayout?)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public double VerticalOffset
    {
        get => (double)GetValue(VerticalOffsetProperty);
        set => SetValue(VerticalOffsetProperty, value);
    }

    public ICommand? AcceptOursCommand
    {
        get => (ICommand?)GetValue(AcceptOursCommandProperty);
        set => SetValue(AcceptOursCommandProperty, value);
    }

    public ICommand? AcceptTheirsCommand
    {
        get => (ICommand?)GetValue(AcceptTheirsCommandProperty);
        set => SetValue(AcceptTheirsCommandProperty, value);
    }

    public ICommand? AcceptBothCommand
    {
        get => (ICommand?)GetValue(AcceptBothCommandProperty);
        set => SetValue(AcceptBothCommandProperty, value);
    }

    /// <summary>
    /// Width reserved for the scrollbar; pills right-align with this as the
    /// inset from the canvas's right edge. AvalonEdit's default vertical
    /// scrollbar is ~17 px wide plus a couple pixels of padding.
    /// </summary>
    internal const double ScrollBarInset = 20;

    public SegmentedAcceptPillOverlay()
    {
        Focusable = false;
        ClipToBounds = true;
        SizeChanged += (_, _) => Reposition();
    }

    private static void OnRangesOrStatesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SegmentedAcceptPillOverlay)d).Rebuild();

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var overlay = (SegmentedAcceptPillOverlay)d;
        if (e.OldValue is MergePaneGlyphLayout oldLayout)
            oldLayout.PropertyChanged -= overlay.OnLayoutPropChanged;
        if (e.NewValue is MergePaneGlyphLayout newLayout)
            newLayout.PropertyChanged += overlay.OnLayoutPropChanged;
        overlay.Reposition();
    }

    private static void OnVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SegmentedAcceptPillOverlay)d).Reposition();

    private void OnLayoutPropChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        Reposition();

    // Cached pill width — pills are a fixed three-cell layout with palette-
    // driven fonts, so DesiredSize.Width doesn't change between frames.
    // Measure once on Rebuild, reuse on every Reposition (triggered on
    // every scroll tick). Avoids O(N conflicts × scroll events) Measure
    // cost for large conflict lists.
    private double _cachedPillWidth;

    private void Rebuild()
    {
        Children.Clear();
        _cachedPillWidth = 0;
        if (Ranges is null) return;
        foreach (var range in Ranges)
        {
            if (!range.IsConflicting) continue;
            var pill = new SegmentedAcceptPill
            {
                RangeIndex = range.Index,
                AcceptOursCommand = AcceptOursCommand,
                AcceptTheirsCommand = AcceptTheirsCommand,
                AcceptBothCommand = AcceptBothCommand,
            };
            if (RangeStates is not null && RangeStates.TryGetValue(range.Index, out var state))
            {
                pill.State = state;
            }
            Children.Add(pill);
        }
        if (Children.Count > 0)
        {
            var first = (SegmentedAcceptPill)Children[0];
            first.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _cachedPillWidth = first.DesiredSize.Width;
        }
        Reposition();
    }

    /// <summary>
    /// Re-read each pill's <see cref="SegmentedAcceptPill.State"/> from the
    /// current <see cref="RangeStates"/> dictionary. Host calls this on
    /// <c>RangeStatesChanged</c> — the DP system treats dictionary mutation
    /// in place as non-change so we can't rely on
    /// <see cref="OnRangesOrStatesChanged"/> firing.
    /// </summary>
    public void RefreshPillStates()
    {
        if (Ranges is null) return;
        int childIdx = 0;
        foreach (var range in Ranges)
        {
            if (!range.IsConflicting) continue;
            if (childIdx >= Children.Count) break;
            var pill = (SegmentedAcceptPill)Children[childIdx++];
            pill.State = RangeStates is not null
                && RangeStates.TryGetValue(range.Index, out var state)
                ? state
                : null;
        }
    }

    private void Reposition()
    {
        if (Layout is null || Ranges is null) return;
        var lineHeight = Layout.LineHeight;
        var offset = VerticalOffset;
        var rightEdge = Math.Max(0, ActualWidth - ScrollBarInset);
        var left = Math.Max(0, rightEdge - _cachedPillWidth);
        int childIdx = 0;
        foreach (var range in Ranges)
        {
            if (!range.IsConflicting) continue;
            if (childIdx >= Children.Count) break;
            var pill = (SegmentedAcceptPill)Children[childIdx++];
            var y = (range.ResultMarkedRange.StartLine - 1) * lineHeight - offset;
            SetTop(pill, y);
            SetLeft(pill, left);
        }
    }
}
