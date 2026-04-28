#nullable enable
using FluentAssertions;
using Leaf.Controls.Merge;
using Leaf.Models.Merge;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Pins the zdiff3 marker classification contract for the inline-element
/// generator that replaces conflict markers with VS-Code-style toolbars.
/// Misclassifying any of the four marker prefixes would render the wrong
/// inline element (e.g. an [Accept Ours · Theirs · Both · Compare] toolbar
/// at a closer instead of an opener), so this lives in its own test surface.
/// </summary>
public class ConflictMarkerInlineGeneratorTests
{
    [Theory]
    [InlineData("<<<<<<<", "Open")]
    [InlineData("<<<<<<< ours", "Open")]
    [InlineData(">>>>>>>", "Close")]
    [InlineData(">>>>>>> theirs", "Close")]
    [InlineData("|||||||", "Base")]
    [InlineData("||||||| base", "Base")]
    [InlineData("=======", "Equals")]
    public void ClassifyMarker_RecognisesAllFourZdiff3Markers(string line, string expectedKindName)
    {
        ConflictMarkerInlineGenerator.ClassifyMarker(line).ToString().Should().Be(expectedKindName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("public class Foo {")]
    [InlineData("    return user;")]
    [InlineData("// comment")]
    [InlineData("======")]      // 6 equals signs — not a marker
    [InlineData("======= bonus")] // 7 equals + content — exact-match-only marker
    [InlineData("<<<")]          // partial opener — not a marker
    public void ClassifyMarker_RejectsNonMarkerLines(string line)
    {
        ConflictMarkerInlineGenerator.ClassifyMarker(line).ToString().Should().Be("None");
    }

    [Fact]
    public void ClassifyMarker_DistinguishesOpenerFromCloser()
    {
        // Defensive: a regression that flipped Open vs Close prefixes would
        // render the [Accept Ours·Theirs·Both·Compare] toolbar at the WRONG
        // end of the conflict — visually plausible, semantically broken.
        ConflictMarkerInlineGenerator.ClassifyMarker("<<<<<<< ours").ToString().Should().Be("Open");
        ConflictMarkerInlineGenerator.ClassifyMarker(">>>>>>> theirs").ToString().Should().Be("Close");
    }

    // ── BuildMarkerMap: pairs marker rows in displayed text to range indices ──

    [Fact]
    public void BuildMarkerMap_PairsOpenMarkersToUnresolvedRangesInOrder()
    {
        // Two unresolved conflicts. Each `<<<<<<<` line should bind its
        // entire marker block (open through close) to the matching
        // ModifiedBaseRange.Index. Context lines and post-Close lines: -1.
        var lines = new[]
        {
            "context-1",        // 1
            "<<<<<<< ours",     // 2: conflict 0 open
            "ours-A",           // 3
            "=======",          // 4
            "theirs-A",         // 5
            ">>>>>>> theirs",   // 6: conflict 0 close
            "context-2",        // 7
            "<<<<<<< ours",     // 8: conflict 1 open
            "ours-B",           // 9
            "=======",          // 10
            "theirs-B",         // 11
            ">>>>>>> theirs",   // 12
        };
        var ranges = new[]
        {
            ConflictRange(index: 0),
            ConflictRange(index: 1),
        };

        var map = ConflictMarkerInlineGenerator.BuildMarkerMap(
            docLineCount: lines.Length,
            ranges: ranges,
            states: null,
            getDocLineText: line => lines[line - 1]);

        map[1].Should().Be(-1, because: "context line outside any conflict");
        map[2].Should().Be(0, because: "first <<<<<<< binds to first unresolved range (Index=0)");
        map[3].Should().Be(0);
        map[4].Should().Be(0);
        map[5].Should().Be(0);
        map[6].Should().Be(0);
        map[7].Should().Be(-1, because: "post-Close context outside any conflict");
        map[8].Should().Be(1, because: "second <<<<<<< binds to second unresolved range (Index=1)");
        map[12].Should().Be(1);
    }

    [Fact]
    public void BuildMarkerMap_SkipsResolvedConflictsWhenPairingMarkers()
    {
        // Regression: after the user accepts a conflict, its `<<<<<<<` block
        // disappears from the displayed text. Earlier the inline generator
        // looked up ResultMarkedRange directly and bound conflict 1's
        // remaining toolbar to range Index=0 — firing accept-commands
        // against the already-resolved range. The walker now skips
        // resolved entries when matching `<<<<<<<` markers to ranges.
        var lines = new[]
        {
            "ours-A-resolved",  // 1: conflict 0 was AcceptOurs — body shrunk to 1 line
            "context",          // 2
            "<<<<<<< ours",     // 3: conflict 1 open (still unresolved)
            "ours-B",           // 4
            "=======",          // 5
            "theirs-B",         // 6
            ">>>>>>> theirs",   // 7
        };
        var ranges = new[]
        {
            ConflictRange(index: 0),
            ConflictRange(index: 1),
        };
        var states = new Dictionary<int, ResolutionState>
        {
            [0] = ResolutionState.AcceptOurs.Instance,
        };

        var map = ConflictMarkerInlineGenerator.BuildMarkerMap(
            docLineCount: lines.Length,
            ranges: ranges,
            states: states,
            getDocLineText: line => lines[line - 1]);

        // Conflict 0's body is the resolved content — no markers visible,
        // so the walker doesn't bind line 1 to any range index.
        map[1].Should().Be(-1, because: "resolved-body content isn't a marker; no range binding here");
        map[2].Should().Be(-1);
        map[3].Should().Be(1, because: "the <<<<<<< at line 3 binds to range Index=1, NOT Index=0 (which is resolved)");
        map[4].Should().Be(1, because: "ours content of conflict 1");
        map[7].Should().Be(1, because: ">>>>>>> close still inside conflict 1's section");
    }

    [Fact]
    public void BuildMarkerMap_ReturnsMinusOneForExtraOpenMarkersBeyondUnresolvedSet()
    {
        // Defensive: a stale-display tick or a literal <<<<<<< typed into a
        // Manual resolution can produce an Open marker that has no matching
        // unresolved range. Walker returns -1 for those rows so the inline
        // generator renders a neutral placeholder instead of wiring accept
        // commands against an invalid range index.
        var lines = new[]
        {
            "<<<<<<< stale",    // 1: extra/stale Open with no matching range
            "literal-text",     // 2
            ">>>>>>> stale",    // 3
        };

        var map = ConflictMarkerInlineGenerator.BuildMarkerMap(
            docLineCount: lines.Length,
            ranges: Array.Empty<ModifiedBaseRange>(),
            states: null,
            getDocLineText: line => lines[line - 1]);

        map[1].Should().Be(-1, because: "no unresolved range available to bind to");
        map[2].Should().Be(-1);
        map[3].Should().Be(-1);
    }

    [Fact]
    public void BuildMarkerMap_NonConflictingRangesAreNotPaired()
    {
        // Auto-merged (!IsConflicting) ranges have no markers in the
        // displayed text. They must not consume the unresolved-cursor
        // when pairing `<<<<<<<` markers — otherwise an auto-merged range
        // ahead of a real conflict would steal that conflict's binding.
        var lines = new[]
        {
            "auto-merged-content",  // 1: from non-conflicting range
            "<<<<<<< ours",         // 2: should bind to the conflicting range, NOT the auto-merged one
            "ours",                 // 3
            "=======",              // 4
            "theirs",               // 5
            ">>>>>>> theirs",       // 6
        };
        var ranges = new[]
        {
            ConflictRange(index: 0, conflicting: false),  // auto-merged
            ConflictRange(index: 1, conflicting: true),
        };

        var map = ConflictMarkerInlineGenerator.BuildMarkerMap(
            docLineCount: lines.Length,
            ranges: ranges,
            states: null,
            getDocLineText: line => lines[line - 1]);

        map[2].Should().Be(1, because: "the <<<<<<< binds to the conflicting range (Index=1), skipping the auto-merged Index=0");
    }

    private static ModifiedBaseRange ConflictRange(int index, bool conflicting = true) =>
        new(
            Index: index,
            Base: new LineRange(1, 1),
            Ours: new LineRange(1, 2),
            Theirs: new LineRange(1, 2),
            ResultMarkedRange: new LineRange(1, 6),
            BaseLines: Array.Empty<string>(),
            OursLines: new[] { "ours" },
            TheirsLines: new[] { "theirs" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: conflicting,
            IsOrderRelevant: true);
}
