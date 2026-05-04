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
    /// Paint the currently-selected cell with the accent colour; clear the
    /// others. Called whenever <see cref="State"/> changes. Uses
    /// <see cref="MergeMotionHelpers.PlayPillCellTransition"/> so each cell
    /// whose colour actually changes crossfades over 200 ms (plan §D3's
    /// post-checkbox analogue), while cells that stay the same colour are
    /// left untouched. A palette swap (V8) still propagates correctly
    /// because the colours are re-resolved per update.
    /// </summary>
    private void UpdateCellHighlighting()
    {
        // Cell-specific accents give each side a strong visual identity —
        // Ours blue, Theirs green, Both amber — so the pill reads the same
        // as the accept markers used elsewhere in the editor.
        var oursAccent = ResolveColor("Merge.Ours.BgStrong.Color");
        var theirsAccent = ResolveColor("Merge.Theirs.BgStrong.Color");
        var bothAccent = ResolveColor("Merge.State.Manual.Color");
        var clear = Colors.Transparent;

        var (oursTarget, bothTarget, theirsTarget) = State switch
        {
            ResolutionState.AcceptOurs => (oursAccent, clear, clear),
            ResolutionState.AcceptTheirs => (clear, clear, theirsAccent),
            ResolutionState.AcceptBoth => (clear, bothAccent, clear),
            // Manual / Unresolved / null — no cell selected; all transparent.
            _ => (clear, clear, clear),
        };

        TransitionCellTo(OursCell, oursTarget);
        TransitionCellTo(BothCell, bothTarget);
        TransitionCellTo(TheirsCell, theirsTarget);
    }

    /// <summary>
    /// Crossfade <paramref name="cell"/>'s Background from its current
    /// colour to <paramref name="target"/>. Reads the current colour from
    /// the installed <see cref="SolidColorBrush"/>; a non-solid Background
    /// (never set by this control) falls through as Transparent for the
    /// From value. No-op when the cell is already at <paramref name="target"/>
    /// so repeat selections don't re-tween.
    /// </summary>
    private static void TransitionCellTo(Control cell, Color target)
    {
        var from = cell.Background is SolidColorBrush scb ? scb.Color : Colors.Transparent;
        if (from == target) return;
        MergeMotionHelpers.PlayPillCellTransition(cell, from, target);
    }

    // Strict palette lookup — a missing key is a programming error, not a
    // rendering fallback. Throws with a clear pointer to the palette XAML.
    private static Color ResolveColor(string key) =>
        MergePaletteResources.ResolveColor(key);

    private void OnOursClicked(object sender, RoutedEventArgs e)
    {
        PlayBounceOn(sender);
        if (AcceptOursCommand?.CanExecute(RangeIndex) == true)
            AcceptOursCommand.Execute(RangeIndex);
    }

    private void OnBothClicked(object sender, RoutedEventArgs e)
    {
        PlayBounceOn(sender);
        if (AcceptBothCommand?.CanExecute(RangeIndex) == true)
            AcceptBothCommand.Execute(RangeIndex);
    }

    private void OnTheirsClicked(object sender, RoutedEventArgs e)
    {
        PlayBounceOn(sender);
        if (AcceptTheirsCommand?.CanExecute(RangeIndex) == true)
            AcceptTheirsCommand.Execute(RangeIndex);
    }

    /// <summary>
    /// Run the plan §D3 AcceptButton storyboard on the clicked cell so the
    /// pill gives immediate 150 ms 0.97→1.0 scale feedback. Safe to call
    /// when the cell element is unset (design-time XAML previews never
    /// route through this path).
    /// </summary>
    private static void PlayBounceOn(object clickSource)
    {
        if (clickSource is FrameworkElement fe)
        {
            MergeMotionHelpers.PlayAcceptBounce(fe);
        }
    }
}
