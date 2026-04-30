using FluentAssertions;
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
    public void ParseFirstBadSha_ShortSha_AlsoMatches()
    {
        // Some git versions print short shas in the terminating line.
        // We accept any 7-40 hex run so the parser doesn't drift the
        // moment git's output formatting changes.
        BisectService.ParseFirstBadSha("abc1234 is the first bad commit\n")
            .Should().Be("abc1234");
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
}
