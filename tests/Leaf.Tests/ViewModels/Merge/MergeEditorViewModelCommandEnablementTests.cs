#nullable enable
using FluentAssertions;
using Leaf.Models;
using Leaf.Models.Merge;
using Leaf.Services.Merge;
using Leaf.Tests.Fakes;
using Leaf.ViewModels.Merge;
using Xunit;

namespace Leaf.Tests.ViewModels.Merge;

/// <summary>
/// Regression tests for command-enablement wiring. The VM uses
/// <c>[RelayCommand(CanExecute = ...)]</c> which does NOT auto-re-evaluate its
/// CanExecute when the gate property changes — it requires explicit
/// <c>Command.NotifyCanExecuteChanged()</c> after every mutation of the gate's
/// dependencies.
/// </summary>
/// <remarks>
/// <para>
/// WPF Buttons subscribe to <see cref="ICommand.CanExecuteChanged"/> and
/// re-query CanExecute when that event fires. If the event doesn't fire, the
/// button's IsEnabled stays at its initial value forever, even if the
/// underlying property flips. That's what tripped the Stagehand smoke test:
/// Mark Resolved property said true, but the button stayed disabled because
/// CanExecuteChanged never fired.
/// </para>
/// <para>
/// These tests subscribe to <see cref="ICommand.CanExecuteChanged"/> directly
/// and assert it fires at least once after a state mutation. Asserting the
/// CanExecute return value is not enough — the property getter is always
/// correct; it's the event plumbing that matters to the button binding.
/// </para>
/// </remarks>
public class MergeEditorViewModelCommandEnablementTests
{
    private static MergeDocument DocWithOneConflict()
    {
        var range = new ModifiedBaseRange(
            Index: 0,
            Base: new LineRange(1, 2),
            Ours: new LineRange(1, 2),
            Theirs: new LineRange(1, 2),
            ResultMarkedRange: new LineRange(1, 6),
            BaseLines: Array.Empty<string>(),
            OursLines: new[] { "ours-line" },
            TheirsLines: new[] { "theirs-line" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);
        return new MergeDocument(
            filePath: "test.txt",
            baseText: string.Empty, oursText: string.Empty, theirsText: string.Empty,
            initialMergedText: "<<<<<<< HEAD\nours-line\n=======\ntheirs-line\n>>>>>>> feature\n",
            baseLines: Array.Empty<string>(),
            oursLines: new[] { "ours-line" },
            theirsLines: new[] { "theirs-line" },
            initialMergedLines: new[] { "<<<<<<< HEAD", "ours-line", "=======", "theirs-line", ">>>>>>> feature" },
            ranges: new[] { range },
            lineEnding: "\n",
            hasTrailingNewline: true);
    }

    private static MergeEditorViewModel CreateVm(MergeDocument? doc)
    {
        var vm = new MergeEditorViewModel(
            new FakeGitService(),
            new FakeClipboardService(),
            new FakeMergeEngine(doc),
            new WordDiffService(),
            aiAssistant: null,
            imageService: null,
            repoPath: "C:/test");
        if (doc is not null)
        {
            typeof(MergeEditorViewModel).GetProperty(nameof(vm.Document))!.SetValue(vm, doc);
            vm.Conflicts.Add(new ConflictInfo { FilePath = "test.txt" });
            vm.SelectedConflict = vm.Conflicts[0];
        }
        return vm;
    }

    [Fact]
    public void MarkResolved_CanExecuteChanged_FiresAfterAcceptOurs()
    {
        // This is the regression that the Stagehand smoke test surfaced:
        // Accept Ours flips the gate, but CanExecuteChanged never fires so
        // the bound button stays disabled. The fix is an explicit
        // NotifyCanExecuteChanged() inside the SetState path.
        var vm = CreateVm(DocWithOneConflict());
        var fired = 0;
        vm.MarkResolvedCommand.CanExecuteChanged += (_, _) => fired++;

        vm.AcceptOursCommand.Execute(0);

        fired.Should().BeGreaterThan(0,
            "MarkResolvedCommand.CanExecuteChanged must fire after a resolution-state " +
            "mutation so the bound button's IsEnabled is re-evaluated");
    }

    [Fact]
    public void MarkResolved_CanExecuteChanged_FiresAfterAcceptTheirs()
    {
        var vm = CreateVm(DocWithOneConflict());
        var fired = 0;
        vm.MarkResolvedCommand.CanExecuteChanged += (_, _) => fired++;
        vm.AcceptTheirsCommand.Execute(0);
        fired.Should().BeGreaterThan(0);
    }

    [Fact]
    public void MarkResolved_CanExecuteChanged_FiresAfterAcceptBoth()
    {
        var vm = CreateVm(DocWithOneConflict());
        var fired = 0;
        vm.MarkResolvedCommand.CanExecuteChanged += (_, _) => fired++;
        vm.AcceptBothCommand.Execute(0);
        fired.Should().BeGreaterThan(0);
    }

    [Fact]
    public void MarkResolved_CanExecuteChanged_FiresAfterAcceptAllOurs()
    {
        var vm = CreateVm(DocWithOneConflict());
        var fired = 0;
        vm.MarkResolvedCommand.CanExecuteChanged += (_, _) => fired++;
        vm.AcceptAllOursCommand.Execute(null);
        fired.Should().BeGreaterThan(0);
    }

    [Fact]
    public void MarkResolved_CanExecuteChanged_FiresAfterUnresolve()
    {
        var vm = CreateVm(DocWithOneConflict());
        vm.AcceptOursCommand.Execute(0); // reach a resolved state first
        var fired = 0;
        vm.MarkResolvedCommand.CanExecuteChanged += (_, _) => fired++;
        vm.UnresolveCommand.Execute(0);
        fired.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Undo_CanExecuteChanged_FiresAfterFirstResolution()
    {
        // Before any state change the undo stack is empty. The first AcceptOurs
        // pushes an entry — CanExecuteChanged must fire so the Undo button
        // enables. Without PushUndo calling NotifyCanExecuteChanged, the
        // button stays disabled forever.
        var vm = CreateVm(DocWithOneConflict());
        var fired = 0;
        vm.UndoCommand.CanExecuteChanged += (_, _) => fired++;
        vm.AcceptOursCommand.Execute(0);
        fired.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Redo_CanExecuteChanged_FiresAfterUndo()
    {
        var vm = CreateVm(DocWithOneConflict());
        vm.AcceptOursCommand.Execute(0);
        var fired = 0;
        vm.RedoCommand.CanExecuteChanged += (_, _) => fired++;
        vm.UndoCommand.Execute(null);
        fired.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CompleteMerge_CanExecuteChanged_FiresWhenFileResolvedCountChanges()
    {
        // Complete Merge gates on CanCompleteMerge = TotalFiles > 0 && ResolvedFiles == TotalFiles.
        // Adding a conflict to Conflicts and marking it resolved must raise
        // CompleteMergeCommand.CanExecuteChanged so the bound button enables.
        var vm = CreateVm(DocWithOneConflict());
        var fired = 0;
        vm.CompleteMergeCommand.CanExecuteChanged += (_, _) => fired++;

        // Simulate the on-disk mark-resolved side effect (flip IsResolved on
        // the selected conflict, then invoke the file-count refresher that
        // MarkResolvedAsync would call).
        vm.Conflicts[0].IsResolved = true;
        typeof(MergeEditorViewModel)
            .GetMethod("NotifyFileCountsChanged",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, null);

        fired.Should().BeGreaterThan(0);
    }

    private sealed class FakeMergeEngine : IMergeEngine
    {
        private readonly MergeDocument? _doc;
        public FakeMergeEngine(MergeDocument? doc) => _doc = doc;
        public Task<MergeDocument> MergeAsync(
            string filePath, string baseText, string oursText, string theirsText,
            bool ignoreWhitespace = false, string? oursLabel = null, string? theirsLabel = null,
            string? baseLabel = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_doc ?? throw new InvalidOperationException("null doc"));
    }
}
