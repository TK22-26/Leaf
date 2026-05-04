#nullable enable
using FluentAssertions;
using Leaf.Models.Merge;
using Xunit;

namespace Leaf.Tests.Models.Merge;

/// <summary>
/// Pins the per-line walker that drives the result-pane's gutter, BG tint,
/// and inline-element generator from a single classification source. Each
/// fixture targets a specific <see cref="MergeLineKind"/> emission rule
/// and the gutter-numbering convention; the three consumers map their UI
/// from these primitives.
/// </summary>
public class MergeDisplayMapTests
{
    [Fact]
    public void EmptyRanges_AllLinesAreContextWithSequentialNumbers()
    {
        var doc = MakeDoc(new[] { "a", "b", "c" });

        var map = doc.BuildDisplayMap(3, null);

        for (int i = 1; i <= 3; i++)
        {
            var line = map.GetLine(i);
            line.Kind.Should().Be(MergeLineKind.Context);
            line.RangeIndex.Should().Be(-1);
            line.FileLineNumber.Should().Be(i);
        }
    }

    [Fact]
    public void OutOfRangeLineNumber_DefaultsToContext()
    {
        var doc = MakeDoc(new[] { "a" });
        var map = doc.BuildDisplayMap(1, null);

        map.GetLine(0).Kind.Should().Be(MergeLineKind.Context);
        map.GetLine(0).RangeIndex.Should().Be(-1);
        map.GetLine(0).FileLineNumber.Should().BeNull();

        map.GetLine(99).Kind.Should().Be(MergeLineKind.Context);
        map.GetLine(99).RangeIndex.Should().Be(-1);
        map.GetLine(99).FileLineNumber.Should().BeNull();
    }

    [Fact]
    public void UnresolvedConflict_EmitsMarkerKindsAndSlotNumberedSections()
    {
        // 1: pre-context
        // 2: <<<<<<< / 3: ours-A / 4: ours-B
        // 5: ||||||| / 6: base-X
        // 7: ======= / 8: theirs-Y
        // 9: >>>>>>>
        var lines = new[]
        {
            "pre", "<<<<<<<", "ours-A", "ours-B", "|||||||", "base-X",
            "=======", "theirs-Y", ">>>>>>>",
        };
        var conflict = new ModifiedBaseRange(
            Index: 0,
            // Base.StartLine deliberately distinct from Ours/Theirs to confirm
            // the slot uses Ours.StartLine, not the per-side StartLine.
            Base: new LineRange(50, 51),
            Ours: new LineRange(2, 4),
            Theirs: new LineRange(5, 6),
            ResultMarkedRange: new LineRange(2, 10),
            BaseLines: new[] { "base-X" },
            OursLines: new[] { "ours-A", "ours-B" },
            TheirsLines: new[] { "theirs-Y" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);
        var doc = MakeDoc(lines, conflict);

        var map = doc.BuildDisplayMap(lines.Length, null);

        // Pre-context.
        map.GetLine(1).Kind.Should().Be(MergeLineKind.Context);
        map.GetLine(1).FileLineNumber.Should().Be(1);
        map.GetLine(1).RangeIndex.Should().Be(-1);

        // Marker kinds.
        map.GetLine(2).Kind.Should().Be(MergeLineKind.OpenMarker);
        map.GetLine(2).FileLineNumber.Should().BeNull();
        map.GetLine(5).Kind.Should().Be(MergeLineKind.BaseMarker);
        map.GetLine(7).Kind.Should().Be(MergeLineKind.EqualsMarker);
        map.GetLine(9).Kind.Should().Be(MergeLineKind.CloseMarker);

        // Section content + slot-based numbering (all start from Ours.StartLine=2).
        map.GetLine(3).Kind.Should().Be(MergeLineKind.UnresolvedOurs);
        map.GetLine(3).FileLineNumber.Should().Be(2);
        map.GetLine(4).Kind.Should().Be(MergeLineKind.UnresolvedOurs);
        map.GetLine(4).FileLineNumber.Should().Be(3);

        map.GetLine(6).Kind.Should().Be(MergeLineKind.UnresolvedBase);
        map.GetLine(6).FileLineNumber.Should().Be(2,
            because: "base-content shares the conflict slot — labeled from Ours.StartLine, not Base.StartLine=50");

        map.GetLine(8).Kind.Should().Be(MergeLineKind.UnresolvedTheirs);
        map.GetLine(8).FileLineNumber.Should().Be(2,
            because: "theirs-content shares the conflict slot — labeled from Ours.StartLine");

        // Every conflict line carries the range index for inline-element
        // command binding — markers AND content alike.
        for (int i = 2; i <= 9; i++)
        {
            map.GetLine(i).RangeIndex.Should().Be(0,
                because: $"line {i} is inside conflict 0's body");
        }
    }

    [Fact]
    public void AcceptOurs_BodyEmitsResolvedOursKind_PostContextResumesAtOursEnd()
    {
        var lines = new[] { "header", "ours-A", "ours-B", "footer" };
        var conflict = new ModifiedBaseRange(
            Index: 0,
            Base: new LineRange(2, 2),
            Ours: new LineRange(2, 4),
            Theirs: new LineRange(2, 3),
            ResultMarkedRange: new LineRange(2, 4),
            BaseLines: Array.Empty<string>(),
            OursLines: new[] { "ours-A", "ours-B" },
            TheirsLines: new[] { "theirs-A" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);
        var doc = MakeDoc(lines, conflict);
        var states = new Dictionary<int, ResolutionState>
        {
            [0] = ResolutionState.AcceptOurs.Instance,
        };

        var map = doc.BuildDisplayMap(lines.Length, states);

        map.GetLine(1).Kind.Should().Be(MergeLineKind.Context);
        map.GetLine(2).Kind.Should().Be(MergeLineKind.ResolvedOurs);
        map.GetLine(2).FileLineNumber.Should().Be(2);
        map.GetLine(3).Kind.Should().Be(MergeLineKind.ResolvedOurs);
        map.GetLine(3).FileLineNumber.Should().Be(3);
        map.GetLine(4).Kind.Should().Be(MergeLineKind.Context);
        map.GetLine(4).FileLineNumber.Should().Be(4,
            because: "post-conflict context resumes at slot start + body lines = 2 + 2 = 4");
    }

    [Fact]
    public void AcceptTheirs_BodyEmitsResolvedTheirsKind_MonotonicGutterEvenWithLengthMismatch()
    {
        // ours.Length=3, theirs.Length=1. AcceptTheirs emits 1 line. The
        // post-conflict context advances by the ACCEPTED side's length
        // (1), not by Ours.Length (3) — earlier code snapped to
        // Ours.EndLineExclusive and produced "5, 8" gutter jumps with
        // no 6 / 7 in between.
        var lines = new[] { "header", "theirs-A", "footer" };
        var conflict = new ModifiedBaseRange(
            Index: 0,
            Base: new LineRange(2, 2),
            Ours: new LineRange(2, 5),
            Theirs: new LineRange(2, 3),
            ResultMarkedRange: new LineRange(2, 3),
            BaseLines: Array.Empty<string>(),
            OursLines: new[] { "o1", "o2", "o3" },
            TheirsLines: new[] { "theirs-A" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);
        var doc = MakeDoc(lines, conflict);
        var states = new Dictionary<int, ResolutionState>
        {
            [0] = ResolutionState.AcceptTheirs.Instance,
        };

        var map = doc.BuildDisplayMap(lines.Length, states);

        map.GetLine(2).Kind.Should().Be(MergeLineKind.ResolvedTheirs);
        map.GetLine(2).FileLineNumber.Should().Be(2);
        map.GetLine(3).FileLineNumber.Should().Be(3,
            because: "footer = slotStart + accepted body lines = 2 + 1 = 3 — monotonic gutter");
    }

    [Fact]
    public void AcceptBoth_FirstOurs_EmitsOursThenTheirsKindsInOrder()
    {
        var lines = new[] { "header", "ours-A", "theirs-A", "footer" };
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
        var doc = MakeDoc(lines, conflict);
        var states = new Dictionary<int, ResolutionState>
        {
            [0] = new ResolutionState.AcceptBoth(FirstOurs: true, SmartCombine: false),
        };

        var map = doc.BuildDisplayMap(lines.Length, states);

        map.GetLine(2).Kind.Should().Be(MergeLineKind.ResolvedOurs);
        map.GetLine(3).Kind.Should().Be(MergeLineKind.ResolvedTheirs);
    }

    [Fact]
    public void AcceptBoth_TheirsFirst_EmitsTheirsThenOursKindsInOrder()
    {
        var lines = new[] { "header", "theirs-A", "ours-A", "footer" };
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
        var doc = MakeDoc(lines, conflict);
        var states = new Dictionary<int, ResolutionState>
        {
            [0] = new ResolutionState.AcceptBoth(FirstOurs: false, SmartCombine: false),
        };

        var map = doc.BuildDisplayMap(lines.Length, states);

        map.GetLine(2).Kind.Should().Be(MergeLineKind.ResolvedTheirs,
            because: "theirs-first ordering puts theirs at the slot start");
        map.GetLine(3).Kind.Should().Be(MergeLineKind.ResolvedOurs);
    }

    [Fact]
    public void Manual_BodyKindIsResolvedManual_FileLineNumbersNull()
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
        var doc = MakeDoc(lines, conflict);
        var states = new Dictionary<int, ResolutionState>
        {
            [0] = new ResolutionState.Manual("manual-1\nmanual-2\n"),
        };

        var map = doc.BuildDisplayMap(lines.Length, states);

        map.GetLine(2).Kind.Should().Be(MergeLineKind.ResolvedManual);
        map.GetLine(2).FileLineNumber.Should().BeNull(
            because: "Manual content has no canonical file-side number");
        map.GetLine(3).Kind.Should().Be(MergeLineKind.ResolvedManual);
    }

    [Theory]
    [InlineData("foo\nbar", 2)]
    [InlineData("foo\nbar\n", 2)]
    [InlineData("foo\rbar", 2)]
    [InlineData("foo\rbar\r", 2)]
    [InlineData("foo\r\nbar", 2)]
    [InlineData("foo\r\nbar\r\n", 2)]
    [InlineData("single", 1)]
    [InlineData("", 0)]
    public void CountResolutionLines_MatchesNormaliseToLfBoundary(string text, int expected)
    {
        // CR-only and CRLF Manual pastes must count the same way the
        // composer emits them — without the CR-aware path the gutter
        // and tint maps drift through the rest of the document.
        MergeDocument.CountResolutionLines(text).Should().Be(expected);
    }

    [Fact]
    public void TwoConflicts_AcceptingFirst_ContextBetweenStaysContextKind()
    {
        // Regression: earlier walkers leaked the resolved overlay across
        // context lines that scrolled up after a conflict was resolved.
        var lines = new[]
        {
            "ours-A",         // 1: conflict 0 resolved body
            "ctx-1",          // 2
            "ctx-2",          // 3
            "<<<<<<<",        // 4: conflict 1 open
            "ours-B",         // 5
            "|||||||",        // 6
            "=======",        // 7
            "theirs-B",       // 8
            ">>>>>>>",        // 9
        };
        var c1 = new ModifiedBaseRange(
            Index: 0, Base: new LineRange(1, 1), Ours: new LineRange(1, 2),
            Theirs: new LineRange(1, 2), ResultMarkedRange: new LineRange(1, 7),
            BaseLines: Array.Empty<string>(), OursLines: new[] { "ours-A" },
            TheirsLines: new[] { "theirs-A" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true, IsOrderRelevant: true);
        var c2 = new ModifiedBaseRange(
            Index: 1, Base: new LineRange(2, 2), Ours: new LineRange(2, 3),
            Theirs: new LineRange(2, 3), ResultMarkedRange: new LineRange(9, 15),
            BaseLines: Array.Empty<string>(), OursLines: new[] { "ours-B" },
            TheirsLines: new[] { "theirs-B" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true, IsOrderRelevant: true);
        var doc = MakeDoc(lines, c1, c2);
        var states = new Dictionary<int, ResolutionState>
        {
            [0] = ResolutionState.AcceptOurs.Instance,
        };

        var map = doc.BuildDisplayMap(lines.Length, states);

        map.GetLine(1).Kind.Should().Be(MergeLineKind.ResolvedOurs);
        map.GetLine(2).Kind.Should().Be(MergeLineKind.Context);
        map.GetLine(3).Kind.Should().Be(MergeLineKind.Context);
        map.GetLine(4).Kind.Should().Be(MergeLineKind.OpenMarker);
        map.GetLine(4).RangeIndex.Should().Be(1,
            because: "the second conflict's marker block binds to its own range index, NOT conflict 0's");
    }

    private static MergeDocument MakeDoc(IReadOnlyList<string> mergedLines, params ModifiedBaseRange[] ranges)
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
