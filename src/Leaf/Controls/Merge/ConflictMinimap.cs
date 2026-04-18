#nullable enable
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Leaf.Models.Merge;
using Leaf.TextEdit;

namespace Leaf.Controls.Merge;

/// <summary>
/// Narrow vertical heat-strip rendered alongside a <see cref="ReadOnlyMergePane"/>,
/// summarising the file's conflict landscape at a glance. Each row represents one
/// line of the pane's content; colour indicates the resolution state of any
/// conflict that intersects that line.
/// </summary>
/// <remarks>
/// <para>
/// Colour legend:
/// <list type="bullet">
/// <item><description>Grey — unchanged or non-conflict content</description></item>
/// <item><description>Side tint (blue = ours, green = theirs) — lines inside a
///   conflict region on this side that hasn't been resolved yet</description></item>
/// <item><description>Amber — AI-proposed resolution pending review (reserved
///   for Phase 5)</description></item>
/// <item><description>Red — unresolved conflict</description></item>
/// <item><description>Bright green — resolved conflict</description></item>
/// </list>
/// </para>
/// <para>
/// Click to jump: sets the paired ScrollViewer's <c>VerticalOffset</c> so the
/// clicked line scrolls into view in the companion pane. Drag-to-scroll is
/// implemented by continuing to treat pointer moves while the left button is
/// held as additional click events.
/// </para>
/// </remarks>
public sealed class ConflictMinimap : FrameworkElement
{
    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(MergePaneGlyphLayout), typeof(ConflictMinimap),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineCountProperty = DependencyProperty.Register(
        nameof(LineCount), typeof(int), typeof(ConflictMinimap),
        new FrameworkPropertyMetadata(0,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RegionsProperty = DependencyProperty.Register(
        nameof(Regions), typeof(IReadOnlyList<ModifiedBaseRange>), typeof(ConflictMinimap),
        new FrameworkPropertyMetadata(Array.Empty<ModifiedBaseRange>(),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RangeStatesProperty = DependencyProperty.Register(
        nameof(RangeStates), typeof(IReadOnlyDictionary<int, ResolutionState>), typeof(ConflictMinimap),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SideProperty = DependencyProperty.Register(
        nameof(Side), typeof(MergePaneSide), typeof(ConflictMinimap),
        new FrameworkPropertyMetadata(MergePaneSide.Ours, FrameworkPropertyMetadataOptions.AffectsRender));

    public MergePaneGlyphLayout? Layout
    {
        get => (MergePaneGlyphLayout?)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public int LineCount
    {
        get => (int)GetValue(LineCountProperty);
        set => SetValue(LineCountProperty, value);
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

    /// <summary>
    /// Fires when the user clicks or drags on the minimap. <see cref="MinimapJumpEventArgs.LineNumber"/>
    /// is the 1-based line the pointer addressed; the consumer scrolls the paired pane.
    /// </summary>
    public event EventHandler<MinimapJumpEventArgs>? JumpRequested;

    // Palette-derived minimap swatches. The minimap reads the base side and
    // state colours from the central palette and applies per-swatch alpha so
    // stacked tints remain legible against the unchanged-grey backdrop.
    private static readonly Brush UnchangedBrush = Freeze(new SolidColorBrush(MergePaletteResources.WithAlpha(
        MergePaletteResources.ResolveColor("Merge.Text.Tertiary.Color"), 0x22)));
    private static readonly Brush OursBrush = Freeze(new SolidColorBrush(MergePaletteResources.WithAlpha(
        MergePaletteResources.ResolveColor("Merge.Ours.Border.Color"), 0xAA)));
    private static readonly Brush TheirsBrush = Freeze(new SolidColorBrush(MergePaletteResources.WithAlpha(
        MergePaletteResources.ResolveColor("Merge.Theirs.Border.Color"), 0xAA)));
    private static readonly Brush UnresolvedBrush = Freeze(new SolidColorBrush(MergePaletteResources.WithAlpha(
        MergePaletteResources.ResolveColor("Merge.State.Unresolved.Color"), 0xDD)));
    private static readonly Brush ResolvedBrush = Freeze(new SolidColorBrush(MergePaletteResources.WithAlpha(
        MergePaletteResources.ResolveColor("Merge.State.Resolved.Color"), 0xDD)));

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    public ConflictMinimap()
    {
        ClipToBounds = true;
        Focusable = false;
        Width = 12;
        Cursor = Cursors.Hand;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var h = double.IsPositiveInfinity(availableSize.Height) ? 0 : availableSize.Height;
        return new Size(Width, h);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (LineCount <= 0 || ActualHeight <= 0) return;

        // Base fill: unchanged grey.
        dc.DrawRectangle(UnchangedBrush, pen: null, new Rect(0, 0, ActualWidth, ActualHeight));

        // One pixel row per document line, scaled to the available height.
        var rowHeight = Math.Max(1.0, ActualHeight / LineCount);

        foreach (var range in Regions)
        {
            if (!range.IsConflicting) continue;
            var sideRange = Side switch
            {
                MergePaneSide.Ours => range.Ours,
                MergePaneSide.Theirs => range.Theirs,
                MergePaneSide.Base => range.Base,
                _ => LineRange.Empty,
            };
            if (sideRange.IsEmpty) continue;

            var y0 = (sideRange.StartLine - 1) * rowHeight;
            var y1 = (sideRange.EndLineExclusive - 1) * rowHeight;
            var h = Math.Max(1.0, y1 - y0);

            var resolved = RangeStates is not null
                && RangeStates.TryGetValue(range.Index, out var state)
                && state is not ResolutionState.Unresolved;

            var brush = resolved
                ? ResolvedBrush
                : (Side == MergePaneSide.Ours ? OursBrush : TheirsBrush);
            dc.DrawRectangle(brush, pen: null, new Rect(0, y0, ActualWidth, h));

            // Add a small red top-marker for unresolved ranges so they stand out
            // at a glance even against a dense resolved-but-theirs tint.
            if (!resolved)
            {
                dc.DrawRectangle(UnresolvedBrush, pen: null, new Rect(0, y0, ActualWidth, Math.Min(2.0, h)));
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
        if (LineCount <= 0 || ActualHeight <= 0) return;
        var line = PointerYToLine(pos.Y, ActualHeight, LineCount);
        JumpRequested?.Invoke(this, new MinimapJumpEventArgs(line));
    }

    /// <summary>
    /// Map a pointer Y-coordinate to a 1-based line number. Uses the same
    /// row-height math the renderer uses so a click on a visible marker
    /// lands on the line that marker represents. In the dense case
    /// (<paramref name="actualHeight"/> &lt; <paramref name="lineCount"/>)
    /// row height pins to 1 px, matching the renderer. Exposed as
    /// <c>internal</c> for unit testing — pure function, no WPF deps.
    /// </summary>
    internal static int PointerYToLine(double y, double actualHeight, int lineCount)
    {
        if (lineCount <= 0 || actualHeight <= 0) return 1;
        var rowHeight = Math.Max(1.0, actualHeight / lineCount);
        var clamped = Math.Max(0, y);
        return (int)Math.Clamp(Math.Floor(clamped / rowHeight) + 1, 1, lineCount);
    }
}

public sealed class MinimapJumpEventArgs : EventArgs
{
    public MinimapJumpEventArgs(int lineNumber) { LineNumber = lineNumber; }
    public int LineNumber { get; }
}
