using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Leaf.Services.Merge;
using Xunit;

namespace Leaf.Tests.Services.Merge;

/// <summary>
/// Tests for the legacy <see cref="ThreeWayMergeService"/> adapter. The adapter
/// routes calls through <see cref="IMergeEngine"/> and shapes the result into the
/// old <see cref="FileMergeResult"/> / <see cref="MergeRegion"/> form.
/// </summary>
public class ThreeWayMergeServiceAdapterTests
{
    private static ThreeWayMergeService CreateService()
        => new(new GitMergeFileEngine(new GitCommandRunner()));

    [Fact]
    public async Task PerformMergeAsync_NoConflict_ReturnsSingleUnchangedRegion()
    {
        var svc = CreateService();
        const string text = "a\nb\nc\n";

        var result = await svc.PerformMergeAsync("f.txt", text, text, text);

        result.FilePath.Should().Be("f.txt");
        result.ConflictCount.Should().Be(0);
        result.IsFullyResolved.Should().BeTrue();
        result.Regions.Should().HaveCount(1);
        result.Regions[0].Type.Should().Be(MergeRegionType.Unchanged);
    }

    [Fact]
    public async Task PerformMergeAsync_SingleConflict_ProducesThreeRegions()
    {
        var svc = CreateService();
        var result = await svc.PerformMergeAsync(
            "f.txt",
            "a\nb\nc\n",
            "a\nb-ours\nc\n",
            "a\nb-theirs\nc\n");

        result.ConflictCount.Should().Be(1);
        result.Regions.Should().HaveCount(3);
        result.Regions[0].Type.Should().Be(MergeRegionType.Unchanged);
        result.Regions[1].Type.Should().Be(MergeRegionType.Conflict);
        result.Regions[1].ConflictNumber.Should().Be(1);
        result.Regions[1].OursLines.Should().Equal("b-ours");
        result.Regions[1].TheirsLines.Should().Equal("b-theirs");
        result.Regions[2].Type.Should().Be(MergeRegionType.Unchanged);
    }

    [Fact]
    public async Task PerformMergeAsync_TwoConflicts_NumbersInOrder()
    {
        var svc = CreateService();
        var result = await svc.PerformMergeAsync(
            "f.txt",
            "a\nb\nc\nd\ne\n",
            "a\nb-o\nc\nd-o\ne\n",
            "a\nb-t\nc\nd-t\ne\n");

        result.ConflictCount.Should().Be(2);
        var conflicts = result.Regions.Where(r => r.Type == MergeRegionType.Conflict).ToList();
        conflicts[0].ConflictNumber.Should().Be(1);
        conflicts[1].ConflictNumber.Should().Be(2);
    }

    [Fact]
    public async Task PerformMergeAsync_RegionsHaveIncrementingIndex()
    {
        var svc = CreateService();
        var result = await svc.PerformMergeAsync(
            "f.txt",
            "a\nb\nc\n",
            "a\nours\nc\n",
            "a\ntheirs\nc\n");

        for (int i = 0; i < result.Regions.Count; i++)
        {
            result.Regions[i].Index.Should().Be(i);
        }
    }

    [Fact]
    public async Task PerformMergeAsync_NullInputs_ThrowArgumentNull()
    {
        var svc = CreateService();

        var act1 = async () => await svc.PerformMergeAsync(null!, "a", "a", "a");
        await act1.Should().ThrowAsync<ArgumentNullException>();

        var act2 = async () => await svc.PerformMergeAsync("f", null!, "a", "a");
        await act2.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullEngine_Throws()
    {
        var act = () => new ThreeWayMergeService(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DefaultConstructor_CreatesUsableService()
    {
        var svc = new ThreeWayMergeService();
        svc.Should().NotBeNull();
    }
}
