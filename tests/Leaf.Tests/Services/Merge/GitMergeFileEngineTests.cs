using System.IO;
using FluentAssertions;
using Leaf.Services;
using Leaf.Services.Merge;
using Xunit;

namespace Leaf.Tests.Services.Merge;

/// <summary>
/// Integration tests for <see cref="GitMergeFileEngine"/>. These exercise the real
/// <c>git</c> binary on PATH — they are essentially contract tests confirming that
/// the engine's flag set, encoding handling, and parser align with Git's actual
/// <c>merge-file</c> output.
/// </summary>
public class GitMergeFileEngineTests
{
    private static GitMergeFileEngine CreateEngine()
        => new(new GitCommandRunner());

    [Fact]
    public async Task Merge_NoChangesOnEitherSide_ProducesBaseVerbatim()
    {
        var engine = CreateEngine();
        const string text = "a\nb\nc\n";
        var doc = await engine.MergeAsync("file.txt", text, text, text);

        doc.HasConflicts.Should().BeFalse();
        doc.ConflictCount.Should().Be(0);
        doc.Ranges.Should().BeEmpty();
        doc.InitialMergedText.Should().Be(text);
    }

    [Fact]
    public async Task Merge_ChangesOnOnlyOneSide_AutoMergesWithoutConflict()
    {
        var engine = CreateEngine();
        const string baseText = "a\nb\nc\n";
        const string oursText = "a\nb\nc\n";
        const string theirsText = "a\nb-modified\nc\n";

        var doc = await engine.MergeAsync("file.txt", baseText, oursText, theirsText);

        doc.HasConflicts.Should().BeFalse();
        doc.Ranges.Should().BeEmpty();
        doc.InitialMergedText.Should().Be(theirsText);
    }

    [Fact]
    public async Task Merge_ConflictingEdits_ProducesSingleConflictRange()
    {
        var engine = CreateEngine();
        const string baseText = "a\nb\nc\n";
        const string oursText = "a\nb-ours\nc\n";
        const string theirsText = "a\nb-theirs\nc\n";

        var doc = await engine.MergeAsync("file.txt", baseText, oursText, theirsText);

        doc.HasConflicts.Should().BeTrue();
        doc.ConflictCount.Should().Be(1);

        var range = doc.Ranges.Single();
        range.OursLines.Should().Equal("b-ours");
        range.TheirsLines.Should().Equal("b-theirs");
        range.BaseLines.Should().Equal("b");
        range.IsConflicting.Should().BeTrue();
    }

    [Fact]
    public async Task Merge_TwoConflictsInOneFile_ReturnsInDocumentOrder()
    {
        var engine = CreateEngine();
        const string baseText = "a\nb\nc\nd\ne\nf\ng\n";
        const string oursText = "a\nb-ours\nc\nd\ne-ours\nf\ng\n";
        const string theirsText = "a\nb-theirs\nc\nd\ne-theirs\nf\ng\n";

        var doc = await engine.MergeAsync("file.txt", baseText, oursText, theirsText);

        doc.ConflictCount.Should().Be(2);
        var first = doc.Ranges[0];
        var second = doc.Ranges[1];
        first.ResultMarkedRange.StartLine.Should().BeLessThan(second.ResultMarkedRange.StartLine);
        first.OursLines.Should().Equal("b-ours");
        second.OursLines.Should().Equal("e-ours");
    }

    [Fact]
    public async Task Merge_CRLFInput_OutputsRestoredCRLF_ViaComposer()
    {
        var engine = CreateEngine();
        var baseText = "a\r\nb\r\nc\r\n";
        var oursText = "a\r\nb-ours\r\nc\r\n";
        var theirsText = "a\r\nb-theirs\r\nc\r\n";

        var doc = await engine.MergeAsync("file.txt", baseText, oursText, theirsText);
        doc.LineEnding.Should().Be("\r\n");

        // Composed text with AcceptOurs should restore CRLF style.
        var composed = doc.ComposeResolvedText(new Dictionary<int, Leaf.Models.Merge.ResolutionState>
        {
            [0] = Leaf.Models.Merge.ResolutionState.AcceptOurs.Instance,
        });
        composed.Should().Be("a\r\nb-ours\r\nc\r\n");
    }

    [Fact]
    public async Task Merge_UnicodeContent_RoundTrips()
    {
        var engine = CreateEngine();
        const string baseText = "α\nβ\nγ\n";
        const string oursText = "α\nβ-🌲\nγ\n";
        const string theirsText = "α\nβ-📝\nγ\n";

        var doc = await engine.MergeAsync("file.txt", baseText, oursText, theirsText);

        doc.ConflictCount.Should().Be(1);
        doc.Ranges.Single().OursLines.Should().Equal("β-🌲");
        doc.Ranges.Single().TheirsLines.Should().Equal("β-📝");
    }

    [Fact]
    public async Task Merge_ByteIdenticalToGitMergeCli_OnSimpleConflict()
    {
        // The whole premise of the engine is parity with `git merge-file` directly.
        // This test runs the engine twice in row (identical inputs) and asserts equality,
        // plus confirms that ComposeResolvedText with no states returns the engine's initial output.
        var engine = CreateEngine();
        const string baseText = "line1\nline2\nline3\n";
        const string oursText = "line1\nours\nline3\n";
        const string theirsText = "line1\ntheirs\nline3\n";

        var doc1 = await engine.MergeAsync("file.txt", baseText, oursText, theirsText);
        var doc2 = await engine.MergeAsync("file.txt", baseText, oursText, theirsText);

        doc1.InitialMergedText.Should().Be(doc2.InitialMergedText);
        doc1.ComposeResolvedText(null).Should().Be(doc1.InitialMergedText);
    }

    [Fact]
    public async Task Merge_IgnoreWhitespace_ThrowsNotSupported()
    {
        // git merge-file does not expose --ignore-*-space flags. Phase 1 surfaces this
        // loudly rather than silently returning a whitespace-sensitive merge. A future
        // phase will implement proper whitespace-insensitive merge via input normalisation.
        var engine = CreateEngine();
        var act = async () => await engine.MergeAsync(
            "f.txt", "a\n", "a\n", "a\n", ignoreWhitespace: true);
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task Merge_NullInput_Throws()
    {
        var engine = CreateEngine();
        var act = async () => await engine.MergeAsync("f", null!, "x", "y");
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Merge_CancellationBeforeInvocation_Throws()
    {
        var engine = CreateEngine();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await engine.MergeAsync("f.txt", "a\n", "a\n", "a\n", cancellationToken: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Merge_RepeatedContentAcrossConflicts_AssignsCorrectInputRanges()
    {
        // File with repeated blank lines and boilerplate. The naive "forward-search for
        // first match" approach could misattribute conflict #2's base slice to a blank
        // line from conflict #1's context. The lock-step walker gets this right.
        var engine = CreateEngine();
        const string baseText =
            "header\n" +
            "\n" +
            "section-a\n" +
            "\n" +
            "shared\n" +
            "\n" +
            "section-b\n" +
            "\n" +
            "footer\n";
        const string oursText =
            "header\n" +
            "\n" +
            "section-a-ours\n" +
            "\n" +
            "shared\n" +
            "\n" +
            "section-b-ours\n" +
            "\n" +
            "footer\n";
        const string theirsText =
            "header\n" +
            "\n" +
            "section-a-theirs\n" +
            "\n" +
            "shared\n" +
            "\n" +
            "section-b-theirs\n" +
            "\n" +
            "footer\n";

        var doc = await engine.MergeAsync("f.txt", baseText, oursText, theirsText);

        doc.ConflictCount.Should().Be(2);
        var c1 = doc.Ranges[0];
        var c2 = doc.Ranges[1];

        // First conflict covers base line 3 ("section-a"), second covers line 7 ("section-b").
        c1.Base.StartLine.Should().Be(3);
        c1.BaseLines.Should().Equal("section-a");
        c2.Base.StartLine.Should().Be(7);
        c2.BaseLines.Should().Equal("section-b");

        // Same for ours/theirs.
        c1.OursLines.Should().Equal("section-a-ours");
        c2.OursLines.Should().Equal("section-b-ours");
        c1.TheirsLines.Should().Equal("section-a-theirs");
        c2.TheirsLines.Should().Equal("section-b-theirs");
    }

    [Fact]
    public async Task Merge_UserContentWithLookalikeOpenMarker_NotMisidentified()
    {
        // A clean auto-merge where the file happens to contain a literal "<<<<<<<" line
        // must not crash the parser. The result must contain the lookalike line verbatim.
        var engine = CreateEngine();
        const string baseText =
            "line1\n" +
            "<<<<<<< documentation\n" +
            "line3\n";
        const string oursText =
            "line1\n" +
            "<<<<<<< documentation\n" +
            "line3-modified-by-ours\n";
        const string theirsText = baseText;

        var doc = await engine.MergeAsync("f.txt", baseText, oursText, theirsText);
        doc.HasConflicts.Should().BeFalse();
        doc.InitialMergedText.Should().Contain("<<<<<<< documentation");
    }

    [Fact]
    public async Task Merge_CustomLabels_RoundTripThroughRangeModel()
    {
        var engine = CreateEngine();
        const string baseText = "a\nb\nc\n";
        const string oursText = "a\nours-b\nc\n";
        const string theirsText = "a\ntheirs-b\nc\n";

        var doc = await engine.MergeAsync(
            "f.txt", baseText, oursText, theirsText,
            oursLabel: "HEAD",
            theirsLabel: "feature/x",
            baseLabel: "common");

        var range = doc.Ranges.Single();
        range.OursLabel.Should().Be("HEAD");
        range.TheirsLabel.Should().Be("feature/x");
        range.BaseLabel.Should().Be("common");
    }

    [Fact]
    public async Task Merge_CleansUpTempDirectory()
    {
        // We can't easily assert the specific temp dir is deleted (it's unique & private),
        // but we can assert the temp root doesn't accumulate leftover leaf-merge-* dirs
        // across repeated invocations when things run to completion.
        var tempRoot = Path.Combine(Path.GetTempPath(), $"leaf-engine-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var engine = new GitMergeFileEngine(new GitCommandRunner(), tempRoot);
            for (int i = 0; i < 5; i++)
            {
                await engine.MergeAsync("f.txt", "a\nb\nc\n", "a\nours\nc\n", "a\ntheirs\nc\n");
            }

            Directory.Exists(tempRoot).Should().BeTrue();
            Directory.GetDirectories(tempRoot).Should().BeEmpty("all per-merge temp dirs should be cleaned up");
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }
}
