#nullable enable
using FluentAssertions;
using Leaf.Controls.Merge;
using Leaf.Models.Merge;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Unit tests for <see cref="ResultPaneBackgroundRenderer"/>'s per-line tint
/// walker. The renderer paints content-line backgrounds via
/// <see cref="ResultPaneBackgroundRenderer.BuildTintMap"/>, which must
/// classify each displayed line into the correct side-tint (ours/base/theirs)
/// for unresolved conflicts and the chosen-side tint for resolved conflicts.
/// Misclassification puts the wrong colour on the wrong content — a silent
/// visual bug that's hard to spot in screenshots, so each scenario pins one
/// concrete failure mode.
/// </summary>
public class ResultPaneBackgroundRendererTests
{
    [StaFact]
    public void UnresolvedConflict_TintsOursBaseAndTheirsSections()
    {
        // 1: header
        // 2: <<<<<<< ours
        // 3: ours-content
        // 4: ||||||| base
        // 5: base-content
        // 6: =======
        // 7: theirs-content
        // 8: >>>>>>> theirs
        // 9: footer
        var lines = new[]
        {
            "header",
            "<<<<<<< ours",
            "ours-content",
            "||||||| base",
            "base-content",
            "=======",
            "theirs-content",
            ">>>>>>> theirs",
            "footer",
        };
        var doc = MakeDocument(lines, new ModifiedBaseRange(
            Index: 0,
            Base: new LineRange(2, 3),
            Ours: new LineRange(2, 3),
            Theirs: new LineRange(2, 3),
            ResultMarkedRange: new LineRange(2, 9),
            BaseLines: new[] { "base-content" },
            OursLines: new[] { "ours-content" },
            TheirsLines: new[] { "theirs-content" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true));
        var renderer = new ResultPaneBackgroundRenderer(() => doc, () => null);

        var map = renderer.BuildTintMap(lines.Length, doc, states: null);

        // Marker rows themselves carry no content-tint (Draw paints marker
        // chrome via a separate path) — they're null in the tint map.
        map[2].Should().BeNull(because: "<<<<<<< marker row has no content tint");
        map[4].Should().BeNull(because: "||||||| marker row has no content tint");
        map[6].Should().BeNull(because: "======= marker row has no content tint");
        map[8].Should().BeNull(because: ">>>>>>> marker row has no content tint");

        // Content rows: distinct ours/base/theirs tints.
        var oursTint = map[3];
        var baseTint = map[5];
        var theirsTint = map[7];
        oursTint.Should().NotBeNull(because: "line 3 sits inside the ours section");
        baseTint.Should().NotBeNull(because: "line 5 sits inside the base section");
        theirsTint.Should().NotBeNull(because: "line 7 sits inside the theirs section");
        oursTint.Should().NotBe(baseTint);
        oursTint.Should().NotBe(theirsTint);
        baseTint.Should().NotBe(theirsTint);

        // Lines outside any conflict: no tint.
        map[1].Should().BeNull(because: "header is outside any conflict");
        map[9].Should().BeNull(because: "footer is outside any conflict");
    }

    [StaFact]
    public void NoConflicts_AllLinesUntinted()
    {
        var lines = new[] { "alpha", "beta", "gamma" };
        var doc = MakeDocument(lines);
        var renderer = new ResultPaneBackgroundRenderer(() => doc, () => null);

        var map = renderer.BuildTintMap(lines.Length, doc, states: null);

        map[1].Should().BeNull();
        map[2].Should().BeNull();
        map[3].Should().BeNull();
    }

    [StaFact]
    public void AcceptOurs_TintsResolutionBodyOursBlue()
    {
        // Regression target for the post-acceptance overpaint bug:
        // earlier ClassifyLine looked up ResultMarkedRange (in
        // InitialMergedText coords), so after AcceptOurs collapsed a
        // conflict's body the tint walker mistakenly painted the
        // resolved-overlay on context lines that had scrolled up to
        // fill the gap. BuildTintMap now tracks displayed-line positions
        // explicitly, and AcceptOurs tints with ours-blue (not the
        // generic resolved-overlay green that made AcceptOurs look
        // identical to AcceptTheirs at a glance).
        var lines = new[] { "header", "ours-A", "footer" };
        var conflict = new ModifiedBaseRange(
            Index: 0,
            Base: new LineRange(2, 2),
            Ours: new LineRange(2, 3),
            Theirs: new LineRange(2, 3),
            ResultMarkedRange: new LineRange(2, 3),
            BaseLines: Array.Empty<string>(),
            OursLines: new[] { "ours-A" },
            TheirsLines: new[] { "theirs-A" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);
        var doc = MakeDocument(lines, conflict);
        var states = new Dictionary<int, ResolutionState>
        {
            [0] = ResolutionState.AcceptOurs.Instance,
        };
        var renderer = new ResultPaneBackgroundRenderer(() => doc, () => states);

        var map = renderer.BuildTintMap(lines.Length, doc, states);

        map[1].Should().BeNull(because: "header is pre-conflict context");
        map[2].Should().NotBeNull(because: "accepted-ours line gets ours-tint");
        map[3].Should().BeNull(because: "footer is post-conflict context — never within range body");
    }

    [StaFact]
    public void AcceptTheirs_TintsResolutionBodyTheirsGreen_NotOursBlue()
    {
        var lines = new[] { "header", "theirs-A", "footer" };
        var conflict = new ModifiedBaseRange(
            Index: 0,
            Base: new LineRange(2, 2),
            Ours: new LineRange(2, 3),
            Theirs: new LineRange(2, 3),
            ResultMarkedRange: new LineRange(2, 3),
            BaseLines: Array.Empty<string>(),
            OursLines: new[] { "ours-A" },
            TheirsLines: new[] { "theirs-A" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);
        var doc = MakeDocument(lines, conflict);
        var statesOurs = new Dictionary<int, ResolutionState> { [0] = ResolutionState.AcceptOurs.Instance };
        var statesTheirs = new Dictionary<int, ResolutionState> { [0] = ResolutionState.AcceptTheirs.Instance };

        var rendererOurs = new ResultPaneBackgroundRenderer(() => doc, () => statesOurs);
        var rendererTheirs = new ResultPaneBackgroundRenderer(() => doc, () => statesTheirs);

        var oursMap = rendererOurs.BuildTintMap(lines.Length, doc, statesOurs);
        var theirsMap = rendererTheirs.BuildTintMap(lines.Length, doc, statesTheirs);

        oursMap[2].Should().NotBeNull();
        theirsMap[2].Should().NotBeNull();
        oursMap[2].Should().NotBe(theirsMap[2],
            because: "AcceptOurs and AcceptTheirs must paint different tints — earlier both used the resolved-overlay and were indistinguishable");
    }

    [StaFact]
    public void TwoConflicts_AcceptingFirst_DoesNotOverpaintContextBelow()
    {
        // Two adjacent conflicts. Accepting conflict 1 shrinks its body
        // from a full marker block to just the ours-content. The
        // BG renderer's tint map MUST track the displayed-line shift —
        // earlier it kept painting the resolved-overlay across all
        // ResultMarkedRange.End-EndLineExclusive lines (in InitialMergedText
        // coords), which after the body shrunk corresponded to context
        // lines below the conflict. Result: swaths of context inappropriately
        // tinted resolved-overlay green.
        var lines = new[]
        {
            "ours-A",          // 1: conflict 1 ours-content (resolved body)
            "ctx-1",           // 2: between-conflict context (must NOT be tinted)
            "ctx-2",           // 3: same
            "<<<<<<< ours",    // 4: conflict 2 open
            "ours-B",          // 5: ours content
            "||||||| base",    // 6
            "=======",         // 7
            "theirs-B",        // 8: theirs content
            ">>>>>>> theirs",  // 9: close
        };
        // Ranges defined in InitialMergedText coords. The InitialMergedText
        // for this fixture (with both conflicts still unresolved) would be:
        //   1:  <<<<<<< ours
        //   2:  ours-A
        //   3:  ||||||| base
        //   4:  =======
        //   5:  theirs-A
        //   6:  >>>>>>> theirs
        //   7:  ctx-1
        //   8:  ctx-2
        //   9:  <<<<<<< ours
        //   10: ours-B
        //   11: ||||||| base
        //   12: =======
        //   13: theirs-B
        //   14: >>>>>>> theirs
        // After AcceptOurs of conflict 1, the displayed text (above) replaces
        // lines 1-6 with just "ours-A" (1 line), shifting everything down.
        var conflict1 = new ModifiedBaseRange(
            Index: 0,
            Base: new LineRange(1, 1),
            Ours: new LineRange(1, 2),
            Theirs: new LineRange(1, 2),
            ResultMarkedRange: new LineRange(1, 7),
            BaseLines: Array.Empty<string>(),
            OursLines: new[] { "ours-A" },
            TheirsLines: new[] { "theirs-A" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);
        var conflict2 = new ModifiedBaseRange(
            Index: 1,
            Base: new LineRange(2, 2),
            Ours: new LineRange(2, 3),
            Theirs: new LineRange(2, 3),
            ResultMarkedRange: new LineRange(9, 15),
            BaseLines: Array.Empty<string>(),
            OursLines: new[] { "ours-B" },
            TheirsLines: new[] { "theirs-B" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);
        var doc = MakeDocument(lines, conflict1, conflict2);
        var states = new Dictionary<int, ResolutionState>
        {
            [0] = ResolutionState.AcceptOurs.Instance,
        };
        var renderer = new ResultPaneBackgroundRenderer(() => doc, () => states);

        var map = renderer.BuildTintMap(lines.Length, doc, states);

        map[1].Should().NotBeNull(because: "conflict 1 is resolved → ours-A is the resolved body, ours-tinted");
        map[2].Should().BeNull(because: "context line between conflicts must not be tinted — regression: ResultMarkedRange-based walker over-painted these");
        map[3].Should().BeNull(because: "same — second context line stays untinted");
        // Conflict 2 markers untinted, content tinted by section.
        map[4].Should().BeNull();        // <<<<<<<
        map[5].Should().NotBeNull();     // ours-B → ours-tint
        map[6].Should().BeNull();        // |||||||
        map[7].Should().BeNull();        // =======
        map[8].Should().NotBeNull();     // theirs-B → theirs-tint
        map[9].Should().BeNull();        // >>>>>>>
    }

    [StaFact]
    public void ManualResolution_TintsBodyResolvedOverlay()
    {
        var lines = new[] { "header", "manual-1", "manual-2", "footer" };
        var conflict = new ModifiedBaseRange(
            Index: 0,
            Base: new LineRange(2, 2),
            Ours: new LineRange(2, 3),
            Theirs: new LineRange(2, 3),
            ResultMarkedRange: new LineRange(2, 4),
            BaseLines: Array.Empty<string>(),
            OursLines: new[] { "ours-A" },
            TheirsLines: new[] { "theirs-A" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);
        var doc = MakeDocument(lines, conflict);
        var states = new Dictionary<int, ResolutionState>
        {
            [0] = new ResolutionState.Manual("manual-1\nmanual-2\n"),
        };
        var renderer = new ResultPaneBackgroundRenderer(() => doc, () => states);

        var map = renderer.BuildTintMap(lines.Length, doc, states);

        map[1].Should().BeNull();
        map[2].Should().NotBeNull(because: "manual line 1 gets resolved-overlay tint (no canonical side)");
        map[3].Should().NotBeNull();
        map[4].Should().BeNull();
        map[2].Should().Be(map[3], because: "both manual lines share the resolved-overlay tint");
    }

    private static MergeDocument MakeDocument(IReadOnlyList<string> mergedLines, params ModifiedBaseRange[] ranges)
    {
        var text = string.Join('\n', mergedLines) + "\n";
        return new MergeDocument(
            filePath: "fixture",
            baseText: text,
            oursText: text,
            theirsText: text,
            initialMergedText: text,
            baseLines: mergedLines,
            oursLines: mergedLines,
            theirsLines: mergedLines,
            initialMergedLines: mergedLines,
            ranges: ranges,
            lineEnding: "\n",
            hasTrailingNewline: true);
    }
}
