#nullable enable
using FluentAssertions;
using Leaf.Models.Merge;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// The renderer's per-line tint comes from <see cref="MergeDisplayMap"/>'s
/// <see cref="MergeLineKind"/> classification (see <c>BrushForKind</c>) —
/// canonical scenarios for that mapping live in <c>MergeDisplayMapTests</c>.
/// These tests pin the tint-relevant subset (which Kind values produce a
/// non-null brush, which produce null) by walking the same scenarios users
/// hit in production.
/// </summary>
public class ResultPaneBackgroundRendererTests
{
    [StaFact]
    public void UnresolvedConflict_ContentLinesGetSideKinds_MarkersGetMarkerKinds()
    {
        // 1: header
        // 2: <<<<<<<
        // 3: ours-content
        // 4: |||||||
        // 5: base-content
        // 6: =======
        // 7: theirs-content
        // 8: >>>>>>>
        // 9: footer
        var lines = new[]
        {
            "header", "<<<<<<< ours", "ours-content", "||||||| base", "base-content",
            "=======", "theirs-content", ">>>>>>> theirs", "footer",
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

        var map = doc.BuildDisplayMap(lines.Length, null);

        // Marker kinds drive the chrome paint path (paints in Draw, not via
        // BrushForKind).
        map.GetLine(2).Kind.Should().Be(MergeLineKind.OpenMarker);
        map.GetLine(4).Kind.Should().Be(MergeLineKind.BaseMarker);
        map.GetLine(6).Kind.Should().Be(MergeLineKind.EqualsMarker);
        map.GetLine(8).Kind.Should().Be(MergeLineKind.CloseMarker);

        // Content lines drive the BrushForKind tint mapping.
        map.GetLine(3).Kind.Should().Be(MergeLineKind.UnresolvedOurs);
        map.GetLine(5).Kind.Should().Be(MergeLineKind.UnresolvedBase);
        map.GetLine(7).Kind.Should().Be(MergeLineKind.UnresolvedTheirs);

        // Outside any conflict — no tint, no marker chrome.
        map.GetLine(1).Kind.Should().Be(MergeLineKind.Context);
        map.GetLine(9).Kind.Should().Be(MergeLineKind.Context);
    }

    [StaFact]
    public void AcceptOurs_ResolutionBodyIsResolvedOurs_NotResolvedTheirs()
    {
        // Earlier every accept tinted the generic resolved-overlay green
        // — making AcceptOurs visually indistinguishable from AcceptTheirs.
        // Pin the per-side kind here so the renderer's BrushForKind picks
        // ours-blue vs theirs-green correctly.
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
        var statesOurs = new Dictionary<int, ResolutionState> { [0] = ResolutionState.AcceptOurs.Instance };
        var statesTheirs = new Dictionary<int, ResolutionState> { [0] = ResolutionState.AcceptTheirs.Instance };

        var oursMap = doc.BuildDisplayMap(lines.Length, statesOurs);
        var theirsMap = doc.BuildDisplayMap(lines.Length, statesTheirs);

        oursMap.GetLine(2).Kind.Should().Be(MergeLineKind.ResolvedOurs);
        theirsMap.GetLine(2).Kind.Should().Be(MergeLineKind.ResolvedTheirs);
    }

    [StaFact]
    public void TwoConflicts_AcceptingFirst_DoesNotOverpaintContextBelow()
    {
        // Regression: earlier the BG renderer used InitialMergedText
        // coordinates to find resolved-range body extents, painting the
        // resolved overlay across context lines that had scrolled up to
        // fill the conflict's collapsed gap. The unified walker tracks
        // displayed-line positions, so context between conflicts MUST stay
        // classified as MergeLineKind.Context regardless of upstream
        // resolutions.
        // InitialMergedText layout (with both conflicts unresolved):
        //   1:  <<<<<<< ours / 2: ours-A / 3: ||||||| / 4: ======= / 5: theirs-A / 6: >>>>>>>
        //   7:  ctx-1 / 8: ctx-2
        //   9:  <<<<<<< ours / 10: ours-B / 11: ||||||| / 12: ======= / 13: theirs-B / 14: >>>>>>>
        // After AcceptOurs of conflict 1, displayed becomes:
        //   1:  ours-A / 2: ctx-1 / 3: ctx-2 / 4: <<<<<<< / 5: ours-B / 6: ||||||| / 7: ======= / 8: theirs-B / 9: >>>>>>>
        var lines = new[]
        {
            "ours-A", "ctx-1", "ctx-2",
            "<<<<<<< ours", "ours-B", "||||||| base", "=======", "theirs-B", ">>>>>>> theirs",
        };
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

        var map = doc.BuildDisplayMap(lines.Length, states);

        map.GetLine(1).Kind.Should().Be(MergeLineKind.ResolvedOurs, because: "conflict 1's resolved body is ours-A");
        map.GetLine(2).Kind.Should().Be(MergeLineKind.Context, because: "ctx-1 stays untinted — the regression target");
        map.GetLine(3).Kind.Should().Be(MergeLineKind.Context, because: "ctx-2 stays untinted");
        map.GetLine(4).Kind.Should().Be(MergeLineKind.OpenMarker);
        map.GetLine(5).Kind.Should().Be(MergeLineKind.UnresolvedOurs);
        map.GetLine(6).Kind.Should().Be(MergeLineKind.BaseMarker);
        map.GetLine(7).Kind.Should().Be(MergeLineKind.EqualsMarker);
        map.GetLine(8).Kind.Should().Be(MergeLineKind.UnresolvedTheirs);
        map.GetLine(9).Kind.Should().Be(MergeLineKind.CloseMarker);
    }

    [StaFact]
    public void ManualResolution_BodyIsResolvedManualKind()
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

        var map = doc.BuildDisplayMap(lines.Length, states);

        map.GetLine(2).Kind.Should().Be(MergeLineKind.ResolvedManual);
        map.GetLine(3).Kind.Should().Be(MergeLineKind.ResolvedManual);
        map.GetLine(4).Kind.Should().Be(MergeLineKind.Context);
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
