using FluentAssertions;
using Leaf.Models;
using Leaf.Services.Git.Operations;
using Xunit;

namespace Leaf.Tests.Services.Git;

/// <summary>
/// Unit tests for the <c>git reflog</c> parser and operation-type
/// classifier. The live CLI path is exercised via manual smoke
/// testing; these lock down the string shapes git produces so a
/// future git-version tweak or an unusual user config stays visible.
/// </summary>
public class ReflogOperationsTests
{
    // ---- ParseReflogOutput ----------------------------------------------

    [Fact]
    public void ParseReflogOutput_Empty_ReturnsEmpty()
    {
        SubmoduleCheck(ReflogOperations.ParseReflogOutput(""));
        SubmoduleCheck(ReflogOperations.ParseReflogOutput("   \n\t  \n"));

        static void SubmoduleCheck(List<ReflogEntry> result) => result.Should().BeEmpty();
    }

    [Fact]
    public void ParseReflogOutput_SingleCommitLine_ParsesAllFields()
    {
        const string line = "abc1234def5678abcdef1234567890abcdef1234\tHEAD@{2026-04-17 11:10:44 -0400}\tcommit: Initial commit\n";

        var result = ReflogOperations.ParseReflogOutput(line);

        result.Should().HaveCount(1);
        var entry = result[0];
        entry.Sha.Should().Be("abc1234def5678abcdef1234567890abcdef1234");
        entry.ShortSha.Should().Be("abc1234");
        entry.Ref.Should().Be("HEAD");
        entry.OperationType.Should().Be(ReflogOperationType.Commit);
        entry.Message.Should().Be("commit: Initial commit");
        entry.Timestamp.Year.Should().Be(2026);
        entry.Timestamp.Month.Should().Be(4);
        entry.Timestamp.Day.Should().Be(17);
        entry.Timestamp.Hour.Should().Be(11);
        entry.Timestamp.Offset.Should().Be(TimeSpan.FromHours(-4));
    }

    [Fact]
    public void ParseReflogOutput_BranchRef_ParsesRefPortionOnly()
    {
        const string line = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\trefs/heads/feature/foo@{2026-04-17 10:00:00 +0000}\tcommit: message\n";

        var result = ReflogOperations.ParseReflogOutput(line);

        result[0].Ref.Should().Be("refs/heads/feature/foo");
    }

    [Fact]
    public void ParseReflogOutput_SubjectContainsTab_PreservesFullMessage()
    {
        // A contributor writing a tab into a commit subject is unusual
        // but legal — the parser must rejoin the trailing tabs rather
        // than truncate.
        const string line = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\tHEAD@{2026-04-17 10:00:00 +0000}\tcommit: before\tafter\n";

        var result = ReflogOperations.ParseReflogOutput(line);

        result[0].Message.Should().Be("commit: before\tafter");
    }

    [Fact]
    public void ParseReflogOutput_MultipleLines_OrderPreserved()
    {
        const string output =
            "1111111111111111111111111111111111111111\tHEAD@{2026-04-17 10:00:00 +0000}\tcommit: first\n" +
            "2222222222222222222222222222222222222222\tHEAD@{2026-04-17 09:00:00 +0000}\treset: moving to HEAD~1\n" +
            "3333333333333333333333333333333333333333\trefs/heads/main@{2026-04-17 08:00:00 +0000}\tcommit: earlier\n";

        var result = ReflogOperations.ParseReflogOutput(output);

        result.Should().HaveCount(3);
        result.Select(e => e.OperationType).Should().Equal(
            ReflogOperationType.Commit,
            ReflogOperationType.Reset,
            ReflogOperationType.Commit);
    }

    [Fact]
    public void ParseReflogOutput_MalformedLine_SkippedNotThrown()
    {
        const string output =
            "garbage-no-tabs\n" +
            "1111111111111111111111111111111111111111\tHEAD@{2026-04-17 10:00:00 +0000}\tcommit: valid\n";

        var result = ReflogOperations.ParseReflogOutput(output);

        result.Should().HaveCount(1);
        result[0].Message.Should().Be("commit: valid");
    }

    [Fact]
    public void ParseReflogOutput_UnparseableTimestamp_SkippedNotThrown()
    {
        const string output =
            "1111111111111111111111111111111111111111\tHEAD@{not-a-date}\tcommit: broken\n" +
            "2222222222222222222222222222222222222222\tHEAD@{2026-04-17 10:00:00 +0000}\tcommit: good\n";

        var result = ReflogOperations.ParseReflogOutput(output);

        result.Should().HaveCount(1);
        result[0].Sha.Should().StartWith("22");
    }

    [Fact]
    public void ParseReflogOutput_SelectorWithoutBraces_SkippedNotThrown()
    {
        // If git ever changes the selector format, drop the row and
        // move on rather than crashing the view.
        const string output =
            "1111111111111111111111111111111111111111\tHEAD-no-braces\tcommit: broken\n" +
            "2222222222222222222222222222222222222222\tHEAD@{2026-04-17 10:00:00 +0000}\tcommit: good\n";

        var result = ReflogOperations.ParseReflogOutput(output);

        result.Should().HaveCount(1);
    }

    [Fact]
    public void ParseReflogOutput_ShortShaProperty_First7Chars()
    {
        const string line = "abcdef0123456789abcdef0123456789abcdef01\tHEAD@{2026-04-17 10:00:00 +0000}\tcommit: x\n";

        var result = ReflogOperations.ParseReflogOutput(line);

        result[0].ShortSha.Should().Be("abcdef0");
    }

    [Fact]
    public void ParseReflogOutput_MalformedSha_Skipped()
    {
        // The format flag pins SHAs to full 40-char hex; anything
        // else is garbage that would otherwise propagate into
        // CheckoutCommitAsync and produce a confusing git error
        // downstream.
        const string output =
            "not-a-sha\tHEAD@{2026-04-17 10:00:00 +0000}\tcommit: bad\n" +
            "abc\tHEAD@{2026-04-17 10:00:00 +0000}\tcommit: too short\n" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\tHEAD@{2026-04-17 10:00:00 +0000}\tcommit: uppercase\n" +
            "1111111111111111111111111111111111111111\tHEAD@{2026-04-17 10:00:00 +0000}\tcommit: good\n";

        var result = ReflogOperations.ParseReflogOutput(output);

        result.Should().HaveCount(1);
        result[0].Sha.Should().Be("1111111111111111111111111111111111111111");
    }

    // ---- ClassifyMessage ------------------------------------------------
    //
    // Locks the prefix → operation-type mapping so the filter dropdown
    // and icon column stay accurate against real reflog messages.

    [Theory]
    [InlineData("commit: Regular commit", ReflogOperationType.Commit)]
    [InlineData("commit (initial): Initial commit", ReflogOperationType.Commit)]
    [InlineData("commit (amend): Amended commit", ReflogOperationType.Amend)]
    [InlineData("checkout: moving from develop to main", ReflogOperationType.Checkout)]
    [InlineData("reset: moving to HEAD~3", ReflogOperationType.Reset)]
    [InlineData("merge feature/foo: Merge made by the 'ort' strategy.", ReflogOperationType.Merge)]
    [InlineData("rebase (start): checkout develop", ReflogOperationType.Rebase)]
    [InlineData("rebase (pick): some commit", ReflogOperationType.Rebase)]
    [InlineData("rebase -i (finish): returning to refs/heads/main", ReflogOperationType.Rebase)]
    [InlineData("cherry-pick: Some commit", ReflogOperationType.CherryPick)]
    [InlineData("revert: Undo commit", ReflogOperationType.Revert)]
    [InlineData("pull: Fast-forward", ReflogOperationType.Pull)]
    [InlineData("push", ReflogOperationType.Push)]
    [InlineData("clone: from https://example.com/repo.git", ReflogOperationType.Clone)]
    [InlineData("branch: Created from HEAD", ReflogOperationType.Branch)]
    [InlineData("stash: WIP on main", ReflogOperationType.Stash)]
    [InlineData("", ReflogOperationType.Other)]
    [InlineData("some-future-git-operation: details", ReflogOperationType.Other)]
    public void ClassifyMessage_KnownPrefixes(string subject, ReflogOperationType expected)
    {
        ReflogOperations.ClassifyMessage(subject).Should().Be(expected);
    }
}
