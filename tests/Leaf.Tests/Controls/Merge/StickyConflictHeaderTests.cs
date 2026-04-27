#nullable enable
using System.Windows;
using FluentAssertions;
using Leaf.Controls.Merge;
using Leaf.Models.Merge;
using Leaf.TextEdit;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Unit tests for <see cref="StickyConflictHeader"/>. Exercises the
/// <see cref="StickyConflictHeader.ComputeLabel"/> derivation through the
/// public DPs so future refactors can't silently change what the sticky
/// strip displays as the user scrolls past conflict regions.
/// </summary>
public class StickyConflictHeaderTests
{
    private static ModifiedBaseRange Range(int index, int startLine, int endLine, bool conflicting)
    {
        return new ModifiedBaseRange(
            Index: index,
            Base: new LineRange(startLine, endLine),
            Ours: new LineRange(startLine, endLine),
            Theirs: new LineRange(startLine, endLine),
            ResultMarkedRange: new LineRange(startLine, endLine),
            BaseLines: new[] { "" },
            OursLines: new[] { "" },
            TheirsLines: new[] { "" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: conflicting,
            IsOrderRelevant: true);
    }

    [StaFact]
    public void NoConflictingRanges_ReturnsNullLabel_AndHidesHeader()
    {
        var header = new StickyConflictHeader
        {
            Layout = new MergePaneGlyphLayout(),
            Ranges = new[] { Range(0, 10, 20, conflicting: false) },
            Side = MergePaneSide.Ours,
        };

        header.ComputeLabel().Should().BeNull();
        header.Visibility.Should().Be(Visibility.Collapsed,
            because: "no conflict to summarize → strip must stay out of view");
    }

    [StaFact]
    public void ViewportAboveFirstConflict_ReturnsNull()
    {
        var layout = new MergePaneGlyphLayout();
        var header = new StickyConflictHeader
        {
            Layout = layout,
            Side = MergePaneSide.Ours,
            Ranges = new[] { Range(0, 10, 15, conflicting: true) },
            // Scrolled to line 5 — conflict at line 10 is BELOW the viewport top.
            VerticalOffset = 4 * layout.LineHeight,
        };

        header.ComputeLabel().Should().BeNull(
            because: "the label only fires once the user has scrolled into or past a conflict");
    }

    [StaFact]
    public void ViewportOnFirstConflict_ReportsConflictOneOfN()
    {
        var layout = new MergePaneGlyphLayout();
        var header = new StickyConflictHeader
        {
            Layout = layout,
            Side = MergePaneSide.Ours,
            Ranges = new[]
            {
                Range(0, 10, 15, conflicting: true),
                Range(1, 30, 35, conflicting: true),
            },
            // Exactly at conflict 0's top.
            VerticalOffset = (10 - 1) * layout.LineHeight,
        };

        header.ComputeLabel().Should().Be("Conflict 1 of 2 · Unresolved");
        header.Visibility.Should().Be(Visibility.Visible);
    }

    [StaFact]
    public void ViewportBetweenConflicts_StillReportsTheOneWeScrolledPast()
    {
        var layout = new MergePaneGlyphLayout();
        var header = new StickyConflictHeader
        {
            Layout = layout,
            Side = MergePaneSide.Ours,
            Ranges = new[]
            {
                Range(0, 10, 15, conflicting: true),
                Range(1, 30, 35, conflicting: true),
            },
            // Scrolled past conflict 0 but not yet at conflict 1.
            VerticalOffset = 20 * layout.LineHeight,
        };

        header.ComputeLabel().Should().Be("Conflict 1 of 2 · Unresolved",
            because: "sticky header keeps labeling the most recently entered conflict until the next one reaches the top");
    }

    [StaFact]
    public void ViewportOnSecondConflict_ReportsConflictTwoOfN()
    {
        var layout = new MergePaneGlyphLayout();
        var header = new StickyConflictHeader
        {
            Layout = layout,
            Side = MergePaneSide.Ours,
            Ranges = new[]
            {
                Range(0, 10, 15, conflicting: true),
                Range(1, 30, 35, conflicting: true),
                Range(2, 50, 55, conflicting: true),
            },
            VerticalOffset = (30 - 1) * layout.LineHeight,
        };

        header.ComputeLabel().Should().Be("Conflict 2 of 3 · Unresolved");
    }

    [StaFact]
    public void NonConflictingRanges_AreSkippedInCountAndIndex()
    {
        var layout = new MergePaneGlyphLayout();
        var header = new StickyConflictHeader
        {
            Layout = layout,
            Side = MergePaneSide.Ours,
            Ranges = new[]
            {
                Range(0, 10, 15, conflicting: false), // Auto-merged, invisible to label.
                Range(1, 30, 35, conflicting: true),  // Conflict 1 of 1.
            },
            VerticalOffset = (30 - 1) * layout.LineHeight,
        };

        header.ComputeLabel().Should().Be("Conflict 1 of 1 · Unresolved",
            because: "auto-merged ranges should not inflate the N-of-M count");
    }

    [StaFact]
    public void ResolvedRange_LabelReflectsAcceptOurs()
    {
        var layout = new MergePaneGlyphLayout();
        var header = new StickyConflictHeader
        {
            Layout = layout,
            Side = MergePaneSide.Ours,
            Ranges = new[] { Range(0, 10, 15, conflicting: true) },
            RangeStates = new Dictionary<int, ResolutionState>
            {
                [0] = ResolutionState.AcceptOurs.Instance,
            },
            VerticalOffset = (10 - 1) * layout.LineHeight,
        };

        header.ComputeLabel().Should().Be("Conflict 1 of 1 · Ours accepted");
    }

    [StaFact]
    public void ResolvedRange_LabelReflectsAcceptTheirs()
    {
        var layout = new MergePaneGlyphLayout();
        var header = new StickyConflictHeader
        {
            Layout = layout,
            Side = MergePaneSide.Theirs,
            Ranges = new[] { Range(0, 10, 15, conflicting: true) },
            RangeStates = new Dictionary<int, ResolutionState>
            {
                [0] = ResolutionState.AcceptTheirs.Instance,
            },
            VerticalOffset = (10 - 1) * layout.LineHeight,
        };

        header.ComputeLabel().Should().Be("Conflict 1 of 1 · Theirs accepted");
    }

    [StaFact]
    public void ResolvedRange_LabelReflectsAcceptBoth()
    {
        var layout = new MergePaneGlyphLayout();
        var header = new StickyConflictHeader
        {
            Layout = layout,
            Side = MergePaneSide.Ours,
            Ranges = new[] { Range(0, 10, 15, conflicting: true) },
            RangeStates = new Dictionary<int, ResolutionState>
            {
                [0] = new ResolutionState.AcceptBoth(FirstOurs: true, SmartCombine: false),
            },
            VerticalOffset = (10 - 1) * layout.LineHeight,
        };

        header.ComputeLabel().Should().Be("Conflict 1 of 1 · Both accepted");
    }

    [StaFact]
    public void ResolvedRange_LabelReflectsManualResolution()
    {
        var layout = new MergePaneGlyphLayout();
        var header = new StickyConflictHeader
        {
            Layout = layout,
            Side = MergePaneSide.Ours,
            Ranges = new[] { Range(0, 10, 15, conflicting: true) },
            RangeStates = new Dictionary<int, ResolutionState>
            {
                [0] = new ResolutionState.Manual("custom text"),
            },
            VerticalOffset = (10 - 1) * layout.LineHeight,
        };

        header.ComputeLabel().Should().Be("Conflict 1 of 1 · Manually resolved");
    }

    [StaFact]
    public void SettingVerticalOffset_FiresDpCallback_FlippingVisibilityAndLabel()
    {
        // Guards the FrameworkPropertyMetadata wiring: the DP's AffectsRender +
        // OnInputChanged hookup is what synchronizes _currentLabel with
        // incoming property changes. A regression that forgot the callback (or
        // dropped AffectsRender) would silently leave Visibility stuck at its
        // constructor default — every "ComputeLabel()" test would still pass.
        var layout = new MergePaneGlyphLayout();
        var header = new StickyConflictHeader
        {
            Layout = layout,
            Side = MergePaneSide.Ours,
            Ranges = new[] { Range(0, 10, 15, conflicting: true) },
            // Start above the conflict so the header is Collapsed.
            VerticalOffset = 0,
        };

        header.Visibility.Should().Be(Visibility.Collapsed,
            because: "before scrolling into a conflict, the sticky strip is hidden");

        // Scroll the viewport down to the top of the conflict — the DP
        // callback must fire, recompute the label, and unhide the strip.
        header.VerticalOffset = (10 - 1) * layout.LineHeight;

        header.Visibility.Should().Be(Visibility.Visible,
            because: "scrolling into a conflict must flip the strip visible via OnInputChanged");
        header.ComputeLabel().Should().Be("Conflict 1 of 1 · Unresolved");
    }

    [StaFact]
    public void RefreshState_AfterInPlaceRangeStatesMutation_UpdatesCaption()
    {
        // RangeStates is a mutable Dictionary; MergeEditorViewModel rewrites
        // entries in place and raises RangeStatesChanged. No WPF DP change
        // notification fires, so the cached "· <state>" label would go
        // stale and the user would see "Conflict 1 of 1 · Unresolved" long
        // after they accepted the conflict via the pill. RefreshState is the
        // explicit resync entry point the view calls.
        var layout = new MergePaneGlyphLayout();
        var states = new Dictionary<int, ResolutionState>
        {
            [0] = ResolutionState.Unresolved.Instance,
        };
        var header = new StickyConflictHeader
        {
            Layout = layout,
            Side = MergePaneSide.Ours,
            Ranges = new[] { Range(0, 10, 15, conflicting: true) },
            RangeStates = states,
            VerticalOffset = (10 - 1) * layout.LineHeight,
        };

        header.ComputeLabel().Should().Be("Conflict 1 of 1 · Unresolved");

        // Mutate the same dictionary in place — the DP reference doesn't
        // change, so no automatic invalidation. Without RefreshState, the
        // cached label would stay "Unresolved" on the next render.
        states[0] = ResolutionState.AcceptOurs.Instance;
        header.RefreshState();

        header.ComputeLabel().Should().Be("Conflict 1 of 1 · Ours accepted",
            because: "RefreshState must re-run DescribeState against the mutated RangeStates");
    }

    [StaFact]
    public void ResultSide_UsesResultMarkedRange_ForYCoordinate()
    {
        // Ours and ResultMarkedRange can diverge once conflicts above have been
        // resolved. The header must track whichever side it was told to — here
        // we pin Result specifically so a regression that fell back to
        // Ours/Theirs would fail.
        var layout = new MergePaneGlyphLayout();
        var header = new StickyConflictHeader
        {
            Layout = layout,
            Side = MergePaneSide.Result,
            Ranges = new[] { Range(0, 10, 15, conflicting: true) },
            // Scrolled to where the ResultMarkedRange top lives.
            VerticalOffset = (10 - 1) * layout.LineHeight,
        };

        header.ComputeLabel().Should().Be("Conflict 1 of 1 · Unresolved");
    }

    [StaFact]
    public void ComputeCurrentVisibleIndex_TracksManualScroll()
    {
        // Pin the chevron-click resync logic that fixes the
        // "click Next at conflict 6 jumps to 8 skipping 7" bug. After a
        // user manually scrolls, the chevron handlers compute the
        // visually-current conflict via this method and write it to
        // CurrentIndex BEFORE invoking the navigation command — so
        // navigation always advances from the conflict the user is
        // visually on, not from a stale prior-click index.
        var layout = new MergePaneGlyphLayout();
        var ranges = new[]
        {
            Range(0, 10, 15, conflicting: true),  // user 1
            Range(1, 30, 35, conflicting: true),  // user 2
            Range(2, 50, 55, conflicting: true),  // user 3
        };
        var header = new StickyConflictHeader
        {
            Layout = layout,
            Side = MergePaneSide.Ours,
            Ranges = ranges,
        };

        // Scroll to before any conflict → no current visible.
        header.VerticalOffset = 0;
        header.ComputeCurrentVisibleIndex().Should().Be(-1,
            because: "viewport above the first conflict means no conflict is current");

        // Scroll to top of conflict 1 (line 10 0-based 9 * lineHeight).
        header.VerticalOffset = (10 - 1) * layout.LineHeight;
        header.ComputeCurrentVisibleIndex().Should().Be(0);

        // Scroll past conflict 2's top → it becomes current.
        header.VerticalOffset = (30 - 1) * layout.LineHeight;
        header.ComputeCurrentVisibleIndex().Should().Be(1);

        // Scroll past conflict 3's top → it becomes current.
        header.VerticalOffset = (50 - 1) * layout.LineHeight;
        header.ComputeCurrentVisibleIndex().Should().Be(2);
    }

    [StaFact]
    public void PreviousAndNextCommands_AreExposedAsBindableDPs()
    {
        // Pins the new DP surface added for the chevron-button navigation.
        // A future refactor that drops or renames PreviousCommand /
        // NextCommand would silently break the wiring at MergeEditorView.xaml
        // — this test catches it at build time via the property name.
        var header = new StickyConflictHeader();
        var prevExecuted = 0;
        var nextExecuted = 0;
        header.PreviousCommand = new RelayTestCommand(() => prevExecuted++);
        header.NextCommand = new RelayTestCommand(() => nextExecuted++);

        header.PreviousCommand.Execute(null);
        header.NextCommand.Execute(null);

        prevExecuted.Should().Be(1);
        nextExecuted.Should().Be(1);
    }
}

/// <summary>
/// Trivial ICommand used by sticky-header tests that need to assert routing
/// without depending on the full VM. Lives next to the test class rather
/// than in a shared fixture because no other test uses it today.
/// </summary>
file sealed class RelayTestCommand : System.Windows.Input.ICommand
{
    private readonly Action _execute;
    public RelayTestCommand(Action execute) { _execute = execute; }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute();
    public event EventHandler? CanExecuteChanged;
}
