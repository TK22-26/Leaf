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
/// C2 Alt+arrow navigation commands: NextChangeSpan / PreviousChangeSpan
/// (Alt+Left / Alt+Right) walk through the detailed diffs inside a conflict
/// range; NextAutoMergedRegion / PreviousAutoMergedRegion (Alt+Down / Alt+Up)
/// walk through non-conflicting ranges surfaced for context.
/// </summary>
public class MergeEditorViewModelAltNavTests
{
    private static ModifiedBaseRange Range(int index, bool conflicting, int diffCount)
    {
        var diffs = new List<DetailedLineRangeMapping>();
        for (int i = 0; i < diffCount; i++)
        {
            diffs.Add(new DetailedLineRangeMapping(
                BaseRange: new LineRange(i + 1, i + 2),
                ModifiedRange: new LineRange(i + 1, i + 2)));
        }
        return new ModifiedBaseRange(
            Index: index,
            Base: new LineRange(index + 1, index + 2),
            Ours: new LineRange(index + 1, index + 2),
            Theirs: new LineRange(index + 1, index + 2),
            ResultMarkedRange: new LineRange(index + 1, index + 8),
            BaseLines: new[] { $"base-{index}" },
            OursLines: new[] { $"ours-{index}" },
            TheirsLines: new[] { $"theirs-{index}" },
            OursDiffs: diffs,
            TheirsDiffs: diffs,
            IsConflicting: conflicting,
            IsOrderRelevant: true);
    }

    private static MergeDocument DocWith(params ModifiedBaseRange[] ranges)
    {
        return new MergeDocument(
            "test.txt", "", "", "", "",
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), ranges, "\n", true);
    }

    private static MergeEditorViewModel CreateVm(MergeDocument doc)
    {
        var vm = new MergeEditorViewModel(
            new FakeGitService(), new FakeClipboardService(),
            new FakeMergeEngine(doc), "C:/test");
        typeof(MergeEditorViewModel).GetProperty(nameof(vm.Document))!.SetValue(vm, doc);
        return vm;
    }

    [Fact]
    public void ChangingCurrentConflictIndex_ResetsChangeSpanCursor()
    {
        // Regression guard: before the fix, F8 / Shift+F8 advanced
        // CurrentConflictIndex without resetting CurrentChangeSpanIndex, so
        // Alt+Right after a conflict jump could index past a shorter
        // conflict's OursDiffs.Count + TheirsDiffs.Count and silently skip
        // spans. The partial OnCurrentConflictIndexChanged hook zeroes the
        // secondary cursor on every CurrentConflictIndex write.
        var vm = CreateVm(DocWith(
            Range(0, conflicting: true, diffCount: 3),
            Range(1, conflicting: true, diffCount: 1)));
        vm.CurrentConflictIndex = 0;
        vm.CurrentChangeSpanIndex = 4;

        vm.CurrentConflictIndex = 1;

        vm.CurrentChangeSpanIndex.Should().Be(0,
            because: "every CurrentConflictIndex change must reset the secondary cursor to avoid drift");
    }

    [Fact]
    public void NextConflict_AfterManualSpanCursor_ResetsSpanIndexToZero()
    {
        // Same invariant via the NextConflictCommand path that F8 triggers.
        var vm = CreateVm(DocWith(
            Range(0, conflicting: true, diffCount: 5),
            Range(1, conflicting: true, diffCount: 2)));
        vm.CurrentConflictIndex = 0;
        vm.CurrentChangeSpanIndex = 7;

        vm.NextConflictCommand.Execute(null);

        vm.CurrentChangeSpanIndex.Should().Be(0);
    }

    [Fact]
    public void NextChangeSpan_AdvancesCursorWithinCurrentConflict()
    {
        // Single conflict with 3 spans each on Ours and Theirs = 6 spans.
        var vm = CreateVm(DocWith(Range(0, conflicting: true, diffCount: 3)));
        vm.CurrentConflictIndex = 0;
        vm.CurrentChangeSpanIndex = 0;

        vm.NextChangeSpanCommand.Execute(null);
        vm.CurrentChangeSpanIndex.Should().Be(1);

        vm.NextChangeSpanCommand.Execute(null);
        vm.CurrentChangeSpanIndex.Should().Be(2);
    }

    [Fact]
    public void NextChangeSpan_PastLastSpan_WrapsToNextConflict()
    {
        // Two conflicts; first has 2 spans total (1 Ours + 1 Theirs).
        var vm = CreateVm(DocWith(
            Range(0, conflicting: true, diffCount: 1),
            Range(1, conflicting: true, diffCount: 1)));
        vm.CurrentConflictIndex = 0;
        vm.CurrentChangeSpanIndex = 1; // at the last span of conflict 0.

        vm.NextChangeSpanCommand.Execute(null);

        vm.CurrentConflictIndex.Should().Be(1,
            because: "past the last span of a conflict should advance to the next conflict");
        vm.CurrentChangeSpanIndex.Should().Be(0,
            because: "landing on a new conflict should reset the change-span cursor to 0");
    }

    [Fact]
    public void PreviousChangeSpan_AtFirstSpan_RetreatsToPreviousConflictLastSpan()
    {
        var vm = CreateVm(DocWith(
            Range(0, conflicting: true, diffCount: 2),
            Range(1, conflicting: true, diffCount: 1)));
        vm.CurrentConflictIndex = 1;
        vm.CurrentChangeSpanIndex = 0;

        vm.PreviousChangeSpanCommand.Execute(null);

        vm.CurrentConflictIndex.Should().Be(0);
        // Conflict 0 has 4 spans (2 Ours + 2 Theirs); cursor lands on span 3.
        vm.CurrentChangeSpanIndex.Should().Be(3);
    }

    [Fact]
    public void NextAutoMergedRegion_CyclesThroughNonConflictingRanges()
    {
        var vm = CreateVm(DocWith(
            Range(0, conflicting: false, diffCount: 0),
            Range(1, conflicting: true, diffCount: 0),
            Range(2, conflicting: false, diffCount: 0)));

        vm.CurrentAutoMergedRegionIndex = 0;
        vm.NextAutoMergedRegionCommand.Execute(null);
        vm.CurrentAutoMergedRegionIndex.Should().Be(1,
            because: "second non-conflicting range is at cursor index 1 in the auto-merged list");

        vm.NextAutoMergedRegionCommand.Execute(null);
        vm.CurrentAutoMergedRegionIndex.Should().Be(0,
            because: "wrap back to the first after passing the last");
    }

    [Fact]
    public void PreviousAutoMergedRegion_WrapsAtStart()
    {
        var vm = CreateVm(DocWith(
            Range(0, conflicting: false, diffCount: 0),
            Range(1, conflicting: false, diffCount: 0)));
        vm.CurrentAutoMergedRegionIndex = 0;

        vm.PreviousAutoMergedRegionCommand.Execute(null);
        vm.CurrentAutoMergedRegionIndex.Should().Be(1);
    }

    [Fact]
    public void AltNavCommands_NoOpWithoutDocument()
    {
        var vm = new MergeEditorViewModel(
            new FakeGitService(), new FakeClipboardService(),
            new FakeMergeEngine(null), "C:/test");
        FluentActions.Invoking(() => vm.NextChangeSpanCommand.Execute(null)).Should().NotThrow();
        FluentActions.Invoking(() => vm.PreviousChangeSpanCommand.Execute(null)).Should().NotThrow();
        FluentActions.Invoking(() => vm.NextAutoMergedRegionCommand.Execute(null)).Should().NotThrow();
        FluentActions.Invoking(() => vm.PreviousAutoMergedRegionCommand.Execute(null)).Should().NotThrow();
    }

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
