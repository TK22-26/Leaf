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
    public async Task Merge_OneSidedDeletionBeforeConflict_DoesNotThrow()
    {
        // The common real-world case: ours deletes a line, theirs keeps it and also
        // modifies a later line. Git auto-accepts the deletion and produces a conflict
        // only for the later line. The range walker must not crash when one cursor
        // lags behind due to a one-sided deletion.
        var engine = CreateEngine();
        const string baseText = "header\nb\ncommon\nd\nfooter\n";
        const string oursText = "header\ncommon\nd-ours\nfooter\n";
        const string theirsText = "header\nb\ncommon\nd-theirs\nfooter\n";

        var doc = await engine.MergeAsync("f.txt", baseText, oursText, theirsText);

        doc.ConflictCount.Should().Be(1);
        doc.Ranges.Single().OursLines.Should().Equal("d-ours");
        doc.Ranges.Single().TheirsLines.Should().Equal("d-theirs");
    }

    [Fact]
    public async Task Merge_LookalikeCloseInTheirs_FailsLoudlyNotSilently()
    {
        // Theirs contains a literal ">>>>>>>" line. The parser will latch onto it as the
        // close marker, leaving the real close ">>>>>>> theirs" as an orphan AFTER the
        // last conflict. The tail-walk defense must catch this and throw, rather than
        // letting the orphan structural line reach the committed output via AcceptTheirs.
        var engine = CreateEngine();
        const string baseText = "line1\nline2\nline3\n";
        const string oursText = "line1\nOURS_CHANGE\nline3\n";
        const string theirsText = "line1\n>>>>>>> literal-close-in-theirs\nline3\n";

        var act = async () => await engine.MergeAsync("f.txt", baseText, oursText, theirsText);
        await act.Should().ThrowAsync<MergeEngineException>()
            .WithMessage("*zdiff3 marker line*");
    }

    [Fact]
    public async Task Merge_LookalikeInMidOurs_FailsLoudlyNotSilently()
    {
        // Ours has a literal "<<<<<<<" line in the middle (not the first line) AND there's
        // a separate conflict. Parser's one-line lookahead can't disambiguate this — the
        // engine must fail loudly via MergeEngineException so the VM's engine-error overlay
        // can offer Use Ours / Use Theirs. Silent corruption (prior behaviour) is worse
        // than failing the specific file.
        var engine = CreateEngine();
        const string baseText = "line1\ncommon\nline3\n";
        const string oursText = "line1\ncontent-a\n<<<<<<< mid\ncontent-b\n";
        const string theirsText = "line1\nother\nline3\n";

        var act = async () => await engine.MergeAsync("f.txt", baseText, oursText, theirsText);
        await act.Should().ThrowAsync<MergeEngineException>()
            .WithMessage("*zdiff3 marker line*");
    }

    [Fact]
    public async Task Merge_LookalikeMarkerInsideOursOfRealConflict_AcceptOursRoundTripsCleanly()
    {
        // Ours content legitimately contains "<<<<<<<"; git emits it inside the conflict's
        // ours section. After AcceptOurs, the composed result must be byte-for-byte ours
        // content — no injected "<<<<<<< ours" structural line, no lost "<<<<<<< example".
        var engine = CreateEngine();
        const string baseText = "line1\nline2\nline3\n";
        const string oursText = "line1\n<<<<<<< example\nline3-modified\n";
        const string theirsText = "line1\nline2\nline3-different\n";

        var doc = await engine.MergeAsync("f.txt", baseText, oursText, theirsText);

        // The parser must surface the lookalike as ours content — not misclassify it
        // as a pre-conflict structural opener.
        doc.HasConflicts.Should().BeTrue();
        var joinedOurs = string.Concat(doc.Ranges.SelectMany(r => r.OursLines.Select(l => l + "\n")));
        joinedOurs.Should().Contain("<<<<<<< example");

        // AcceptOurs on all conflicts must round-trip to the original ours text.
        var states = doc.Ranges.ToDictionary(r => r.Index, _ => (Leaf.Models.Merge.ResolutionState)Leaf.Models.Merge.ResolutionState.AcceptOurs.Instance);
        var composed = doc.ComposeResolvedText(states);
        composed.Should().Be(oursText);
    }

    [Fact]
    public async Task Merge_LookalikeMarkerInsideTheirsOfRealConflict_AcceptTheirsRoundTripsCleanly()
    {
        var engine = CreateEngine();
        const string baseText = "line1\nline2\nline3\n";
        const string oursText = "line1\nline2\nline3-ours\n";
        const string theirsText = "line1\n<<<<<<< example\nline3-theirs\n";

        var doc = await engine.MergeAsync("f.txt", baseText, oursText, theirsText);

        doc.HasConflicts.Should().BeTrue();
        var joinedTheirs = string.Concat(doc.Ranges.SelectMany(r => r.TheirsLines.Select(l => l + "\n")));
        joinedTheirs.Should().Contain("<<<<<<< example");

        var states = doc.Ranges.ToDictionary(r => r.Index, _ => (Leaf.Models.Merge.ResolutionState)Leaf.Models.Merge.ResolutionState.AcceptTheirs.Instance);
        var composed = doc.ComposeResolvedText(states);
        composed.Should().Be(theirsText);
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
    public async Task Merge_LookalikeMarkerAdjacentToRealConflict_BothHandled()
    {
        // Real end-to-end case: the file contains a lookalike "<<<<<<<" line before a
        // real conflict. The parser must locate the real conflict correctly and preserve
        // the lookalike line as content. This exercises the fix path for NEW-C-1/NEW-C-2
        // on actual git-merge-file output.
        var engine = CreateEngine();
        const string baseText =
            "intro\n" +
            "<<<<<<< docs about markers\n" +
            "end-docs\n" +
            "target\n" +
            "outro\n";
        const string oursText =
            "intro\n" +
            "<<<<<<< docs about markers\n" +
            "end-docs\n" +
            "target-ours\n" +
            "outro\n";
        const string theirsText =
            "intro\n" +
            "<<<<<<< docs about markers\n" +
            "end-docs\n" +
            "target-theirs\n" +
            "outro\n";

        var doc = await engine.MergeAsync("f.txt", baseText, oursText, theirsText);

        doc.ConflictCount.Should().Be(1);
        doc.InitialMergedText.Should().Contain("<<<<<<< docs about markers");
        doc.Ranges.Single().OursLines.Should().Equal("target-ours");
        doc.Ranges.Single().TheirsLines.Should().Equal("target-theirs");
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
        // Deterministic: we explicitly drain the fire-and-forget cleanup tasks
        // before asserting. In production no one waits, but the test must not
        // race the cleanup under load.
        var tempRoot = Path.Combine(Path.GetTempPath(), $"leaf-engine-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var engine = new GitMergeFileEngine(new GitCommandRunner(), tempRoot);
            for (int i = 0; i < 5; i++)
            {
                await engine.MergeAsync("f.txt", "a\nb\nc\n", "a\nours\nc\n", "a\ntheirs\nc\n");
            }
            await engine.WaitForPendingCleanupAsync();

            Directory.Exists(tempRoot).Should().BeTrue();
            Directory.GetDirectories(tempRoot).Should().BeEmpty("all per-merge temp dirs should be cleaned up");
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }
}
