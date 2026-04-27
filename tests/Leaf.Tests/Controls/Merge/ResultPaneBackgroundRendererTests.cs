#nullable enable
using FluentAssertions;
using Leaf.Controls.Merge;
using Leaf.Models.Merge;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Pins the per-side line classification logic that drives the result pane's
/// side-tinted backgrounds. The renderer reads each conflict's
/// <see cref="ModifiedBaseRange.ResultMarkedRange"/> + the in-merged-text
/// position of <c>|||||||</c> / <c>=======</c> separators to decide whether
/// a given line should paint with the Ours / Base / Theirs / Resolved tint.
/// Misclassification would put the wrong color on the wrong content — a
/// silent visual bug that's easy to miss in screenshots — so this test
/// surface guards each section boundary explicitly.
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
            ResultMarkedRange: new LineRange(2, 9),  // line 2..8 inclusive
            BaseLines: new[] { "base-content" },
            OursLines: new[] { "ours-content" },
            TheirsLines: new[] { "theirs-content" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true));
        var renderer = new ResultPaneBackgroundRenderer(() => doc, () => null);

        // Marker lines themselves get no tint — the inline element generator
        // paints those.
        renderer.ClassifyLine(doc, null, 2).Should().BeNull(because: "<<<<<<< marker line is rendered by the inline generator");
        renderer.ClassifyLine(doc, null, 4).Should().BeNull(because: "||||||| marker line is rendered by the inline generator");
        renderer.ClassifyLine(doc, null, 6).Should().BeNull(because: "======= marker line is rendered by the inline generator");
        renderer.ClassifyLine(doc, null, 8).Should().BeNull(because: ">>>>>>> marker line is rendered by the inline generator");

        // Content lines: ours / base / theirs.
        var oursTint = renderer.ClassifyLine(doc, null, 3);
        var baseTint = renderer.ClassifyLine(doc, null, 5);
        var theirsTint = renderer.ClassifyLine(doc, null, 7);
        oursTint.Should().NotBeNull(because: "line 3 sits inside the ours section");
        baseTint.Should().NotBeNull(because: "line 5 sits inside the base section");
        theirsTint.Should().NotBeNull(because: "line 7 sits inside the theirs section");

        // The three section tints must all be distinguishable — a regression
        // that collapsed two of them to the same brush would silently lose
        // the side affordance.
        oursTint.Should().NotBe(baseTint);
        oursTint.Should().NotBe(theirsTint);
        baseTint.Should().NotBe(theirsTint);

        // Lines outside the conflict get no tint.
        renderer.ClassifyLine(doc, null, 1).Should().BeNull(because: "header is outside any conflict");
        renderer.ClassifyLine(doc, null, 9).Should().BeNull(because: "footer is outside any conflict");
    }

    [StaFact]
    public void ResolvedConflict_TintsAllMarkedLinesWithResolvedOverlay()
    {
        var lines = new[]
        {
            "header",
            "<<<<<<< ours",
            "ours-content",
            "=======",
            "theirs-content",
            ">>>>>>> theirs",
            "footer",
        };
        var doc = MakeDocument(lines, new ModifiedBaseRange(
            Index: 0,
            Base: new LineRange(2, 2),
            Ours: new LineRange(2, 3),
            Theirs: new LineRange(2, 3),
            ResultMarkedRange: new LineRange(2, 7),
            BaseLines: Array.Empty<string>(),
            OursLines: new[] { "ours-content" },
            TheirsLines: new[] { "theirs-content" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true));
        var states = new Dictionary<int, ResolutionState>
        {
            [0] = ResolutionState.AcceptOurs.Instance,
        };
        var renderer = new ResultPaneBackgroundRenderer(() => doc, () => states);

        // Inside the resolved range, every line gets the resolved tint
        // (single colour, no per-side breakdown — the conflict is settled).
        var resolvedTint = renderer.ClassifyLine(doc, states, 3);
        resolvedTint.Should().NotBeNull();
        renderer.ClassifyLine(doc, states, 5).Should().Be(resolvedTint,
            because: "all resolved-range content lines share one tint");
        renderer.ClassifyLine(doc, states, 2).Should().Be(resolvedTint,
            because: "marker lines inside a resolved range also paint with the resolved tint — the inline-generator chrome layers atop");
    }

    [StaFact]
    public void NoConflicts_AllLinesHaveNoTint()
    {
        var lines = new[] { "alpha", "beta", "gamma" };
        var doc = MakeDocument(lines);
        var renderer = new ResultPaneBackgroundRenderer(() => doc, () => null);
        renderer.ClassifyLine(doc, null, 1).Should().BeNull();
        renderer.ClassifyLine(doc, null, 2).Should().BeNull();
        renderer.ClassifyLine(doc, null, 3).Should().BeNull();
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
