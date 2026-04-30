using System.IO;
using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.InteractiveRebase;

/// <summary>
/// Plan-serialisation and log-parsing tests for
/// <see cref="InteractiveRebaseService"/>. These exercise the pure logic
/// that builds git's todo grammar and parses <c>git log</c> records, which
/// is the part most likely to drift if anyone touches the format strings.
/// The full end-to-end <c>git rebase</c> run isn't covered here — that
/// requires a real fixture repo and lives in the integration suite once
/// Phase 4 is in place.
/// </summary>
public class InteractiveRebaseServicePlanTests
{
    [Fact]
    public void MaterialisePlan_Pick_EmitsPickLine()
    {
        var dir = NewMessagesDir();
        var (todo, count) = InteractiveRebaseService.MaterialisePlan(
            [Item("abc1234", "first", RebaseTodoAction.Pick)], dir);

        todo.Should().Be("pick abc1234 first\n");
        count.Should().Be(0, because: "pick doesn't queue a message");
    }

    [Fact]
    public void MaterialisePlan_Reword_QueuesNewMessage()
    {
        var dir = NewMessagesDir();
        var (todo, count) = InteractiveRebaseService.MaterialisePlan(
            [Item("abc1234", "old subject", RebaseTodoAction.Reword,
                originalMessage: "old subject\n\nbody",
                newMessage: "new subject\n\nrewritten body")],
            dir);

        todo.Should().Be("reword abc1234 old subject\n");
        count.Should().Be(1);
        File.ReadAllText(Path.Combine(dir, "0001.msg"))
            .Should().Be("new subject\n\nrewritten body");
    }

    [Fact]
    public void MaterialisePlan_RewordWithoutNewMessage_FallsBackToOriginal()
    {
        // Common case: user toggled `reword` but hasn't typed a replacement
        // yet. We honour the toggle by queueing the original message — the
        // helper still gets a message file, the rebase pauses on the user
        // for confirmation, and an empty NewMessage doesn't silently turn
        // into an empty commit message.
        var dir = NewMessagesDir();
        var (todo, count) = InteractiveRebaseService.MaterialisePlan(
            [Item("abc1234", "subj", RebaseTodoAction.Reword,
                originalMessage: "subj\n\noriginal body",
                newMessage: null)],
            dir);

        todo.Should().Be("reword abc1234 subj\n");
        count.Should().Be(1);
        File.ReadAllText(Path.Combine(dir, "0001.msg"))
            .Should().Be("subj\n\noriginal body");
    }

    [Fact]
    public void MaterialisePlan_FixupAndDrop_NoMessageQueued()
    {
        var dir = NewMessagesDir();
        var (todo, count) = InteractiveRebaseService.MaterialisePlan(
            [
                Item("a", "first", RebaseTodoAction.Pick),
                Item("b", "tweaked", RebaseTodoAction.Fixup),
                Item("c", "junk", RebaseTodoAction.Drop),
            ],
            dir);

        todo.Should().Be("pick a first\nfixup b tweaked\ndrop c junk\n");
        count.Should().Be(0);
    }

    [Fact]
    public void MaterialisePlan_Squash_QueuesUserMessageWhenProvided()
    {
        var dir = NewMessagesDir();
        var (todo, count) = InteractiveRebaseService.MaterialisePlan(
            [
                Item("a", "first", RebaseTodoAction.Pick),
                Item("b", "tweaked", RebaseTodoAction.Squash,
                    newMessage: "combined: first + tweaked"),
            ],
            dir);

        todo.Should().Be("pick a first\nsquash b tweaked\n");
        count.Should().Be(1);
        File.ReadAllText(Path.Combine(dir, "0001.msg"))
            .Should().Be("combined: first + tweaked");
    }

    [Fact]
    public void MaterialisePlan_SquashWithoutNewMessage_QueuesEmptyPassthrough()
    {
        // The helper treats an empty queue file as "leave git's
        // pre-loaded combined message in COMMIT_EDITMSG alone." Writing
        // the squashed commit's OriginalMessage here would silently
        // delete the preceding commit's text from the merged result —
        // the bug this regression test pins.
        var dir = NewMessagesDir();
        var (todo, count) = InteractiveRebaseService.MaterialisePlan(
            [
                Item("a", "first", RebaseTodoAction.Pick),
                Item("b", "tweaked", RebaseTodoAction.Squash,
                    originalMessage: "tweaked\n\nshould NOT replace combined default"),
            ],
            dir);

        todo.Should().Be("pick a first\nsquash b tweaked\n");
        count.Should().Be(1);
        File.ReadAllText(Path.Combine(dir, "0001.msg")).Should().BeEmpty();
    }

    [Fact]
    public void MaterialisePlan_Edit_DoesNotQueueMessage()
    {
        // `edit` stops the rebase so the user can amend manually. Git only
        // pops the editor when they run `git commit --amend`, which we
        // don't drive — Leaf's amend path lives elsewhere.
        var dir = NewMessagesDir();
        var (todo, count) = InteractiveRebaseService.MaterialisePlan(
            [Item("a", "tweak me", RebaseTodoAction.Edit)], dir);

        todo.Should().Be("edit a tweak me\n");
        count.Should().Be(0);
    }

    [Fact]
    public void MaterialisePlan_Exec_RequiresCommand()
    {
        var dir = NewMessagesDir();
        FluentActions.Invoking(() => InteractiveRebaseService.MaterialisePlan(
            [new RebaseTodoItem
                {
                    Sha = string.Empty,
                    ShortSha = string.Empty,
                    Subject = string.Empty,
                    Action = RebaseTodoAction.Exec,
                    ExecCommand = null,
                }],
            dir))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Exec entry with no command*");
    }

    [Fact]
    public void MaterialisePlan_MultipleRewordsNumberInOrder()
    {
        var dir = NewMessagesDir();
        var (todo, count) = InteractiveRebaseService.MaterialisePlan(
            [
                Item("a", "first",  RebaseTodoAction.Reword, newMessage: "First!"),
                Item("b", "second", RebaseTodoAction.Pick),
                Item("c", "third",  RebaseTodoAction.Reword, newMessage: "Third!"),
            ],
            dir);

        todo.Should().Be("reword a first\npick b second\nreword c third\n");
        count.Should().Be(2);
        File.ReadAllText(Path.Combine(dir, "0001.msg")).Should().Be("First!");
        File.ReadAllText(Path.Combine(dir, "0002.msg")).Should().Be("Third!");
    }

    [Fact]
    public void ParseLogRecords_RoundTripsFiveFieldsPerRecord()
    {
        // The format string in InteractiveRebaseService is:
        //   %H %x1F %h %x1F "name <email>" %x1F %aI %x1F %B %x1E
        // Mirror it here so a drifting format string would break this test.
        var record1 =
            "abcdef1234567890abcdef1Alice <a@x>2026-04-29T10:00:00ZFirst line\n\nBody.";
        var record2 =
            "1234567890abcdef1234567Bob <b@x>2026-04-29T11:00:00ZSecond commit";

        var items = InteractiveRebaseService.ParseLogRecords(record1 + record2);

        items.Should().HaveCount(2);
        items[0].Sha.Should().Be("abcdef1234567890");
        items[0].ShortSha.Should().Be("abcdef1");
        items[0].Author.Should().Be("Alice <a@x>");
        items[0].Subject.Should().Be("First line");
        items[0].OriginalMessage.Should().Be("First line\n\nBody.");
        items[0].Action.Should().Be(RebaseTodoAction.Pick);
        items[1].Subject.Should().Be("Second commit");
    }

    [Fact]
    public void ParseLogRecords_EmptyOutput_ReturnsEmptyList()
    {
        InteractiveRebaseService.ParseLogRecords(string.Empty).Should().BeEmpty();
        InteractiveRebaseService.ParseLogRecords("\n").Should().BeEmpty();
    }

    // Path-shell normalisation lives on RebaseHelperResolver and is
    // covered by RebaseHelperResolverTests — no duplicate assertion here.

    private static RebaseTodoItem Item(
        string sha, string subject, RebaseTodoAction action,
        string? originalMessage = null, string? newMessage = null)
    {
        return new RebaseTodoItem
        {
            Sha = sha,
            ShortSha = sha.Length >= 7 ? sha[..7] : sha,
            Subject = subject,
            OriginalMessage = originalMessage ?? subject,
            Action = action,
            NewMessage = newMessage,
        };
    }

    private static string NewMessagesDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "leaf-rebase-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
