#nullable enable
using FluentAssertions;
using Leaf.Controls.Merge;
using Leaf.Models.Merge;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Unit tests for <see cref="MergeResultLineNumberMargin"/>'s display-map
/// walker. The walker drives the result pane's gutter — wrong numbers here
/// would silently mis-attribute a line to the wrong side, masking the
/// divergence the gutter is meant to make obvious. Each scenario pins one
/// part of the contract: marker skip, ours/base/theirs sectioning,
/// per-resolution-state numbering, and the snap-back behaviour after a
/// conflict body so post-conflict context lines re-align with ours-file
/// coordinates.
/// </summary>
public class MergeResultLineNumberMarginTests
{
    [Fact]
    public void NoMergeDocument_ReturnsSequentialMap()
    {
        // Fallback to natural numbering when there's nothing to walk —
        // matches the stock LineNumberMargin so a missing document doesn't
        // read as a bug.
        var map = MergeResultLineNumberMargin.BuildDisplayMap(
            docLineCount: 5, mergeDoc: null, states: null);
        for (int i = 1; i <= 5; i++)
        {
            map[i].Should().Be(i);
        }
    }

    [Fact]
    public void NoRanges_ReturnsSequentialMap()
    {
        // An empty ranges list is the same shape as the no-conflict path
        // (all auto-merged from the start). Should yield natural numbering
        // without forcing the host to special-case "no conflicts".
        var doc = MakeDocument(new[] { "alpha", "beta", "gamma" });
        var map = MergeResultLineNumberMargin.BuildDisplayMap(
            docLineCount: 3, mergeDoc: doc, states: null);
        map[1].Should().Be(1);
        map[2].Should().Be(2);
        map[3].Should().Be(3);
    }

    [Fact]
    public void UnresolvedConflict_MatchesUserSpec_TheDog_Jumped_Sat_OnThePorch()
    {
        // Reproduces the user's explicit example:
        //   2: The dog
        //      OURS
        //   3: Jumped
        //      THEIRS
        //   3: Sat
        //      END
        //   4: on the porch.
        // Which corresponds to:
        //   ours-file:   line 1=?, line 2="The dog", line 3="Jumped", line 4="on the porch."
        //   theirs-file: line 1=?, line 2="The dog", line 3="Sat",     line 4="on the porch."
        // and the conflict at ours[3..4) / theirs[3..4) with empty base.
        // Displayed result-pane lines (post-zdiff3 markers, base empty):
        //   doc 1: "ignored-line-1" (auto-merged context, ours line 1)
        //   doc 2: "The dog"        (auto-merged context, ours line 2)
        //   doc 3: "<<<<<<< ours"   (marker, no number)
        //   doc 4: "Jumped"         (ours-section, ours line 3)
        //   doc 5: "||||||| base"   (marker, no number)
        //   doc 6: "======="        (marker, no number)
        //   doc 7: "Sat"            (theirs-section, theirs line 3)
        //   doc 8: ">>>>>>> theirs" (marker, no number)
        //   doc 9: "on the porch."  (auto-merged context, ours line 4)
        var lines = new[]
        {
            "ignored-line-1",
            "The dog",
            "<<<<<<< ours",
            "Jumped",
            "||||||| base",
            "=======",
            "Sat",
            ">>>>>>> theirs",
            "on the porch.",
        };
        var conflict = new ModifiedBaseRange(
            Index: 0,
            Base: new LineRange(3, 3),  // empty at insertion point line 3
            Ours: new LineRange(3, 4),
            Theirs: new LineRange(3, 4),
            ResultMarkedRange: new LineRange(3, 9),
            BaseLines: Array.Empty<string>(),
            OursLines: new[] { "Jumped" },
            TheirsLines: new[] { "Sat" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);
        var doc = MakeDocument(lines, conflict);

        var map = MergeResultLineNumberMargin.BuildDisplayMap(
            docLineCount: lines.Length, mergeDoc: doc, states: null);

        map[1].Should().Be(1, because: "ignored line is ours-line 1, auto-merged context");
        map[2].Should().Be(2, because: "'The dog' is ours-line 2, auto-merged context");
        map[3].Should().BeNull(because: "<<<<<<< marker line is unnumbered");
        map[4].Should().Be(3, because: "'Jumped' is ours-line 3 inside the ours-section");
        map[5].Should().BeNull(because: "||||||| marker line is unnumbered");
        map[6].Should().BeNull(because: "======= marker line is unnumbered");
        map[7].Should().Be(3, because: "'Sat' is theirs-line 3 inside the theirs-section");
        map[8].Should().BeNull(because: ">>>>>>> marker line is unnumbered");
        map[9].Should().Be(4, because: "'on the porch.' is ours-line 4, auto-merged context after the conflict");
    }

    [Fact]
    public void UnresolvedConflict_NumbersAllThreeSectionsByOursStartLine()
    {
        // All three conflict sections (ours / base / theirs) number from
        // Ours.StartLine — they're alternative content for the SAME slot in
        // the merged result file, not three separate file positions. Source
        // files (especially base) often have very different line counts;
        // numbering each section by its source-file line would produce a
        // non-monotonic gutter (e.g. ours-content shows 52 then base jumps
        // backwards to 48 because the base file is shorter — which reads
        // as "impossible" to users).
        var lines = new[]
        {
            "pre",                  // pre-context (ours-line 1)
            "<<<<<<< ours",
            "ours-A",
            "ours-B",
            "||||||| base",
            "base-X",
            "=======",
            "theirs-Y",
            ">>>>>>> theirs",
        };
        var conflict = new ModifiedBaseRange(
            Index: 0,
            // Intentional: Base.StartLine=50 (much later in base file).
            // We MUST NOT use this for the gutter — base content shares
            // the result-file slot at Ours.StartLine.
            Base: new LineRange(50, 51),
            Ours: new LineRange(2, 4),
            // Theirs.StartLine=5 (different from Ours.StartLine=2).
            // Same rule — theirs-content gutter labels from Ours.StartLine.
            Theirs: new LineRange(5, 6),
            ResultMarkedRange: new LineRange(2, 10),
            BaseLines: new[] { "base-X" },
            OursLines: new[] { "ours-A", "ours-B" },
            TheirsLines: new[] { "theirs-Y" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);
        var doc = MakeDocument(lines, conflict);

        var map = MergeResultLineNumberMargin.BuildDisplayMap(
            docLineCount: lines.Length, mergeDoc: doc, states: null);

        map[1].Should().Be(1, because: "pre-context line is ours-line 1");
        map[2].Should().BeNull();             // <<<<<<<
        map[3].Should().Be(2, because: "ours-A starts the conflict slot at result-line 2");
        map[4].Should().Be(3, because: "ours-B is the second line in the conflict slot");
        map[5].Should().BeNull();             // |||||||
        map[6].Should().Be(2, because: "base-X shares the conflict slot — labeled from Ours.StartLine, not Base.StartLine=50");
        map[7].Should().BeNull();             // =======
        map[8].Should().Be(2, because: "theirs-Y also shares the conflict slot — labeled from Ours.StartLine, not Theirs.StartLine=5");
        map[9].Should().BeNull();             // >>>>>>>
    }

    [Fact]
    public void AcceptOursState_NumbersOursContentWithOursLineNumbers()
    {
        // After the user clicks Accept Ours the markers disappear; the doc
        // shows just the ours-side content. The gutter must keep showing
        // those lines as ours-file numbers (not the doc-line position).
        var lines = new[] { "header", "ours-A", "ours-B", "footer" };
        var conflict = new ModifiedBaseRange(
            Index: 0,
            Base: new LineRange(2, 2),
            Ours: new LineRange(2, 4),
            Theirs: new LineRange(2, 3),
            ResultMarkedRange: new LineRange(2, 4),  // post-resolution span
            BaseLines: Array.Empty<string>(),
            OursLines: new[] { "ours-A", "ours-B" },
            TheirsLines: new[] { "theirs-X" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);
        var doc = MakeDocument(lines, conflict);
        var states = new Dictionary<int, ResolutionState>
        {
            [0] = ResolutionState.AcceptOurs.Instance,
        };

        var map = MergeResultLineNumberMargin.BuildDisplayMap(
            docLineCount: lines.Length, mergeDoc: doc, states: states);

        map[1].Should().Be(1, because: "header is ours-line 1, pre-conflict context");
        map[2].Should().Be(2, because: "first accepted ours line is ours-file line 2");
        map[3].Should().Be(3, because: "second accepted ours line is ours-file line 3");
        map[4].Should().Be(4, because: "footer snaps back to ours-pointer = Ours.EndLineExclusive = 4");
    }

    [Fact]
    public void AcceptTheirsState_NumbersTheirsContentWithTheirsLineNumbers()
    {
        var lines = new[] { "header", "theirs-A", "footer" };
        var conflict = new ModifiedBaseRange(
            Index: 0,
            Base: new LineRange(2, 2),
            Ours: new LineRange(2, 4),
            Theirs: new LineRange(2, 3),
            ResultMarkedRange: new LineRange(2, 3),
            BaseLines: Array.Empty<string>(),
            OursLines: new[] { "ours-A", "ours-B" },
            TheirsLines: new[] { "theirs-A" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);
        var doc = MakeDocument(lines, conflict);
        var states = new Dictionary<int, ResolutionState>
        {
            [0] = ResolutionState.AcceptTheirs.Instance,
        };

        var map = MergeResultLineNumberMargin.BuildDisplayMap(
            docLineCount: lines.Length, mergeDoc: doc, states: states);

        map[1].Should().Be(1);
        map[2].Should().Be(2, because: "accepted theirs takes the conflict slot, numbered from Ours.StartLine for monotonic gutter");
        // After AcceptTheirs the displayed body has theirs.Length=1 line, so
        // post-context resumes at slotStart+1=3 (NOT Ours.EndLineExclusive=4
        // which would produce a backward gutter jump from 2 to 4 skipping
        // the line in between). Snapping to the ACTUAL emitted body length
        // keeps the gutter monotonic regardless of which side is accepted.
        map[3].Should().Be(3, because: "footer snaps to slotStart + body lines = 2 + 1 = 3 — monotonic with the theirs-content line above");
    }

    [Fact]
    public void AcceptBoth_NumbersBothSidesSequentiallyFromOursStart()
    {
        // AcceptBoth shows ours-then-theirs (or reverse) at the conflict slot.
        // The line-number sequence is sequential from Ours.StartLine so the
        // gutter stays monotonic with surrounding context — Theirs.StartLine
        // (which can sit far apart from Ours.StartLine in their respective
        // files) would create a backward jump in the gutter that reads as
        // "impossible".
        var lines = new[] { "header", "ours-A", "theirs-A", "footer" };
        var conflict = new ModifiedBaseRange(
            Index: 0,
            Base: new LineRange(2, 2),
            Ours: new LineRange(2, 3),
            Theirs: new LineRange(5, 6),
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
            [0] = new ResolutionState.AcceptBoth(FirstOurs: true, SmartCombine: false),
        };

        var map = MergeResultLineNumberMargin.BuildDisplayMap(
            docLineCount: lines.Length, mergeDoc: doc, states: states);

        map[1].Should().Be(1);
        map[2].Should().Be(2, because: "first accept-both line is at conflict slot start (Ours.StartLine=2)");
        map[3].Should().Be(3, because: "second accept-both line is sequential from Ours.StartLine — does NOT jump to Theirs.StartLine=5");
        // AcceptBoth body emits ours.Length + theirs.Length = 1 + 1 = 2 lines.
        // Post-context resumes at slotStart + 2 = 4.
        map[4].Should().Be(4, because: "footer = slotStart + accept-both body lines = 2 + 2 = 4 — monotonic with the two slot lines above");
    }

    [Fact]
    public void ManualState_LeavesItsLinesUnnumbered()
    {
        // Free-form text has no canonical mapping back to either side, so
        // the gutter draws nothing for those rows — matches user spec for
        // "skip line numbers on lines that are not part of the file".
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

        var map = MergeResultLineNumberMargin.BuildDisplayMap(
            docLineCount: lines.Length, mergeDoc: doc, states: states);

        map[1].Should().Be(1);
        map[2].Should().BeNull(because: "manual lines have no ours/theirs anchor");
        map[3].Should().BeNull();
        // Manual body has 2 lines. Post-context resumes at slotStart + 2 = 4.
        map[4].Should().Be(4, because: "footer = slotStart + manual body lines = 2 + 2 = 4");
    }

    [Fact]
    public void TwoConflicts_AcrossOneSidedAutoMerge_DoesNotShiftSecondConflict()
    {
        // Regression: a one-sided auto-merged range (theirs added 2 lines,
        // Ours.Length=0 but ResultMarkedRange.Length=2) sits between two
        // conflicts. Earlier walker advanced ours-pointer through the
        // auto-merged body then snapped it back to Ours.EndLineExclusive,
        // leaving the pointer 2 lines short — the next conflict's
        // pre-context loop then over-emitted by 2, shifting the conflict's
        // marker pattern so markers got numbers and content got nulls.
        // This test pins the no-shift behaviour: every conflict-2 marker
        // line must read null, every conflict-2 content line must read its
        // file-side number.
        var lines = new[]
        {
            "<<<<<<< ours",        // 1: conflict 1 open
            "ours-A",               // 2: ours content
            "||||||| base",         // 3
            "=======",              // 4
            "theirs-A",             // 5: theirs content
            ">>>>>>> theirs",       // 6: conflict 1 close
            "added-1",              // 7: theirs-added auto-merge
            "added-2",              // 8: theirs-added auto-merge
            "<<<<<<< ours",         // 9: conflict 2 open
            "ours-B",               // 10
            "||||||| base",         // 11
            "=======",              // 12
            "theirs-B",             // 13
            ">>>>>>> theirs",       // 14: conflict 2 close
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
        // One-sided auto-merge: theirs added 2 lines, ours unchanged at this
        // position. Ours.Length=0, Theirs.Length=2, ResultMarkedRange.Length=2.
        var autoMerged = new ModifiedBaseRange(
            Index: 1,
            Base: new LineRange(2, 2),
            Ours: new LineRange(2, 2),
            Theirs: new LineRange(2, 4),
            ResultMarkedRange: new LineRange(7, 9),
            BaseLines: Array.Empty<string>(),
            OursLines: Array.Empty<string>(),
            TheirsLines: new[] { "added-1", "added-2" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: false,
            IsOrderRelevant: false);
        var conflict2 = new ModifiedBaseRange(
            Index: 2,
            Base: new LineRange(2, 2),
            Ours: new LineRange(2, 3),
            Theirs: new LineRange(4, 5),
            ResultMarkedRange: new LineRange(9, 15),
            BaseLines: Array.Empty<string>(),
            OursLines: new[] { "ours-B" },
            TheirsLines: new[] { "theirs-B" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);
        var doc = MakeDocument(lines, conflict1, autoMerged, conflict2);

        var map = MergeResultLineNumberMargin.BuildDisplayMap(
            docLineCount: lines.Length, mergeDoc: doc, states: null);

        // Conflict 1: markers null, content numbered from Ours.StartLine.
        map[1].Should().BeNull();
        map[2].Should().Be(1, because: "ours-A is at conflict slot start (Ours.StartLine=1)");
        map[3].Should().BeNull();
        map[4].Should().BeNull();
        map[5].Should().Be(1, because: "theirs-A shares the same conflict slot — labeled from Ours.StartLine=1");
        map[6].Should().BeNull();

        // Conflict 2 — the regression target. Every marker MUST be null,
        // every content line MUST get its conflict-slot number. A walker
        // that shifted by 2 here would put numbers on lines 9, 11, 12, 14
        // (the four marker rows) and nulls on 10 and 13.
        map[9].Should().BeNull(because: "conflict 2 opener — must remain unnumbered");
        map[10].Should().Be(2, because: "ours-B is at conflict slot start (Ours.StartLine=2)");
        map[11].Should().BeNull(because: "conflict 2 base separator — must remain unnumbered");
        map[12].Should().BeNull(because: "conflict 2 equals separator — must remain unnumbered");
        map[13].Should().Be(2, because: "theirs-B shares the same conflict slot — labeled from Ours.StartLine=2");
        map[14].Should().BeNull(because: "conflict 2 close — must remain unnumbered");
    }

    [Fact]
    public void AutoMergedRange_AdvancesOursPointerByMarkedRangeLength()
    {
        // Non-conflicting (auto-merged) ranges occupy their ResultMarkedRange
        // verbatim in the displayed text. Numbering advances ours-pointer
        // through them, then snaps to Ours.EndLineExclusive at the boundary.
        var lines = new[] { "auto-A", "auto-B", "after" };
        var autoMerged = new ModifiedBaseRange(
            Index: 0,
            Base: new LineRange(1, 3),
            Ours: new LineRange(1, 3),
            Theirs: new LineRange(1, 3),
            ResultMarkedRange: new LineRange(1, 3),
            BaseLines: new[] { "auto-A", "auto-B" },
            OursLines: new[] { "auto-A", "auto-B" },
            TheirsLines: new[] { "auto-A", "auto-B" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: false,
            IsOrderRelevant: false);
        var doc = MakeDocument(lines, autoMerged);

        var map = MergeResultLineNumberMargin.BuildDisplayMap(
            docLineCount: lines.Length, mergeDoc: doc, states: null);

        map[1].Should().Be(1);
        map[2].Should().Be(2);
        map[3].Should().Be(3);
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
