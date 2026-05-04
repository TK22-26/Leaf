#nullable enable
using FluentAssertions;
using Leaf.Controls.Merge;
using Leaf.Models.Merge;
using Leaf.TextEdit;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Pins the explicit Refresh() entry points that every RangeStates-consuming
/// overlay exposes for hosts that mutate the dictionary in place. Without
/// these, pre-seventh-pass code path let AcceptOurs clicks silently leave
/// the minimap / CodeLens bar / pane-connection canvas stuck in the
/// previous state.
/// </summary>
public class OverlayRefreshTests
{
    [StaFact]
    public void ConflictOverviewRuler_Refresh_IsCallable()
    {
        var map = new ConflictOverviewRuler();
        FluentActions.Invoking(() => map.Refresh()).Should().NotThrow();
    }

    [StaFact]
    public void CodeLensActionBar_Refresh_IsCallable()
    {
        var bar = new CodeLensActionBar
        {
            Layout = new MergePaneGlyphLayout(),
            Ranges = Array.Empty<ModifiedBaseRange>(),
        };
        FluentActions.Invoking(() => bar.Refresh()).Should().NotThrow();
    }

    [StaFact]
    public void CodeLensActionBar_Refresh_AfterStateChange_RebuildsWithNewOpacity()
    {
        // Before: an unresolved range produces a full-opacity bar. After
        // mutating RangeStates in-place + calling Refresh, the same range
        // should render at the resolved-fade opacity (0.4).
        var states = new Dictionary<int, ResolutionState>();
        var range = new ModifiedBaseRange(
            Index: 0,
            Base: new LineRange(1, 2),
            Ours: new LineRange(1, 2),
            Theirs: new LineRange(1, 2),
            ResultMarkedRange: new LineRange(1, 6),
            BaseLines: new[] { "" },
            OursLines: new[] { "" },
            TheirsLines: new[] { "" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);
        var bar = new CodeLensActionBar
        {
            Layout = new MergePaneGlyphLayout(),
            Ranges = new[] { range },
            RangeStates = states,
        };
        var panelBefore = (System.Windows.Controls.StackPanel)bar.Children[0]!;
        panelBefore.Opacity.Should().Be(1.0, because: "unresolved → full opacity");

        // In-place mutation — no DP reference change, would NOT trigger a
        // rebuild without the explicit Refresh call.
        states[0] = ResolutionState.AcceptOurs.Instance;
        bar.Refresh();

        var panelAfter = (System.Windows.Controls.StackPanel)bar.Children[0]!;
        panelAfter.Opacity.Should().BeApproximately(0.4, 0.001,
            because: "resolved ranges fade the CodeLens chrome after Refresh picks up the dictionary mutation");
    }

    [StaFact]
    public void PaneConnectionCanvas_Refresh_IsCallable()
    {
        var canvas = new PaneConnectionCanvas();
        FluentActions.Invoking(() => canvas.Refresh()).Should().NotThrow();
    }
}
