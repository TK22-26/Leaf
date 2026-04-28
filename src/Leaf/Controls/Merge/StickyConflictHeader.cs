#nullable enable
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentIcons.Common;
using FluentIcons.Wpf;
using Leaf.Models.Merge;
using Leaf.TextEdit;

namespace Leaf.Controls.Merge;

file static class StickyConflictHeaderBrushes
{
    // Cached frozen brushes keep the layout pass off the resource-dictionary
    // hot path and give WPF a cross-thread-safe reference for compositor
    // re-use. Tokens resolve strictly (Resolve<T> throws on missing key) —
    // the sticky header will not silently fall back to Transparent / Gray
    // if a palette drifts.
    public static readonly SolidColorBrush Background =
        MergePaletteResources.ResolveFrozenBrush("Merge.Surface.4.Color");

    public static readonly SolidColorBrush Foreground =
        MergePaletteResources.ResolveFrozenBrush("Merge.Text.Primary.Color");

    public static readonly SolidColorBrush HoverSurface =
        MergePaletteResources.ResolveFrozenBrush("Merge.Surface.5.Color");

    public static readonly SolidColorBrush BorderBrush =
        MergePaletteResources.ResolveFrozenBrush("Merge.Border.Subtle.Color");

    public static readonly FontFamily ChromeFont =
        MergePaletteResources.Resolve<FontFamily>("Merge.FontFamily.Chrome");

    public static readonly double LabelSize =
        MergePaletteResources.Resolve<double>("Merge.Type.BodyStrong.Size");
}

/// <summary>
/// Sticky strip shown at the top of a merge pane's viewport. Surfaces
/// "Conflict N of M · &lt;state&gt;" for whichever conflicting range the user
/// has currently scrolled into and provides clickable chevrons to step
/// to the previous / next conflict — matching VS Code's sticky-scroll +
/// inline navigation pattern, JetBrains' diff-toolbar chevrons, and
/// GitKraken's floating-panel arrows.
/// </summary>
/// <remarks>
/// <para>
/// Layout: <c>[‹ button] [conflict-counter TextBlock] [› button]</c>. The
/// strip is hit-test enabled so chevrons receive clicks; the label region
/// is passive (IsHitTestVisible=false on the TextBlock) so a click in the
/// dead space falls through to the underlying pane. Buttons bind to the
/// <see cref="PreviousCommand"/> / <see cref="NextCommand"/> DPs which the
/// host wires to the VM's existing F8 / Shift+F8 commands.
/// </para>
/// <para>
/// Visibility: hidden until <see cref="ComputeLabel"/> resolves a label,
/// which only happens once a conflicting range has scrolled to or above
/// the viewport top. Without this, the strip would render an empty bar
/// at the top of every pane.
/// </para>
/// </remarks>
public sealed class StickyConflictHeader : Border
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
            FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutChanged));

    public static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.Register(
        nameof(VerticalOffset), typeof(double), typeof(StickyConflictHeader),
        new FrameworkPropertyMetadata(0.0,
            FrameworkPropertyMetadataOptions.AffectsRender, OnInputChanged));

    public static readonly DependencyProperty SideProperty = DependencyProperty.Register(
        nameof(Side), typeof(MergePaneSide), typeof(StickyConflictHeader),
        new FrameworkPropertyMetadata(MergePaneSide.Ours,
            FrameworkPropertyMetadataOptions.AffectsRender, OnInputChanged));

    /// <summary>VM command invoked when the prev-conflict chevron is clicked.</summary>
    public static readonly DependencyProperty PreviousCommandProperty = DependencyProperty.Register(
        nameof(PreviousCommand), typeof(ICommand), typeof(StickyConflictHeader));

    /// <summary>VM command invoked when the next-conflict chevron is clicked.</summary>
    public static readonly DependencyProperty NextCommandProperty = DependencyProperty.Register(
        nameof(NextCommand), typeof(ICommand), typeof(StickyConflictHeader));

    /// <summary>
    /// Two-way mirror of the VM's <c>CurrentConflictIndex</c>. The chevron
    /// click handlers write the visually-current conflict (computed from
    /// <see cref="VerticalOffset"/> the same way <see cref="ComputeLabel"/>
    /// does) into this DP before invoking <see cref="NextCommand"/> /
    /// <see cref="PreviousCommand"/>. Without this sync, manual scrolling
    /// (mouse wheel, scrollbar, minimap click) leaves the VM's index
    /// pointing at wherever the last Next/Previous click landed — so
    /// clicking Next at "Conflict 6" (per the sticky strip) when the VM
    /// internally has CurrentConflictIndex==6 from a prior click would
    /// advance to conflict 8, perceived as "skipped 7".
    /// </summary>
    public static readonly DependencyProperty CurrentIndexProperty = DependencyProperty.Register(
        nameof(CurrentIndex), typeof(int), typeof(StickyConflictHeader),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnInputChanged));

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

    public ICommand? PreviousCommand
    {
        get => (ICommand?)GetValue(PreviousCommandProperty);
        set => SetValue(PreviousCommandProperty, value);
    }

    public ICommand? NextCommand
    {
        get => (ICommand?)GetValue(NextCommandProperty);
        set => SetValue(NextCommandProperty, value);
    }

    public int CurrentIndex
    {
        get => (int)GetValue(CurrentIndexProperty);
        set => SetValue(CurrentIndexProperty, value);
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var header = (StickyConflictHeader)d;
        header.RecomputeLabel();
        // The Side DP also routes through here. Repainting the accent bar
        // is cheap (one brush assignment) so unconditionally re-tint —
        // simpler than a separate OnSideChanged callback.
        header.UpdateAccent();
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Subscribe to the Layout's own PropertyChanged so an in-place
        // LineHeight tweak (font-size change without a Layout reference swap)
        // re-runs ComputeLabel. AffectsRender on the DP alone only fires on
        // reference change — without this, a font-size slider that rebuilds
        // glyph metrics on the existing Layout instance would leave the
        // sticky strip pointing at the wrong conflict until the next scroll.
        var header = (StickyConflictHeader)d;
        if (e.OldValue is MergePaneGlyphLayout oldLayout)
            oldLayout.PropertyChanged -= header.OnLayoutPropChanged;
        if (e.NewValue is MergePaneGlyphLayout newLayout)
            newLayout.PropertyChanged += header.OnLayoutPropChanged;
        header.RecomputeLabel();
    }

    private void OnLayoutPropChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        RecomputeLabel();

    /// <summary>
    /// Explicit resync entry point for hosts that mutate
    /// <see cref="RangeStates"/> in place (the live dictionary pattern
    /// <see cref="MergeEditorViewModel"/> uses). Because the DP reference
    /// stays the same across those mutations, the DP metadata callback
    /// never fires and the cached <c>"· &lt;state&gt;"</c> caption goes stale.
    /// Mirrors <see cref="SegmentedAcceptPillOverlay.RefreshPillStates"/> —
    /// the view calls both from its <c>RangeStatesChanged</c> hook.
    /// </summary>
    public void RefreshState() => RecomputeLabel();

    private void RecomputeLabel()
    {
        var label = ComputeLabel();
        _label.Text = label ?? string.Empty;
        Visibility = label is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private readonly TextBlock _label;

    public StickyConflictHeader()
    {
        Focusable = false;
        Height = 30;  // bumped from 22 so the strip reads as deliberate chrome
                      // rather than a thin line — VS Code uses a similar weight.
        Visibility = Visibility.Collapsed; // hidden until a conflict comes into view
        Background = StickyConflictHeaderBrushes.Background;
        BorderBrush = StickyConflictHeaderBrushes.BorderBrush;
        BorderThickness = new Thickness(0, 0, 0, 1);  // bottom hairline only

        var grid = new Grid();
        // Side-accent bar (4 px) + prev chevron + flexible label cell + next chevron.
        // The accent bar's color flips per-side (Ours blue / Theirs green / Result
        // primary) so the user can tell at a glance which pane's strip they're
        // looking at, even if scrolled to the same Y in two different panes.
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _accentBar = new Border();
        Grid.SetColumn(_accentBar, 0);
        // Color set in OnInputChanged via UpdateAccent so a Side DP change
        // re-tints the bar.

        var prevBtn = BuildChevronButton(Symbol.ChevronLeft, "Previous conflict (Shift+F8)",
            "Merge.Sticky.PrevConflict", isPrevious: true);
        Grid.SetColumn(prevBtn, 1);

        _label = new TextBlock
        {
            FontFamily = StickyConflictHeaderBrushes.ChromeFont,
            FontSize = StickyConflictHeaderBrushes.LabelSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = StickyConflictHeaderBrushes.Foreground,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,  // centered now, was Left
            Margin = new Thickness(8, 0, 8, 0),
            IsHitTestVisible = false,  // clicks in the label region fall through
        };
        Grid.SetColumn(_label, 2);

        var nextBtn = BuildChevronButton(Symbol.ChevronRight, "Next conflict (F8)",
            "Merge.Sticky.NextConflict", isPrevious: false);
        Grid.SetColumn(nextBtn, 3);

        grid.Children.Add(_accentBar);
        grid.Children.Add(prevBtn);
        grid.Children.Add(_label);
        grid.Children.Add(nextBtn);
        Child = grid;

        UpdateAccent();

        // Detach from the bound Layout when this header leaves the visual
        // tree. MergePaneGlyphLayout instances are reused across
        // pane re-binds — without an Unloaded teardown the header keeps a
        // strong reference back from Layout.PropertyChanged, leaking one
        // header per merge-editor open over a long Leaf session.
        Unloaded += OnHeaderUnloaded;
    }

    private void OnHeaderUnloaded(object sender, RoutedEventArgs e)
    {
        if (Layout is { } layout)
        {
            layout.PropertyChanged -= OnLayoutPropChanged;
        }
    }

    private readonly Border _accentBar;

    /// <summary>
    /// Paint the 4 px side-accent bar with the side's signature colour
    /// (Ours blue / Theirs green / Base grey / Result primary). Called
    /// from the Side DP change handler so a re-binding flips the
    /// indicator immediately. No-op if the palette token is missing —
    /// guarded by a <c>Resolve</c> try so a future palette without one
    /// of these tokens degrades to "no accent" rather than crashing
    /// the merge editor's first render.
    /// </summary>
    private void UpdateAccent()
    {
        // Side-accent bar carries the SIDE's signature colour. The Result
        // strip is intentionally a neutral hairline (Border.Strong) — Result
        // represents the combination of ours+theirs and shouldn't visually
        // claim a side, and using a green here read as "resolved" semantics
        // even when the conflict is still unresolved.
        var key = Side switch
        {
            MergePaneSide.Ours => "Merge.Ours.Accent.Color",
            MergePaneSide.Theirs => "Merge.Theirs.Accent.Color",
            MergePaneSide.Base => "Merge.Base.Accent.Color",
            _ => "Merge.Border.Strong.Color",  // Result + any future side
        };
        try
        {
            _accentBar.Background = MergePaletteResources.ResolveFrozenBrush(key);
        }
        catch
        {
            _accentBar.Background = StickyConflictHeaderBrushes.BorderBrush;
        }
    }

    private Button BuildChevronButton(Symbol symbol, string tooltip, string automationId, bool isPrevious)
    {
        var icon = new SymbolIcon
        {
            Symbol = symbol,
            FontSize = 14,
            Foreground = StickyConflictHeaderBrushes.Foreground,
        };
        var btn = new Button
        {
            Content = icon,
            ToolTip = tooltip,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 0, 8, 0),
            Cursor = Cursors.Hand,
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(btn, automationId);
        AutomationProperties.SetName(btn, tooltip);
        // Hover tint: re-style on MouseEnter / MouseLeave rather than build
        // a full ControlTemplate. Keeps the file XAML-free and the tint
        // stays in lockstep with palette tokens.
        btn.MouseEnter += (_, _) => btn.Background = StickyConflictHeaderBrushes.HoverSurface;
        btn.MouseLeave += (_, _) => btn.Background = Brushes.Transparent;
        btn.Click += (_, _) =>
        {
            // Trust the VM's CurrentConflictIndex as-is and advance from
            // there. Earlier versions resynced CurrentIndex to the visually-
            // current conflict before invoking the command, but that broke
            // mid-scroll: rapid clicks during the smooth-scroll animation
            // computed an intermediate visual index and snapped the VM back,
            // making the second click a no-op (it scrolled the result pane
            // backwards to the prior conflict instead of advancing). Manual-
            // scroll-then-click drift is acceptable — Next still moves to the
            // next position, which is what the keyboard F8 path does.
            var cmd = isPrevious ? PreviousCommand : NextCommand;
            if (cmd?.CanExecute(null) == true) cmd.Execute(null);
        };
        return btn;
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
        if (Ranges is null) return null;
        var conflicting = Ranges.Where(r => r.IsConflicting).ToList();
        if (conflicting.Count == 0) return null;

        // Resolve which conflict to label. Prefer the VM's tracked
        // CurrentConflictIndex (set by Next / Previous navigation —
        // either keyboard F8 / Shift+F8 or the chevron buttons here)
        // because the label MUST update on click. Earlier the label
        // was driven purely by VerticalOffset, which made the chevron
        // feel broken: clicking Next advanced the VM and scrolled the
        // panes, but the visually-current conflict (whichever one had
        // just scrolled past the top of the viewport) frequently
        // stayed pointing at the prior conflict — so the user saw the
        // label remain "Conflict 1 of N" even though they had in fact
        // moved.
        // Visual override: if the user manually scrolled FORWARD past
        // the VM's tracked conflict (e.g. wheel-scrolled past three
        // conflicts) the label catches up so it doesn't lag behind
        // their position. Backward scrolls don't override — the label
        // sticks at the most-recently-navigated conflict, which is
        // what most editors do.
        var visualIdx = Layout is null ? -1 : ComputeCurrentVisibleIndex(conflicting);
        int idx;
        if (CurrentIndex >= 0 && CurrentIndex < conflicting.Count)
        {
            idx = visualIdx > CurrentIndex ? visualIdx : CurrentIndex;
        }
        else if (visualIdx >= 0)
        {
            idx = visualIdx;
        }
        else
        {
            return null;
        }

        var range = conflicting[idx];
        var stateLabel = DescribeState(range);
        return $"Conflict {idx + 1} of {conflicting.Count} · {stateLabel}";
    }

    /// <summary>
    /// Compute which conflict (0-based index into the filtered conflicting
    /// list) is currently at or above the viewport top — i.e. "the conflict
    /// the user is looking at right now". Shared by <see cref="ComputeLabel"/>
    /// (for the visible label) and the chevron click handlers (for syncing
    /// <see cref="CurrentIndex"/> before navigation). Returns -1 when the
    /// viewport is above the first conflict.
    /// </summary>
    internal int ComputeCurrentVisibleIndex() =>
        ComputeCurrentVisibleIndex(Ranges?.Where(r => r.IsConflicting).ToList());

    private int ComputeCurrentVisibleIndex(List<ModifiedBaseRange>? conflicting)
    {
        if (conflicting is null || conflicting.Count == 0 || Layout is null) return -1;
        var lineHeight = Layout.LineHeight;
        int currentIdx = -1;
        for (int i = 0; i < conflicting.Count; i++)
        {
            var sideRange = GetSideRange(conflicting[i]);
            if (sideRange.IsEmpty) continue;
            var topY = (sideRange.StartLine - 1) * lineHeight;
            if (topY <= VerticalOffset) currentIdx = i;
            else break;
        }
        return currentIdx;
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
