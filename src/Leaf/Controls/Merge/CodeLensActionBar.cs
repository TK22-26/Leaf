#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Leaf.Models.Merge;
using Leaf.TextEdit;

namespace Leaf.Controls.Merge;

/// <summary>
/// CodeLens-style inline action bar overlaid on top of the result pane. For
/// each conflicting <see cref="ModifiedBaseRange"/>, renders a row of
/// underlined link-buttons — <em>Accept Ours</em>, <em>Accept Theirs</em>,
/// <em>Accept Both</em>, <em>Compare</em> — positioned at the start of that
/// range's merged-result line. Click routes through the corresponding
/// <see cref="System.Windows.Input.ICommand"/> set via dependency property so
/// the host view can stay view-agnostic.
/// </summary>
/// <remarks>
/// <para>
/// The bar is a <see cref="Canvas"/> child of the grid that contains the
/// result pane — it does not modify the vendored AvalonEdit document flow,
/// which is what Phase 2b's line-mapping guarantees depend on. Positioning
/// uses the shared <see cref="MergePaneGlyphLayout"/> line height minus the
/// result pane's vertical scroll offset, matching the pattern already used by
/// <see cref="PaneConnectionCanvas"/>.
/// </para>
/// <para>
/// Resolved ranges render at 40 % opacity so the user can still see their
/// own recent resolution without the chrome drawing attention. Hover
/// tooltips surface the corresponding keybinding.
/// </para>
/// <para>
/// Intentional difference from <see cref="ReadOnlyMergePane"/>'s checkbox:
/// the checkbox routes through <c>MergeEditorView.ApplyCheckbox</c>, which
/// infers <c>AcceptBoth</c> when the user clicks one side's checkbox while
/// the other side is already accepted (toggle-style UX). CodeLens does NOT
/// route through that path — each button is a dedicated explicit state set.
/// "Accept Ours" means "set AcceptOurs" even if Theirs was previously
/// accepted, because the label reads literally. Users who want both
/// click the dedicated "Accept Both" button.
/// </para>
/// </remarks>
public sealed class CodeLensActionBar : Canvas
{
    public static readonly DependencyProperty RangesProperty = DependencyProperty.Register(
        nameof(Ranges), typeof(IReadOnlyList<ModifiedBaseRange>), typeof(CodeLensActionBar),
        new FrameworkPropertyMetadata(Array.Empty<ModifiedBaseRange>(),
            FrameworkPropertyMetadataOptions.AffectsRender, OnRangesChanged));

    public static readonly DependencyProperty RangeStatesProperty = DependencyProperty.Register(
        nameof(RangeStates), typeof(IReadOnlyDictionary<int, ResolutionState>), typeof(CodeLensActionBar),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender, OnStatesChanged));

    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(MergePaneGlyphLayout), typeof(CodeLensActionBar),
        new FrameworkPropertyMetadata(null, OnLayoutChanged));

    public static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.Register(
        nameof(VerticalOffset), typeof(double), typeof(CodeLensActionBar),
        new FrameworkPropertyMetadata(0.0, OnVerticalOffsetChanged));

    public static readonly DependencyProperty AcceptOursCommandProperty = DependencyProperty.Register(
        nameof(AcceptOursCommand), typeof(ICommand), typeof(CodeLensActionBar),
        new PropertyMetadata(null, OnCommandChanged));

    public static readonly DependencyProperty AcceptTheirsCommandProperty = DependencyProperty.Register(
        nameof(AcceptTheirsCommand), typeof(ICommand), typeof(CodeLensActionBar),
        new PropertyMetadata(null, OnCommandChanged));

    public static readonly DependencyProperty AcceptBothCommandProperty = DependencyProperty.Register(
        nameof(AcceptBothCommand), typeof(ICommand), typeof(CodeLensActionBar),
        new PropertyMetadata(null, OnCommandChanged));

    public static readonly DependencyProperty CompareCommandProperty = DependencyProperty.Register(
        nameof(CompareCommand), typeof(ICommand), typeof(CodeLensActionBar),
        new PropertyMetadata(null, OnCommandChanged));

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CodeLensActionBar)d).Rebuild();

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

    /// <summary>
    /// Vertical scroll offset of the result pane. Host wires this to the
    /// underlying scroll source; positioning math subtracts it from each
    /// range's pixel Y so the bar tracks as the pane scrolls.
    /// </summary>
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
    /// Compare command — bound to the VM's CompareConflictCommand. The VM
    /// raises CompareRequested, which the host turns into a smooth-scroll
    /// of both Ours and Theirs panes to the range's respective start line.
    /// </summary>
    public ICommand? CompareCommand
    {
        get => (ICommand?)GetValue(CompareCommandProperty);
        set => SetValue(CompareCommandProperty, value);
    }

    /// <summary>Bar height in device-independent pixels — matches one line height.</summary>
    internal const double BarHeight = 22;

    public CodeLensActionBar()
    {
        Focusable = false;
        ClipToBounds = true;
        // No Background — the canvas is a transparent overlay; the result
        // pane shows through in the gaps between bars.
    }

    private static void OnRangesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CodeLensActionBar)d).Rebuild();

    private static void OnStatesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CodeLensActionBar)d).Rebuild();

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (CodeLensActionBar)d;
        if (e.OldValue is MergePaneGlyphLayout oldLayout)
            oldLayout.PropertyChanged -= bar.OnLayoutPropChanged;
        if (e.NewValue is MergePaneGlyphLayout newLayout)
            newLayout.PropertyChanged += bar.OnLayoutPropChanged;
        bar.Reposition();
    }

    private static void OnVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CodeLensActionBar)d).Reposition();

    private void OnLayoutPropChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        Reposition();

    /// <summary>
    /// Rebuild every bar child from the current <see cref="Ranges"/>. Called
    /// when Ranges or RangeStates change; cheap because the child count
    /// equals the conflicting-range count (tens, not thousands).
    /// </summary>
    private void Rebuild()
    {
        Children.Clear();
        if (Ranges is null) return;
        foreach (var range in Ranges)
        {
            if (!range.IsConflicting) continue;
            Children.Add(BuildBarForRange(range));
        }
        Reposition();
    }

    private UIElement BuildBarForRange(ModifiedBaseRange range)
    {
        var resolved = RangeStates is not null
            && RangeStates.TryGetValue(range.Index, out var state)
            && state is not ResolutionState.Unresolved;

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Opacity = resolved ? 0.4 : 1.0,
            Tag = range.Index,
        };
        AddLink(panel, "Accept Ours", "Alt+1", range.Index, AcceptOursCommand);
        AddLink(panel, "Accept Theirs", "Alt+2", range.Index, AcceptTheirsCommand);
        AddLink(panel, "Accept Both", "Alt+3", range.Index, AcceptBothCommand);
        AddLink(panel, "Compare", "Scroll Ours + Theirs to this conflict", range.Index, CompareCommand);
        return panel;
    }

    private static void AddLink(StackPanel panel, string text, string keybind, int rangeIndex, ICommand? command)
    {
        var link = new Hyperlink(new Run(text))
        {
            ToolTip = keybind,
            Foreground = Application.Current?.TryFindResource("Merge.Ours.Accent") as Brush
                ?? Brushes.DodgerBlue,
        };
        if (command is not null)
        {
            link.Command = command;
            link.CommandParameter = rangeIndex;
        }
        var wrapper = new TextBlock(link)
        {
            Margin = panel.Children.Count == 0 ? new Thickness(0) : new Thickness(12, 0, 0, 0),
            FontSize = Application.Current?.TryFindResource("Merge.Type.Caption.Size") as double? ?? 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(wrapper);
    }

    /// <summary>
    /// Position every child at its range's top-line Y (in canvas coordinates)
    /// minus <see cref="VerticalOffset"/> and <see cref="BarHeight"/>. Called
    /// on every scroll and layout change.
    /// </summary>
    private void Reposition()
    {
        if (Layout is null || Ranges is null) return;
        var lineHeight = Layout.LineHeight;
        var offset = VerticalOffset;
        int childIdx = 0;
        foreach (var range in Ranges)
        {
            if (!range.IsConflicting) continue;
            if (childIdx >= Children.Count) break;
            var child = Children[childIdx++];
            var y = (range.ResultMarkedRange.StartLine - 1) * lineHeight - offset - BarHeight;
            SetTop(child, y);
            SetLeft(child, 4);
        }
    }
}
