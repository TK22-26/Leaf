#nullable enable
using FluentAssertions;
using Leaf.Models.Merge;
using Xunit;

namespace Leaf.Tests.Models.Merge;

/// <summary>
/// Property-style invariant tests for <see cref="MergeDocument.ComposeResolvedText"/>.
/// xUnit <c>[Theory]</c> + hand-written seed values stand in for a full FsCheck
/// generator — the goal is to pin the invariants that hold regardless of which
/// ranges are which state, not to fuzz-test the engine itself.
/// </summary>
public class MergeDocumentInvariantsTests
{
    private static MergeDocument BuildDoc(
        IReadOnlyList<ModifiedBaseRange> ranges,
        string initialMergedText,
        string lineEnding = "\n",
        bool hasTrailingNewline = true)
    {
        return new MergeDocument(
            filePath: "test.cs",
            baseText: string.Empty,
            oursText: string.Empty,
            theirsText: string.Empty,
            initialMergedText: initialMergedText,
            baseLines: Array.Empty<string>(),
            oursLines: Array.Empty<string>(),
            theirsLines: Array.Empty<string>(),
            initialMergedLines: initialMergedText.TrimEnd('\n').Split('\n'),
            ranges: ranges,
            lineEnding: lineEnding,
            hasTrailingNewline: hasTrailingNewline);
    }

    private static ModifiedBaseRange MakeConflictRange(
        int index, int startLine, string ours, string theirs,
        string oursLabel = "HEAD", string theirsLabel = "incoming")
    {
        // ResultMarkedRange spans the full marker triad including opener + separator + closer.
        // For a single-line ours + single-line theirs with no base region (standard zdiff3
        // fallback when base is unavailable), that's 5 lines:
        //   <<<<<<< HEAD
        //   <ours>
        //   =======
        //   <theirs>
        //   >>>>>>> incoming
        return new ModifiedBaseRange(
            Index: index,
            Base: new LineRange(startLine, startLine + 1),
            Ours: new LineRange(startLine, startLine + 1),
            Theirs: new LineRange(startLine, startLine + 1),
            ResultMarkedRange: new LineRange(startLine, startLine + 5),
            BaseLines: Array.Empty<string>(),
            OursLines: new[] { ours },
            TheirsLines: new[] { theirs },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true,
            OursLabel: oursLabel,
            BaseLabel: null,
            TheirsLabel: theirsLabel);
    }

    [Fact]
    public void Invariant_EmptyStatesYieldsInitialText()
    {
        // Compose with no resolved ranges should be exactly the initial
        // merged text (zdiff3 markers and all). This is the identity case.
        var ranges = new[] { MakeConflictRange(0, 2, "o", "t") };
        var initial = "line1\n<<<<<<< HEAD\no\n=======\nt\n>>>>>>> incoming\nline2\n";
        var doc = BuildDoc(ranges, initial);
        var result = doc.ComposeResolvedText(new Dictionary<int, ResolutionState>());
        result.Should().Be(initial);
    }

    [Fact]
    public void Invariant_UnresolvedStateIsSemanticallyEqualToNoState()
    {
        // Explicit Unresolved should be *semantically* equivalent to omitting
        // the entry. The two paths are not required to produce byte-identical
        // output — the dict-empty case returns InitialMergedText verbatim while
        // the walker re-synthesises zdiff3 markers from range labels, which
        // in zdiff3 format always include the `|||||||` base marker.
        // What must hold: both produce a commit-gate-triggering triad containing
        // the same ours/theirs content.
        var ranges = new[] { MakeConflictRange(0, 1, "o", "t") };
        var initial = "<<<<<<< HEAD\no\n|||||||\n=======\nt\n>>>>>>> incoming\n";
        var doc = BuildDoc(ranges, initial);
        doc.ComposeResolvedText(new Dictionary<int, ResolutionState>())
            .Should().Contain("<<<<<<<").And.Contain("=======").And.Contain(">>>>>>>")
            .And.Contain("o").And.Contain("t");
        doc.ComposeResolvedText(
                new Dictionary<int, ResolutionState> { { 0, ResolutionState.Unresolved.Instance } })
            .Should().Contain("<<<<<<<").And.Contain("=======").And.Contain(">>>>>>>")
            .And.Contain("o").And.Contain("t");
    }

    [Fact]
    public void Invariant_ComposeIsDeterministic_SameInputSameOutput()
    {
        // Pure-function guarantee. The composition has no hidden state
        // (no dictionary mutation, no time-dependent branches).
        var ranges = new[] { MakeConflictRange(0, 1, "o", "t"), MakeConflictRange(1, 2, "o2", "t2") };
        var initial = "<<<<<<< HEAD\no\n=======\nt\n>>>>>>> incoming\n" +
                      "<<<<<<< HEAD\no2\n=======\nt2\n>>>>>>> incoming\n";
        var doc = BuildDoc(ranges, initial);
        var states = new Dictionary<int, ResolutionState>
        {
            { 0, ResolutionState.AcceptOurs.Instance },
            { 1, ResolutionState.AcceptTheirs.Instance },
        };
        var first = doc.ComposeResolvedText(states);
        var second = doc.ComposeResolvedText(states);
        var third = doc.ComposeResolvedText(states);
        first.Should().Be(second).And.Be(third);
    }

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\n", true)]
    [InlineData("\r\n", false)]
    [InlineData("\r\n", true)]
    public void Invariant_TrailingNewlineIsPreserved(string lineEnding, bool hasTrailing)
    {
        // The pipeline must round-trip the "does this file end with a newline"
        // flag. Git cares deeply; a missed trailing-newline toggles a diff.
        var ranges = new[] { MakeConflictRange(0, 1, "o", "t") };
        var initial = $"<<<<<<< HEAD{lineEnding}o{lineEnding}======={lineEnding}" +
                      $"t{lineEnding}>>>>>>> incoming{lineEnding}";
        if (!hasTrailing) initial = initial.TrimEnd('\n', '\r');
        var doc = BuildDoc(ranges, initial, lineEnding, hasTrailing);
        var composed = doc.ComposeResolvedText(
            new Dictionary<int, ResolutionState> { { 0, ResolutionState.AcceptOurs.Instance } });
        composed.EndsWith(lineEnding).Should().Be(hasTrailing);
    }

    [Fact]
    public void Invariant_ManualStateIsUsedVerbatim()
    {
        // A Manual state's text substitutes the range verbatim. No trimming,
        // no extra newlines. Commit gate relies on this — if Manual added a
        // rogue newline, AvalonEdit's CRLF-preservation downstream would drift.
        var ranges = new[] { MakeConflictRange(0, 1, "o", "t") };
        var initial = "<<<<<<< HEAD\no\n=======\nt\n>>>>>>> incoming\n";
        var doc = BuildDoc(ranges, initial);
        var manual = "exact custom text\nover multiple lines";
        var composed = doc.ComposeResolvedText(
            new Dictionary<int, ResolutionState> { { 0, new ResolutionState.Manual(manual) } });
        composed.Should().Contain(manual);
        composed.Should().NotContain("<<<<<<<");
    }

    [Theory]
    [InlineData(true, true)]   // ours first, smart dedupe
    [InlineData(false, true)]  // theirs first, smart dedupe
    [InlineData(true, false)]  // ours first, dumb concat
    [InlineData(false, false)] // theirs first, dumb concat
    public void Invariant_AcceptBoth_ContainsBothSidesContent(bool firstOurs, bool smart)
    {
        // Whatever the flags, the composed output must include both ours and
        // theirs content. The smart/firstOurs knobs affect ordering + dedup,
        // not inclusion.
        var ranges = new[] { MakeConflictRange(0, 1, "ours-side", "theirs-side") };
        var initial = "<<<<<<< HEAD\nours-side\n=======\ntheirs-side\n>>>>>>> incoming\n";
        var doc = BuildDoc(ranges, initial);
        var composed = doc.ComposeResolvedText(
            new Dictionary<int, ResolutionState>
            {
                { 0, new ResolutionState.AcceptBoth(firstOurs, smart) }
            });
        composed.Should().Contain("ours-side");
        composed.Should().Contain("theirs-side");
        composed.Should().NotContain("<<<<<<<");
    }

    [Fact]
    public void Invariant_AcceptBoth_OrderingFlagActuallyOrders()
    {
        // Distinct content on each side, order must be observable.
        var ranges = new[] { MakeConflictRange(0, 1, "LEFT", "RIGHT") };
        var initial = "<<<<<<< HEAD\nLEFT\n=======\nRIGHT\n>>>>>>> incoming\n";
        var doc = BuildDoc(ranges, initial);
        var oursFirst = doc.ComposeResolvedText(
            new Dictionary<int, ResolutionState>
            {
                { 0, new ResolutionState.AcceptBoth(FirstOurs: true, SmartCombine: false) }
            });
        var theirsFirst = doc.ComposeResolvedText(
            new Dictionary<int, ResolutionState>
            {
                { 0, new ResolutionState.AcceptBoth(FirstOurs: false, SmartCombine: false) }
            });
        oursFirst.IndexOf("LEFT").Should().BeLessThan(oursFirst.IndexOf("RIGHT"));
        theirsFirst.IndexOf("RIGHT").Should().BeLessThan(theirsFirst.IndexOf("LEFT"));
    }

    [Fact]
    public void Invariant_ResolvingOneRangeDoesNotAffectOthers()
    {
        // Two independent conflicts — resolving one must leave the other
        // in its original state. The composition walks linearly so this
        // is a locality invariant that's easy to break with a global mutation.
        // Range 0 at lines 1-5; range 1 at lines 6-10.
        var ranges = new[]
        {
            MakeConflictRange(0, 1, "A-ours", "A-theirs"),
            MakeConflictRange(1, 6, "B-ours", "B-theirs"),
        };
        var initial =
            "<<<<<<< HEAD\nA-ours\n=======\nA-theirs\n>>>>>>> incoming\n" +
            "<<<<<<< HEAD\nB-ours\n=======\nB-theirs\n>>>>>>> incoming\n";
        var doc = BuildDoc(ranges, initial);
        var composed = doc.ComposeResolvedText(
            new Dictionary<int, ResolutionState> { { 0, ResolutionState.AcceptOurs.Instance } });
        // A is resolved → only its ours-side content remains, no triad
        composed.Should().Contain("A-ours");
        composed.Should().NotContain("A-theirs");
        // B is still unresolved → all marker pieces present
        composed.Should().Contain("<<<<<<<");
        composed.Should().Contain("B-ours");
        composed.Should().Contain("B-theirs");
        composed.Should().Contain(">>>>>>>");
    }

    [Fact]
    public void Invariant_AcceptOursThenUnresolve_ReturnsToOriginal()
    {
        // The "undo" invariant at the data-model level: sequential state
        // changes are equivalent to a single final state.
        var ranges = new[] { MakeConflictRange(0, 1, "o", "t") };
        var initial = "<<<<<<< HEAD\no\n=======\nt\n>>>>>>> incoming\n";
        var doc = BuildDoc(ranges, initial);

        var states = new Dictionary<int, ResolutionState>
        {
            { 0, ResolutionState.AcceptOurs.Instance }
        };
        var afterAccept = doc.ComposeResolvedText(states);
        afterAccept.Should().NotContain("<<<<<<<");

        states.Remove(0); // simulate Unresolve
        var afterUnresolve = doc.ComposeResolvedText(states);
        afterUnresolve.Should().Be(initial);
    }
}
