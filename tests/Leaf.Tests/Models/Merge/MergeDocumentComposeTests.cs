using FluentAssertions;
using Leaf.Models.Merge;
using Xunit;

namespace Leaf.Tests.Models.Merge;

public class MergeDocumentComposeTests
{
    private static MergeDocument DocumentWithSingleConflict(
        string lineEnding = "\n",
        bool hasTrailingNewline = true)
    {
        // Composed merged text with one zdiff3 conflict block:
        //   line 1: context_before
        //   line 2: <<<<<<< ours
        //   line 3: ours-1
        //   line 4: ours-2
        //   line 5: ||||||| base
        //   line 6: base-1
        //   line 7: =======
        //   line 8: theirs-1
        //   line 9: >>>>>>> theirs
        //   line 10: context_after
        var mergedLines = new[]
        {
            "context_before",
            "<<<<<<< ours",
            "ours-1",
            "ours-2",
            "||||||| base",
            "base-1",
            "=======",
            "theirs-1",
            ">>>>>>> theirs",
            "context_after",
        };
        var mergedText = string.Join("\n", mergedLines) + (hasTrailingNewline ? "\n" : string.Empty);

        var range = new ModifiedBaseRange(
            Index: 0,
            Base: new LineRange(2, 3),
            Ours: new LineRange(2, 4),
            Theirs: new LineRange(2, 3),
            ResultMarkedRange: new LineRange(2, 10), // lines 2..9 inclusive, half-open end = 10
            BaseLines: new[] { "base-1" },
            OursLines: new[] { "ours-1", "ours-2" },
            TheirsLines: new[] { "theirs-1" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);

        return new MergeDocument(
            filePath: "test.txt",
            baseText: string.Empty,
            oursText: string.Empty,
            theirsText: string.Empty,
            initialMergedText: mergedText,
            baseLines: new[] { "context_before", "base-1", "context_after" },
            oursLines: new[] { "context_before", "ours-1", "ours-2", "context_after" },
            theirsLines: new[] { "context_before", "theirs-1", "context_after" },
            initialMergedLines: mergedLines,
            ranges: new[] { range },
            lineEnding: lineEnding,
            hasTrailingNewline: hasTrailingNewline);
    }

    [Fact]
    public void Compose_WithNullStates_ReturnsInitialText()
    {
        var doc = DocumentWithSingleConflict();
        doc.ComposeResolvedText(null).Should().Be(doc.InitialMergedText);
    }

    [Fact]
    public void Compose_WithEmptyStates_ReturnsInitialText()
    {
        var doc = DocumentWithSingleConflict();
        doc.ComposeResolvedText(new Dictionary<int, ResolutionState>()).Should().Be(doc.InitialMergedText);
    }

    [Fact]
    public void Compose_AcceptOurs_SubstitutesOursContent()
    {
        var doc = DocumentWithSingleConflict();
        var composed = doc.ComposeResolvedText(new Dictionary<int, ResolutionState>
        {
            [0] = ResolutionState.AcceptOurs.Instance,
        });

        composed.Should().Be("context_before\nours-1\nours-2\ncontext_after\n");
    }

    [Fact]
    public void Compose_AcceptTheirs_SubstitutesTheirsContent()
    {
        var doc = DocumentWithSingleConflict();
        var composed = doc.ComposeResolvedText(new Dictionary<int, ResolutionState>
        {
            [0] = ResolutionState.AcceptTheirs.Instance,
        });

        composed.Should().Be("context_before\ntheirs-1\ncontext_after\n");
    }

    [Fact]
    public void Compose_AcceptBoth_OursFirst_InterleavesInOrder()
    {
        var doc = DocumentWithSingleConflict();
        var composed = doc.ComposeResolvedText(new Dictionary<int, ResolutionState>
        {
            [0] = new ResolutionState.AcceptBoth(FirstOurs: true, SmartCombine: true),
        });

        composed.Should().Be("context_before\nours-1\nours-2\ntheirs-1\ncontext_after\n");
    }

    [Fact]
    public void Compose_AcceptBoth_TheirsFirst_InterleavesReversed()
    {
        var doc = DocumentWithSingleConflict();
        var composed = doc.ComposeResolvedText(new Dictionary<int, ResolutionState>
        {
            [0] = new ResolutionState.AcceptBoth(FirstOurs: false, SmartCombine: true),
        });

        composed.Should().Be("context_before\ntheirs-1\nours-1\nours-2\ncontext_after\n");
    }

    [Fact]
    public void Compose_Manual_EmitsTextVerbatimWithTrailingNewline()
    {
        var doc = DocumentWithSingleConflict();
        var composed = doc.ComposeResolvedText(new Dictionary<int, ResolutionState>
        {
            [0] = new ResolutionState.Manual("custom line"),
        });

        composed.Should().Be("context_before\ncustom line\ncontext_after\n");
    }

    [Fact]
    public void Compose_UnresolvedState_KeepsConflictMarkers()
    {
        var doc = DocumentWithSingleConflict();
        var composed = doc.ComposeResolvedText(new Dictionary<int, ResolutionState>
        {
            [0] = ResolutionState.Unresolved.Instance,
        });

        composed.Should().Be(doc.InitialMergedText);
    }

    [Fact]
    public void Compose_CRLFLineEnding_RestoresOnOutput()
    {
        var doc = DocumentWithSingleConflict(lineEnding: "\r\n");
        var composed = doc.ComposeResolvedText(new Dictionary<int, ResolutionState>
        {
            [0] = ResolutionState.AcceptOurs.Instance,
        });

        composed.Should().Be("context_before\r\nours-1\r\nours-2\r\ncontext_after\r\n");
    }

    [Fact]
    public void Compose_NoTrailingNewline_IsPreserved()
    {
        var doc = DocumentWithSingleConflict(hasTrailingNewline: false);
        var composed = doc.ComposeResolvedText(new Dictionary<int, ResolutionState>
        {
            [0] = ResolutionState.AcceptTheirs.Instance,
        });

        // Without trailing newline, the final line has no \n but we still end lines during substitution.
        // We only suppress the trailing newline on the very last line.
        composed.Should().EndWith("context_after\n");
    }

    [Fact]
    public void Compose_AcceptBoth_OneSideEmpty_FallsBackToNonEmpty()
    {
        var doc = BuildDocWithEmptySide(emptyOurs: true);
        var composed = doc.ComposeResolvedText(new Dictionary<int, ResolutionState>
        {
            [0] = new ResolutionState.AcceptBoth(FirstOurs: true, SmartCombine: true),
        });

        // With empty ours, both-ordering is irrelevant; just use theirs.
        composed.Should().Be("a\ntheirs-1\nz\n");
    }

    private static MergeDocument BuildDocWithEmptySide(bool emptyOurs)
    {
        var mergedLines = new[]
        {
            "a",
            "<<<<<<< ours",
            "||||||| base",
            "=======",
            "theirs-1",
            ">>>>>>> theirs",
            "z",
        };
        var range = new ModifiedBaseRange(
            0,
            new LineRange(2, 2),
            new LineRange(2, 2),
            new LineRange(2, 3),
            new LineRange(2, 7),
            Array.Empty<string>(),
            emptyOurs ? Array.Empty<string>() : new[] { "ours-1" },
            new[] { "theirs-1" },
            Array.Empty<DetailedLineRangeMapping>(),
            Array.Empty<DetailedLineRangeMapping>(),
            true,
            false);

        return new MergeDocument(
            "t.txt", string.Empty, string.Empty, string.Empty,
            string.Join("\n", mergedLines) + "\n",
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            mergedLines,
            new[] { range },
            "\n", true);
    }
}
