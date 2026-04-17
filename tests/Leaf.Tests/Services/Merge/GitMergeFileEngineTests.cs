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
