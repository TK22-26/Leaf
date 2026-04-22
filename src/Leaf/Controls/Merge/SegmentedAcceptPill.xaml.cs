#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Leaf.Models.Merge;

namespace Leaf.Controls.Merge;

/// <summary>
/// Three-cell pill that replaces the per-side accept checkbox. Ours / Both /
/// Theirs are exclusive selections — clicking a cell invokes the corresponding
/// host-supplied <see cref="System.Windows.Input.ICommand"/> with the conflict
/// range index as the command parameter.
/// </summary>
/// <remarks>
/// <para>
/// The pre-C2 ReadOnlyMergePane checkbox was ambiguous: clicking Ours while
/// Theirs was already accepted silently set <c>AcceptBoth</c> via the host's
/// inference. The pill makes every choice explicit — a dedicated Both cell
/// keeps the toggle UX from carrying implicit state.
/// </para>
/// <para>
/// The visual reflects <see cref="State"/> via a dependency-property driven
/// Background on the currently-selected cell (accent brush from the palette).
/// Click handlers raise the command regardless of current state, so clicking
/// the already-selected cell is a no-op (WPF ICommand CanExecute handles it)
/// rather than an unresolve — Unresolve stays a separate affordance.
/// </para>
/// </remarks>
public partial class SegmentedAcceptPill : UserControl
{
    public static readonly DependencyProperty RangeIndexProperty = DependencyProperty.Register(
        nameof(RangeIndex), typeof(int), typeof(SegmentedAcceptPill),
        new PropertyMetadata(-1));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(ResolutionState), typeof(SegmentedAcceptPill),
        new PropertyMetadata(null, OnStateChanged));

    public static readonly DependencyProperty AcceptOursCommandProperty = DependencyProperty.Register(
        nameof(AcceptOursCommand), typeof(System.Windows.Input.ICommand), typeof(SegmentedAcceptPill));

    public static readonly DependencyProperty AcceptTheirsCommandProperty = DependencyProperty.Register(
        nameof(AcceptTheirsCommand), typeof(System.Windows.Input.ICommand), typeof(SegmentedAcceptPill));

    public static readonly DependencyProperty AcceptBothCommandProperty = DependencyProperty.Register(
        nameof(AcceptBothCommand), typeof(System.Windows.Input.ICommand), typeof(SegmentedAcceptPill));

    public int RangeIndex
    {
        get => (int)GetValue(RangeIndexProperty);
        set => SetValue(RangeIndexProperty, value);
    }

    public ResolutionState? State
    {
        get => (ResolutionState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public System.Windows.Input.ICommand? AcceptOursCommand
    {
        get => (System.Windows.Input.ICommand?)GetValue(AcceptOursCommandProperty);
        set => SetValue(AcceptOursCommandProperty, value);
    }

    public System.Windows.Input.ICommand? AcceptTheirsCommand
    {
        get => (System.Windows.Input.ICommand?)GetValue(AcceptTheirsCommandProperty);
        set => SetValue(AcceptTheirsCommandProperty, value);
    }

    public System.Windows.Input.ICommand? AcceptBothCommand
    {
        get => (System.Windows.Input.ICommand?)GetValue(AcceptBothCommandProperty);
        set => SetValue(AcceptBothCommandProperty, value);
    }

    public SegmentedAcceptPill()
    {
        InitializeComponent();
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SegmentedAcceptPill)d).UpdateCellHighlighting();
    }

    /// <summary>
    /// Paint the currently-selected cell with the accent brush; clear the
    /// others. Called whenever <see cref="State"/> changes. Uses direct
    /// background assignment rather than DataTriggers so a palette swap (V8)
    /// propagates through the {DynamicResource} lookup on the first UpdateCell
    /// after the swap.
    /// </summary>
    private void UpdateCellHighlighting()
    {
        // Cell-specific accents give each side a strong visual identity —
        // Ours blue, Theirs green, Both amber — so the pill reads the same as
        // the accept markers used elsewhere in the editor.
        var oursAccent = ResolveBrush("Merge.Ours.BgStrong");
        var theirsAccent = ResolveBrush("Merge.Theirs.BgStrong");
        var bothAccent = ResolveBrush("Merge.State.Manual");
        var clear = Brushes.Transparent;

        OursCell.Background = clear;
        BothCell.Background = clear;
        TheirsCell.Background = clear;

        switch (State)
        {
            case ResolutionState.AcceptOurs:
                OursCell.Background = oursAccent;
                break;
            case ResolutionState.AcceptTheirs:
                TheirsCell.Background = theirsAccent;
                break;
            case ResolutionState.AcceptBoth:
                BothCell.Background = bothAccent;
                break;
            case ResolutionState.Manual:
            case ResolutionState.Unresolved:
            case null:
                // No cell selected — pill reads as "unresolved" via plain
                // surface background on every cell.
                break;
        }
    }

    private Brush ResolveBrush(string key) =>
        (Brush?)TryFindResource(key) ?? Brushes.Transparent;

    private void OnOursClicked(object sender, RoutedEventArgs e)
    {
        if (AcceptOursCommand?.CanExecute(RangeIndex) == true)
            AcceptOursCommand.Execute(RangeIndex);
    }

    private void OnBothClicked(object sender, RoutedEventArgs e)
    {
        if (AcceptBothCommand?.CanExecute(RangeIndex) == true)
            AcceptBothCommand.Execute(RangeIndex);
    }

    private void OnTheirsClicked(object sender, RoutedEventArgs e)
    {
        if (AcceptTheirsCommand?.CanExecute(RangeIndex) == true)
            AcceptTheirsCommand.Execute(RangeIndex);
    }
}
