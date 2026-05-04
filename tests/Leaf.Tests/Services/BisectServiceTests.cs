using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.Services;

/// <summary>
/// Pure-logic tests for <see cref="BisectService"/>: the stdout
/// parsers that translate git's verbose status into structured
/// <see cref="Models.BisectResult"/> fields. The CLI-driven paths
/// (start / good / bad / skip / reset) require a real git fixture and
/// live in the integration suite.
/// </summary>
public class BisectServiceTests
{
    [Fact]
    public void ParseFirstBadSha_TerminatingLine_ReturnsSha()
    {
        const string output =
            "abcdef1234567890abcdef1234567890abcdef12 is the first bad commit\n" +
            "commit abcdef1234567890abcdef1234567890abcdef12\n" +
            "Author: Alice <alice@example.com>\n" +
            "Date:   Wed Apr 29 10:00:00 2026 +0000\n" +
            "\n" +
            "    introduce regression\n";

        BisectService.ParseFirstBadSha(output)
            .Should().Be("abcdef1234567890abcdef1234567890abcdef12");
    }

    [Fact]
    public void ParseFirstBadSha_ShortSha_DoesNotMatch()
    {
        // We require the canonical 40-char SHA form git actually emits.
        // Accepting 7+ would let stray short SHAs in commit subjects
        // ("a1b2c3d is the first bad commit" referencing a previous
        // bisect result, for example) false-positive a fresh bisect
        // into early termination.
        BisectService.ParseFirstBadSha("abc1234 is the first bad commit\n")
            .Should().BeNull();
    }

    [Fact]
    public void ParseFirstBadSha_TextOnlyContainsPhrase_DoesNotMatch()
    {
        // A commit whose subject literally contains "is the first bad
        // commit" must not be mistaken for the converging-line marker.
        // We anchor end-of-line and require the SHA at column 0.
        const string output =
            "Bisecting: 4 revisions left to test after this (roughly 2 steps)\n" +
            "[abcdef1234567890abcdef1234567890abcdef12] commit message about how this is the first bad commit fix\n";
        BisectService.ParseFirstBadSha(output).Should().BeNull();
    }

    [Fact]
    public void ParseFirstBadSha_NoTerminator_ReturnsNull()
    {
        // Standard "Bisecting:" progress line — bisect hasn't converged.
        const string output = "Bisecting: 12 revisions left to test after this (roughly 4 steps)\n";
        BisectService.ParseFirstBadSha(output).Should().BeNull();
    }

    [Fact]
    public void ParseFirstBadSha_EmptyOrNull_ReturnsNull()
    {
        BisectService.ParseFirstBadSha(string.Empty).Should().BeNull();
        BisectService.ParseFirstBadSha("\n\n").Should().BeNull();
    }

    [Theory]
    [InlineData("Bisecting: 12 revisions left to test after this (roughly 4 steps)", 4)]
    [InlineData("Bisecting: 1 revisions left to test after this (roughly 1 step)", 1)]
    [InlineData("Bisecting: 0 revisions left to test after this (roughly 0 steps)", 0)]
    public void ParseStepsRemaining_BisectingLine_ReturnsHint(string line, int expected)
    {
        BisectService.ParseStepsRemaining(line).Should().Be(expected);
    }

    [Fact]
    public void ParseStepsRemaining_NoHint_ReturnsNull()
    {
        // First step after `bisect start` doesn't carry the hint
        // because git is still working out the search range.
        BisectService.ParseStepsRemaining("Bisecting: midpoint\n").Should().BeNull();
        BisectService.ParseStepsRemaining(string.Empty).Should().BeNull();
    }

    [Fact]
    public void IsAllSkippedTerminator_RealTerminator_ReturnsTrue()
    {
        // Git's actual terminator string when every remaining candidate
        // was skipped — the bisect is effectively done but not converged.
        const string output =
            "There are only 'skip'ped commits left to test.\n" +
            "The first bad commit could be any of:\n" +
            "abcdef1234567890abcdef1234567890abcdef12\n";
        BisectService.IsAllSkippedTerminator(output).Should().BeTrue();
    }

    [Fact]
    public void IsAllSkippedTerminator_RegularBisect_ReturnsFalse()
    {
        BisectService.IsAllSkippedTerminator(
            "Bisecting: 4 revisions left to test after this (roughly 2 steps)\n")
            .Should().BeFalse();
        BisectService.IsAllSkippedTerminator(string.Empty).Should().BeFalse();
    }

    private const string SampleLog =
        "# bad: [aaa1111aaa1111aaa1111aaa1111aaa1111aaaa] step 8\n" +
        "# good: [ggg1111ggg1111ggg1111ggg1111ggg1111gggg] step 1\n" +
        "git bisect start 'HEAD' 'ggg1111ggg1111ggg1111ggg1111ggg1111gggg'\n" +
        "# good: [ccc3333ccc3333ccc3333ccc3333ccc3333cccc] step 4\n" +
        "git bisect good ccc3333ccc3333ccc3333ccc3333ccc3333cccc\n" +
        "# bad: [bbb2222bbb2222bbb2222bbb2222bbb2222bbbb] step 6\n" +
        "git bisect bad bbb2222bbb2222bbb2222bbb2222bbb2222bbbb\n";

    [Fact]
    public void ParseLog_TwoVerdicts_ReturnsMostRecentFirst()
    {
        var entries = BisectService.ParseLog(SampleLog);

        entries.Should().HaveCount(2);
        entries[0].Verdict.Should().Be(BisectVerdict.Bad);
        entries[0].ShortSha.Should().Be("bbb2222");
        entries[0].Subject.Should().Be("step 6");
        entries[1].Verdict.Should().Be(BisectVerdict.Good);
        entries[1].ShortSha.Should().Be("ccc3333");
        entries[1].Subject.Should().Be("step 4");
    }

    [Fact]
    public void ParseLog_OnlyBookends_ReturnsEmpty()
    {
        // Right after `git bisect start <bad> <good>` and before any
        // verdicts: just bookend comments + the start line.
        const string log =
            "# bad: [aaa] HEAD\n" +
            "# good: [ggg] init\n" +
            "git bisect start 'HEAD' 'ggg'\n";
        BisectService.ParseLog(log).Should().BeEmpty();
    }

    [Fact]
    public void ParseLog_EmptyOrBlank_ReturnsEmpty()
    {
        BisectService.ParseLog(string.Empty).Should().BeEmpty();
        BisectService.ParseLog("\n\n").Should().BeEmpty();
    }

    [Fact]
    public void ParseLog_SkipVerdict_Recognised()
    {
        const string log =
            "# bad: [aaa] HEAD\n" +
            "# good: [ggg] init\n" +
            "git bisect start 'HEAD' 'ggg'\n" +
            "# skip: [sss4444sss4444sss4444sss4444sss4444ssss] step 5\n" +
            "git bisect skip sss4444sss4444sss4444sss4444sss4444ssss\n";

        var entries = BisectService.ParseLog(log);
        entries.Should().HaveCount(1);
        entries[0].Verdict.Should().Be(BisectVerdict.Skip);
        entries[0].ShortSha.Should().Be("sss4444");
    }

    [Fact]
    public void TruncateLastVerdict_DropsLastCommand()
    {
        var truncated = BisectService.TruncateLastVerdict(SampleLog);

        truncated.Should().NotBeNull();
        // The last line of SampleLog is the `git bisect bad bbb…` command;
        // truncated output should NOT contain it but should still contain
        // the comment (so a future audit can see the dropped commit) and
        // the prior verdict's command.
        truncated.Should().NotContain("git bisect bad bbb2222");
        truncated.Should().Contain("git bisect good ccc3333");
        truncated.Should().Contain("git bisect start");
    }

    [Fact]
    public void TruncateLastVerdict_OnlyBookends_ReturnsNull()
    {
        // Nothing to undo when no verdict commands have been issued.
        const string log =
            "# bad: [aaa] HEAD\n" +
            "# good: [ggg] init\n" +
            "git bisect start 'HEAD' 'ggg'\n";
        BisectService.TruncateLastVerdict(log).Should().BeNull();
    }
}
