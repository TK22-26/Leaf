#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Leaf.Models.Merge;
using Leaf.TextEdit;

namespace Leaf.Controls.Merge;

file static class StickyConflictHeaderBrushes
{
    // Cached frozen brushes keep OnRender off the resource-dictionary hot path
    // and give WPF a cross-thread-safe reference for compositor re-use. Tokens
    // resolve strictly (Resolve<T> throws on missing key) — the sticky header
    // will not silently fall back to Transparent / Gray if a palette drifts.
    public static readonly SolidColorBrush Background =
        MergePaletteResources.ResolveFrozenBrush("Merge.Surface.3.Color");

    public static readonly SolidColorBrush Foreground =
        MergePaletteResources.ResolveFrozenBrush("Merge.Text.Secondary.Color");

    public static readonly FontFamily ChromeFont =
        MergePaletteResources.Resolve<FontFamily>("Merge.FontFamily.Chrome");
}

/// <summary>
/// Sticky strip shown at the top of a merge pane's viewport surfacing
/// "Conflict N of M · &lt;state&gt;" for whichever conflicting range the user
/// has currently scrolled into. Matches VS Code's sticky-scroll / Sublime
/// Merge's conflict-header affordance — the label stays visible so the user
/// always knows which conflict the edits around it belong to, even after
/// scrolling past the conflict's opening line.
/// </summary>
/// <remarks>
/// <para>
/// Self-rendered (no XAML) because the content is a single TextBlock whose
/// string is derived from <see cref="Ranges"/>, <see cref="RangeStates"/>,
/// <see cref="Layout"/>, <see cref="VerticalOffset"/>, and <see cref="Side"/>.
/// Hosting as a plain <see cref="TextBlock"/> with a multi-parameter
/// converter would fight the standard WPF binding pipeline; keeping the
/// derivation in code-behind is clearer and easier to unit-test.
/// </para>
/// <para>
/// Placement: the pane wraps its scroll viewer in a Grid with this control
/// at <c>Grid.Row="0" Height="Auto"</c> above the scroll viewer. The strip
/// is only visible when a conflict is in range of the top of the viewport.
/// </para>
/// </remarks>
public sealed class StickyConflictHeader : Control
{
    public static readonly DependencyProperty RangesProperty = DependencyProperty.Register(
        nameof(Ranges), typeof(IReadOnlyList<ModifiedBaseRange>), typeof(StickyConflictHeader),
        new FrameworkPropertyMetadata(Array.Empty<ModifiedBaseRange>(),
            FrameworkPropertyMetadataOptions.AffectsRender, OnInputChanged));

    public static readonly DependencyProperty RangeStatesProperty = DependencyProperty.Register(
        nameof(RangeStates), typeof(IReadOnlyDictionary<int, ResolutionState>), typeof(StickyConflictHeader),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender, OnInputChanged));

    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(MergePaneGlyphLayout), typeof(StickyConflictHeader),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender, OnInputChanged));

    public static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.Register(
        nameof(VerticalOffset), typeof(double), typeof(StickyConflictHeader),
        new FrameworkPropertyMetadata(0.0,
            FrameworkPropertyMetadataOptions.AffectsRender, OnInputChanged));

    public static readonly DependencyProperty SideProperty = DependencyProperty.Register(
        nameof(Side), typeof(MergePaneSide), typeof(StickyConflictHeader),
        new FrameworkPropertyMetadata(MergePaneSide.Ours,
            FrameworkPropertyMetadataOptions.AffectsRender, OnInputChanged));

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

    public MergePaneSide Side
    {
        get => (MergePaneSide)GetValue(SideProperty);
        set => SetValue(SideProperty, value);
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var header = (StickyConflictHeader)d;
        header._currentLabel = header.ComputeLabel();
        header.Visibility = header._currentLabel is null ? Visibility.Collapsed : Visibility.Visible;
        header.InvalidateVisual();
    }

    private string? _currentLabel;

    public StickyConflictHeader()
    {
        Focusable = false;
        Height = 22;
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed; // hidden until a conflict comes into view
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_currentLabel is null) return;

        dc.DrawRectangle(StickyConflictHeaderBrushes.Background, pen: null,
            new Rect(0, 0, ActualWidth, ActualHeight));

        var text = new FormattedText(
            _currentLabel,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(StickyConflictHeaderBrushes.ChromeFont,
                FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            MergePaletteResources.Resolve<double>("Merge.Type.Caption.Size"),
            StickyConflictHeaderBrushes.Foreground,
            numberSubstitution: null,
            System.Windows.Media.TextFormattingMode.Ideal,
            pixelsPerDip: VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(12, (ActualHeight - text.Height) / 2));
    }

    /// <summary>
    /// Compute "Conflict N of M · state" for whichever conflicting range the
    /// user has scrolled to or past the top of. Null = no conflicting range
    /// in view → header hidden. Exposed as <c>internal</c> so tests can
    /// drive the derivation with synthetic inputs without standing up a
    /// visual tree.
    /// </summary>
    internal string? ComputeLabel()
    {
        if (Ranges is null || Layout is null) return null;
        // Inline filter (not MergeDocument.ConflictingRanges) because this
        // control binds to a lower-level IReadOnlyList<ModifiedBaseRange> DP
        // rather than the document itself — MergeDocument isn't in scope here.
        // The predicate is pinned by StickyConflictHeaderTests so drift from
        // the document-level helper would surface immediately.
        var conflicting = Ranges.Where(r => r.IsConflicting).ToList();
        if (conflicting.Count == 0) return null;

        var lineHeight = Layout.LineHeight;
        // Walk the conflict list and pick the last range whose visual top is
        // at or above the viewport top — that's "where the user is right now"
        // even if they've scrolled past the opening line.
        int currentIdx = -1;
        for (int i = 0; i < conflicting.Count; i++)
        {
            var sideRange = GetSideRange(conflicting[i]);
            if (sideRange.IsEmpty) continue;
            var topY = (sideRange.StartLine - 1) * lineHeight;
            if (topY <= VerticalOffset) currentIdx = i;
            else break;
        }
        if (currentIdx < 0) return null;

        var range = conflicting[currentIdx];
        var stateLabel = DescribeState(range);
        return $"Conflict {currentIdx + 1} of {conflicting.Count} · {stateLabel}";
    }

    private LineRange GetSideRange(ModifiedBaseRange range) => Side switch
    {
        MergePaneSide.Ours => range.Ours,
        MergePaneSide.Theirs => range.Theirs,
        MergePaneSide.Base => range.Base,
        MergePaneSide.Result => range.ResultMarkedRange,
        _ => LineRange.Empty,
    };

    private string DescribeState(ModifiedBaseRange range)
    {
        if (RangeStates is null || !RangeStates.TryGetValue(range.Index, out var state))
        {
            return "Unresolved";
        }
        // Sentence-case after the middle-dot separator so labels read
        // uniformly: "Conflict 1 of 2 · Ours accepted", "· Manually resolved".
        return state switch
        {
            ResolutionState.AcceptOurs => "Ours accepted",
            ResolutionState.AcceptTheirs => "Theirs accepted",
            ResolutionState.AcceptBoth => "Both accepted",
            ResolutionState.Manual => "Manually resolved",
            ResolutionState.Unresolved => "Unresolved",
            _ => "Unresolved",
        };
    }
}
