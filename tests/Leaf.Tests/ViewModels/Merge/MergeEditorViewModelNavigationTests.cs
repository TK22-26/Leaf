using FluentAssertions;
using Leaf.Models;
using Leaf.Models.Merge;
using Leaf.Services;
using Leaf.Services.Merge;
using Leaf.Tests.Fakes;
using Leaf.ViewModels.Merge;
using Xunit;

namespace Leaf.Tests.ViewModels.Merge;

/// <summary>
/// VM-level tests for Phase 4 keyboard-navigation commands: Next/Previous
/// conflict + AcceptCurrent* routing. Uses a fake engine so the tests don't
/// shell out to real git.
/// </summary>
public class MergeEditorViewModelNavigationTests
{
    private static MergeDocument DocWithRanges(int conflictCount)
    {
        // Build an in-memory MergeDocument with `conflictCount` conflicting ranges
        // interleaved with context lines. We don't go through the engine; we
        // construct the document directly because the Phase 4 navigation logic
        // only depends on Ranges[].IsConflicting.
        var ranges = new List<ModifiedBaseRange>(conflictCount);
        for (int i = 0; i < conflictCount; i++)
        {
            ranges.Add(new ModifiedBaseRange(
                Index: i,
                Base: new LineRange(i + 1, i + 2),
                Ours: new LineRange(i + 1, i + 2),
                Theirs: new LineRange(i + 1, i + 2),
                ResultMarkedRange: new LineRange(i + 1, i + 8),
                BaseLines: new[] { $"base-{i}" },
                OursLines: new[] { $"ours-{i}" },
                TheirsLines: new[] { $"theirs-{i}" },
                OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
                TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
                IsConflicting: true,
                IsOrderRelevant: true));
        }
        return new MergeDocument(
            "test.txt", "", "", "", "",
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), ranges, "\n", true);
    }

    private static MergeEditorViewModel CreateVm(MergeDocument doc)
    {
        var vm = new MergeEditorViewModel(
            new FakeGitService(),
            new FakeClipboardService(),
            new FakeMergeEngine(doc),
            repoPath: "C:/test");
        typeof(MergeEditorViewModel).GetProperty(nameof(vm.Document))!.SetValue(vm, doc);
        return vm;
    }

    [Fact]
    public void NextConflict_WithNoDocument_IsNoOp()
    {
        var vm = new MergeEditorViewModel(
            new FakeGitService(), new FakeClipboardService(),
            new FakeMergeEngine(null), "C:/test");
        vm.Invoking(v => v.NextConflictCommand.Execute(null)).Should().NotThrow();
    }

    [Fact]
    public void NextConflict_CyclesAndWrapsAround()
    {
        var vm = CreateVm(DocWithRanges(3));
        vm.CurrentConflictIndex.Should().Be(0);
        vm.NextConflictCommand.Execute(null);
        vm.CurrentConflictIndex.Should().Be(1);
        vm.NextConflictCommand.Execute(null);
        vm.CurrentConflictIndex.Should().Be(2);
        vm.NextConflictCommand.Execute(null);
        vm.CurrentConflictIndex.Should().Be(0);
    }

    [Fact]
    public void PreviousConflict_WrapsAroundAtStart()
    {
        var vm = CreateVm(DocWithRanges(3));
        vm.CurrentConflictIndex = 0;
        vm.PreviousConflictCommand.Execute(null);
        vm.CurrentConflictIndex.Should().Be(2);
    }

    [Fact]
    public void NextConflict_SkipsResolvedRanges()
    {
        var vm = CreateVm(DocWithRanges(3));
        // Resolve range 1; Next from 0 should go to 2, not 1.
        vm.RangeStates[1] = ResolutionState.AcceptOurs.Instance;
        vm.CurrentConflictIndex = 0;
        vm.NextConflictCommand.Execute(null);
        vm.CurrentConflictIndex.Should().Be(2);
    }

    [Fact]
    public void NextConflict_AllResolved_StillWrapsWithoutCrash()
    {
        var vm = CreateVm(DocWithRanges(3));
        vm.RangeStates[0] = ResolutionState.AcceptOurs.Instance;
        vm.RangeStates[1] = ResolutionState.AcceptTheirs.Instance;
        vm.RangeStates[2] = ResolutionState.AcceptOurs.Instance;
        vm.CurrentConflictIndex = 0;
        vm.NextConflictCommand.Execute(null);
        vm.CurrentConflictIndex.Should().BeInRange(0, 2);
    }

    [Fact]
    public void AcceptCurrentConflictOurs_SetsStateForCurrentRange()
    {
        var vm = CreateVm(DocWithRanges(3));
        vm.CurrentConflictIndex = 1;
        vm.AcceptCurrentConflictOursCommand.Execute(null);
        vm.RangeStates.Should().ContainKey(1);
        vm.RangeStates[1].Should().BeOfType<ResolutionState.AcceptOurs>();
    }

    [Fact]
    public void AcceptCurrentConflictTheirs_SetsStateForCurrentRange()
    {
        var vm = CreateVm(DocWithRanges(2));
        vm.CurrentConflictIndex = 0;
        vm.AcceptCurrentConflictTheirsCommand.Execute(null);
        vm.RangeStates[0].Should().BeOfType<ResolutionState.AcceptTheirs>();
    }

    [Fact]
    public void AcceptCurrentConflictBoth_SetsAcceptBoth()
    {
        var vm = CreateVm(DocWithRanges(1));
        vm.CurrentConflictIndex = 0;
        vm.AcceptCurrentConflictBothCommand.Execute(null);
        vm.RangeStates[0].Should().BeOfType<ResolutionState.AcceptBoth>();
    }

    [Fact]
    public void AcceptCurrentConflict_WithEmptyDocument_IsNoOp()
    {
        var vm = CreateVm(DocWithRanges(0));
        vm.Invoking(v => v.AcceptCurrentConflictOursCommand.Execute(null)).Should().NotThrow();
        vm.RangeStates.Should().BeEmpty();
    }

    /// <summary>
    /// Minimal IMergeEngine stub — returns a pre-built document regardless of inputs.
    /// Used because Phase 4 navigation tests don't need the real engine's shell-out.
    /// </summary>
    private sealed class FakeMergeEngine : IMergeEngine
    {
        private readonly MergeDocument? _doc;
        public FakeMergeEngine(MergeDocument? doc) => _doc = doc;
        public Task<MergeDocument> MergeAsync(
            string filePath, string baseText, string oursText, string theirsText,
            bool ignoreWhitespace = false, string? oursLabel = null, string? theirsLabel = null,
            string? baseLabel = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_doc ?? new MergeDocument(
                filePath, baseText, oursText, theirsText, "",
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<string>(), Array.Empty<ModifiedBaseRange>(), "\n", true));
    }
}
