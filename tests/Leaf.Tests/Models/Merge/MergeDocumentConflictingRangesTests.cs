#nullable enable
using FluentAssertions;
using Leaf.Models.Merge;
using Xunit;

namespace Leaf.Tests.Models.Merge;

/// <summary>
/// Pins the <see cref="MergeDocument.ConflictingRanges"/> helper that
/// consolidates the per-call-site <c>Ranges.Where(r =&gt; r.IsConflicting)</c>
/// filter. Drift between the old inline predicate and any replacement would
/// show up in these tests before it reached navigation or composition.
/// </summary>
public class MergeDocumentConflictingRangesTests
{
    private static ModifiedBaseRange Range(int index, bool conflicting) =>
        new(
            Index: index,
            Base: new LineRange(index + 1, index + 2),
            Ours: new LineRange(index + 1, index + 2),
            Theirs: new LineRange(index + 1, index + 2),
            ResultMarkedRange: new LineRange(index + 1, index + 6),
            BaseLines: new[] { "" },
            OursLines: new[] { "" },
            TheirsLines: new[] { "" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: conflicting,
            IsOrderRelevant: true);

    private static MergeDocument Doc(params ModifiedBaseRange[] ranges) =>
        new("f.cs", "", "", "", "",
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), ranges, "\n", true);

    [Fact]
    public void ConflictingRanges_FiltersOutAutoMergedRanges()
    {
        var doc = Doc(
            Range(0, conflicting: true),
            Range(1, conflicting: false),
            Range(2, conflicting: true));

        doc.ConflictingRanges.Select(r => r.Index).Should().Equal(0, 2);
    }

    [Fact]
    public void ConflictingRanges_OnEmptyRanges_ReturnsEmpty()
    {
        var doc = Doc();
        doc.ConflictingRanges.Should().BeEmpty();
    }

    [Fact]
    public void ConflictingRanges_AgreesWithConflictCount()
    {
        var doc = Doc(
            Range(0, conflicting: true),
            Range(1, conflicting: false),
            Range(2, conflicting: true),
            Range(3, conflicting: true));

        doc.ConflictingRanges.Count().Should().Be(doc.ConflictCount,
            because: "the filter and the count must always agree — they share a predicate");
    }
}
