#nullable enable
using System.Threading;
using FluentAssertions;
using Leaf.Models.Merge;
using Leaf.Services.Merge;
using Leaf.Tests.Fakes;
using Leaf.ViewModels.Merge;
using Xunit;

namespace Leaf.Tests.ViewModels.Merge;

/// <summary>
/// Tests for the C6 conflict-note flow: the Note property round-trips on
/// every ResolutionState variant, AddNoteCommand attaches / clears notes
/// without disturbing the existing resolution, and BuildMergeCommitMessageFromNotes
/// emits a well-formed commit body the user can copy to a PR description.
/// </summary>
public class MergeEditorViewModelNotesTests
{
    [Fact]
    public void ResolutionState_NoteRoundtrips_OnEveryVariant()
    {
        // Records compare by value including init-only properties, so two
        // states differing only by Note must compare unequal. Without this
        // the Undo stack would collapse note-edit transitions and lose the
        // user's input on redo.
        var ours = ResolutionState.AcceptOurs.Instance with { Note = "alpha" };
        ours.Note.Should().Be("alpha");
        ours.Should().NotBe(ResolutionState.AcceptOurs.Instance);

        var theirs = ResolutionState.AcceptTheirs.Instance with { Note = "beta" };
        theirs.Note.Should().Be("beta");

        var both = new ResolutionState.AcceptBoth(FirstOurs: true, SmartCombine: true) with { Note = "gamma" };
        both.Note.Should().Be("gamma");
        both.FirstOurs.Should().BeTrue();

        var manual = new ResolutionState.Manual("custom") with { Note = "delta" };
        manual.Note.Should().Be("delta");
        manual.Text.Should().Be("custom");

        var unresolved = ResolutionState.Unresolved.Instance with { Note = "epsilon" };
        unresolved.Note.Should().Be("epsilon");
    }

    [Fact]
    public void AddNote_OnExistingResolution_PreservesVariantAndCopiesNote()
    {
        var vm = CreateVmWithDocument();
        vm.RangeStates[0] = ResolutionState.AcceptOurs.Instance;

        vm.AddNoteCommand.Execute((0, "needs review"));

        vm.RangeStates[0].Should().BeOfType<ResolutionState.AcceptOurs>(
            because: "the note must not flip a resolved range to unresolved");
        vm.RangeStates[0].Note.Should().Be("needs review");
    }

    [Fact]
    public void AddNote_OnUnresolvedRange_CreatesUnresolvedEntryWithNote()
    {
        var vm = CreateVmWithDocument();
        // No entry in RangeStates == canonical "unresolved".
        vm.RangeStates.ContainsKey(0).Should().BeFalse();

        vm.AddNoteCommand.Execute((0, "explain when fixing"));

        vm.RangeStates.ContainsKey(0).Should().BeTrue();
        vm.RangeStates[0].Should().BeOfType<ResolutionState.Unresolved>();
        vm.RangeStates[0].Note.Should().Be("explain when fixing");
    }

    [Fact]
    public void AddNote_EmptyText_OnUnresolvedRange_ClearsEntry()
    {
        var vm = CreateVmWithDocument();
        vm.RangeStates[0] = ResolutionState.Unresolved.Instance with { Note = "old" };

        vm.AddNoteCommand.Execute((0, "   "));

        vm.RangeStates.ContainsKey(0).Should().BeFalse(
            because: "bare Unresolved with no note collapses back to 'no entry'");
    }

    [Fact]
    public void AddNote_EmptyText_OnResolvedRange_StripsNoteButKeepsResolution()
    {
        var vm = CreateVmWithDocument();
        vm.RangeStates[0] = ResolutionState.AcceptTheirs.Instance with { Note = "old" };

        vm.AddNoteCommand.Execute((0, null));

        vm.RangeStates[0].Should().BeOfType<ResolutionState.AcceptTheirs>();
        vm.RangeStates[0].Note.Should().BeNull();
    }

    [Fact]
    public void BuildMergeCommitMessageFromNotes_Empty_WhenNoNotes()
    {
        var vm = CreateVmWithDocument();
        vm.RangeStates[0] = ResolutionState.AcceptOurs.Instance;

        vm.BuildMergeCommitMessageFromNotes().Should().BeEmpty();
    }

    [Fact]
    public void BuildMergeCommitMessageFromNotes_Formats_EveryNonEmptyNote_InIndexOrder()
    {
        var vm = CreateVmWithDocument();
        vm.RangeStates[2] = ResolutionState.AcceptOurs.Instance with { Note = "second" };
        vm.RangeStates[0] = ResolutionState.AcceptTheirs.Instance with { Note = "first" };
        vm.RangeStates[4] = ResolutionState.AcceptOurs.Instance; // no note

        var message = vm.BuildMergeCommitMessageFromNotes();

        message.Should().Contain("Conflict notes:");
        // Index-ordered output: range 0 before range 2. "First" vs "second"
        // correspondingly.
        var firstIdx = message.IndexOf("first", StringComparison.Ordinal);
        var secondIdx = message.IndexOf("second", StringComparison.Ordinal);
        firstIdx.Should().BeGreaterThan(0);
        secondIdx.Should().BeGreaterThan(firstIdx,
            because: "notes must render in range-index order so the commit body is deterministic");
        // Range 4 had no note; no "range 5" or empty bullet in output.
        message.Should().NotContain("- #5:");
    }

    [Fact]
    public void AddNote_SupportsUndoRedo()
    {
        var vm = CreateVmWithDocument();
        vm.RangeStates[0] = ResolutionState.AcceptOurs.Instance;

        vm.AddNoteCommand.Execute((0, "first draft"));
        vm.RangeStates[0].Note.Should().Be("first draft");
        // Explicit CanUndo/CanRedo asserts pin the undo-stack wiring —
        // without them the state-only checks below would pass even if
        // PushUndo were accidentally removed from AddNote.
        vm.CanUndo.Should().BeTrue(because: "AddNote must push onto the undo stack");

        vm.UndoCommand.Execute(null);
        vm.RangeStates[0].Note.Should().BeNull(
            because: "Undo must roll back the AddNote transition");
        vm.CanRedo.Should().BeTrue(because: "undoing leaves the entry on the redo stack");

        vm.RedoCommand.Execute(null);
        vm.RangeStates[0].Note.Should().Be("first draft");
    }

    private static MergeEditorViewModel CreateVmWithDocument()
    {
        var doc = new MergeDocument(
            "f.cs", "", "", "", "",
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<ModifiedBaseRange>(), "\n", true);
        var vm = new MergeEditorViewModel(
            new FakeGitService(), new FakeClipboardService(),
            new FakeMergeEngine(doc), "C:/test");
        typeof(MergeEditorViewModel).GetProperty(nameof(vm.Document))!.SetValue(vm, doc);
        return vm;
    }

    private sealed class FakeMergeEngine : IMergeEngine
    {
        private readonly MergeDocument _doc;
        public FakeMergeEngine(MergeDocument doc) => _doc = doc;
        public Task<MergeDocument> MergeAsync(
            string filePath, string baseText, string oursText, string theirsText,
            bool ignoreWhitespace = false, string? oursLabel = null, string? theirsLabel = null,
            string? baseLabel = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_doc);
    }
}
