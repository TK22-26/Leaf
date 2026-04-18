#nullable enable
using FluentAssertions;
using Leaf.Controls.Merge;
using Leaf.Models.Merge;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Unit tests for <see cref="ReadOnlyMergePane.IsAcceptedForSide"/>. This is
/// the pane's checkbox truth table — drives whether the per-range accept
/// glyph renders as checked or unchecked. A subtle wrong entry here would
/// mean the checkbox disagrees with the actual resolution state.
/// </summary>
public class ReadOnlyMergePaneTests
{
    [Theory]
    [InlineData(MergePaneSide.Ours, true)]
    [InlineData(MergePaneSide.Theirs, false)]
    [InlineData(MergePaneSide.Base, false)]
    public void IsAcceptedForSide_AcceptOurs_OnlyOursSeesItChecked(MergePaneSide side, bool expected)
    {
        ReadOnlyMergePane.IsAcceptedForSide(side, ResolutionState.AcceptOurs.Instance)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(MergePaneSide.Ours, false)]
    [InlineData(MergePaneSide.Theirs, true)]
    [InlineData(MergePaneSide.Base, false)]
    public void IsAcceptedForSide_AcceptTheirs_OnlyTheirsSeesItChecked(MergePaneSide side, bool expected)
    {
        ReadOnlyMergePane.IsAcceptedForSide(side, ResolutionState.AcceptTheirs.Instance)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(MergePaneSide.Ours, true)]
    [InlineData(MergePaneSide.Theirs, true)]
    [InlineData(MergePaneSide.Base, false)]
    public void IsAcceptedForSide_AcceptBoth_BothOursAndTheirsSeeItChecked(MergePaneSide side, bool expected)
    {
        ReadOnlyMergePane.IsAcceptedForSide(side,
            new ResolutionState.AcceptBoth(FirstOurs: true, SmartCombine: true))
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(MergePaneSide.Ours)]
    [InlineData(MergePaneSide.Theirs)]
    [InlineData(MergePaneSide.Base)]
    public void IsAcceptedForSide_Unresolved_AllSidesUnchecked(MergePaneSide side)
    {
        ReadOnlyMergePane.IsAcceptedForSide(side, ResolutionState.Unresolved.Instance)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(MergePaneSide.Ours)]
    [InlineData(MergePaneSide.Theirs)]
    [InlineData(MergePaneSide.Base)]
    public void IsAcceptedForSide_Manual_AllSidesUnchecked(MergePaneSide side)
    {
        // Manual resolution is authored through the result pane (when editable)
        // or via the AI popover — neither side's checkbox lights up, because
        // "manual" isn't a side choice.
        ReadOnlyMergePane.IsAcceptedForSide(side, new ResolutionState.Manual("x"))
            .Should().BeFalse();
    }

    [Fact]
    public void IsAcceptedForSide_AcceptBoth_OrderingDoesNotAffectCheckboxes()
    {
        // FirstOurs / SmartCombine are ordering details — the checkbox state
        // must be the same for all AcceptBoth variants. Regression guard for
        // "AcceptBoth(firstOurs=false) uncheck ours" bugs.
        var bothOursFirst = new ResolutionState.AcceptBoth(FirstOurs: true, SmartCombine: true);
        var bothTheirsFirst = new ResolutionState.AcceptBoth(FirstOurs: false, SmartCombine: true);
        var bothDumb = new ResolutionState.AcceptBoth(FirstOurs: true, SmartCombine: false);

        foreach (var state in new[] { bothOursFirst, bothTheirsFirst, bothDumb })
        {
            ReadOnlyMergePane.IsAcceptedForSide(MergePaneSide.Ours, state).Should().BeTrue();
            ReadOnlyMergePane.IsAcceptedForSide(MergePaneSide.Theirs, state).Should().BeTrue();
            ReadOnlyMergePane.IsAcceptedForSide(MergePaneSide.Base, state).Should().BeFalse();
        }
    }
}
