using FluentAssertions;
using Leaf.Models.Merge;
using Xunit;

namespace Leaf.Tests.Models.Merge;

public class LineRangeTests
{
    [Fact]
    public void Length_IsEndMinusStart()
    {
        var range = new LineRange(5, 9);
        range.Length.Should().Be(4);
    }

    [Fact]
    public void IsEmpty_WhenStartEqualsEnd()
    {
        new LineRange(5, 5).IsEmpty.Should().BeTrue();
        new LineRange(5, 6).IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void LastLineInclusive_ReturnsExclusiveEndMinusOne()
    {
        new LineRange(5, 9).LastLineInclusive.Should().Be(8);
    }

    [Theory]
    [InlineData(5, 9, 5, true)]
    [InlineData(5, 9, 8, true)]
    [InlineData(5, 9, 9, false)]
    [InlineData(5, 9, 4, false)]
    public void Contains_RespectsHalfOpen(int start, int end, int probe, bool expected)
    {
        new LineRange(start, end).Contains(probe).Should().Be(expected);
    }

    [Theory]
    [InlineData(5, 9, 7, 12, true)]     // overlapping
    [InlineData(5, 9, 9, 12, false)]    // touching but not overlapping (half-open)
    [InlineData(5, 9, 1, 5, false)]     // theirs ends exactly at ours.Start
    [InlineData(5, 9, 2, 6, true)]      // partial overlap
    public void Overlaps_HandlesBoundaryCases(int aStart, int aEnd, int bStart, int bEnd, bool expected)
    {
        var a = new LineRange(aStart, aEnd);
        var b = new LineRange(bStart, bEnd);
        a.Overlaps(b).Should().Be(expected);
        b.Overlaps(a).Should().Be(expected); // commutative
    }

    [Fact]
    public void Empty_IsDefaultStruct()
    {
        LineRange.Empty.Should().Be(new LineRange(0, 0));
        LineRange.Empty.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Equality_FollowsValueSemantics()
    {
        (new LineRange(5, 9)).Should().Be(new LineRange(5, 9));
        (new LineRange(5, 9)).Should().NotBe(new LineRange(5, 10));
    }
}
