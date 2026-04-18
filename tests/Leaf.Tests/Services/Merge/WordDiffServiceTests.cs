using FluentAssertions;
using Leaf.Services.Merge;
using Xunit;

namespace Leaf.Tests.Services.Merge;

public class WordDiffServiceTests
{
    private readonly WordDiffService _svc = new();

    [Fact]
    public void DiffLines_IdenticalLines_ReturnSingleUnchangedSegmentPerSide()
    {
        var (l, r) = _svc.DiffLines("foo bar", "foo bar");
        l.Should().HaveCount(1);
        l[0].Kind.Should().Be(TokenKind.Unchanged);
        r.Should().HaveCount(1);
        r[0].Kind.Should().Be(TokenKind.Unchanged);
    }

    [Fact]
    public void DiffLines_EmptyBothSides_ReturnsEmptySegmentLists()
    {
        // Empty-both fast path keeps the invariant "EndColumn > StartColumn" on
        // every emitted segment by returning empty lists rather than a zero-width
        // Unchanged segment.
        var (l, r) = _svc.DiffLines(string.Empty, string.Empty);
        l.Should().BeEmpty();
        r.Should().BeEmpty();
    }

    [Fact]
    public void DiffLines_EmptyLeft_FullRightIsAdded()
    {
        var (l, r) = _svc.DiffLines(string.Empty, "hello");
        l.Should().BeEmpty();
        r.Should().Contain(s => s.Kind == TokenKind.Added && s.Text == "hello");
    }

    [Fact]
    public void DiffLines_EmptyRight_FullLeftIsRemoved()
    {
        var (l, r) = _svc.DiffLines("hello", string.Empty);
        l.Should().Contain(s => s.Kind == TokenKind.Removed && s.Text == "hello");
        r.Should().BeEmpty();
    }

    [Fact]
    public void DiffLines_WordReplacement_MarksChangedTokenOnly()
    {
        // "int x = 1;" vs "int y = 1;" — only the identifier changes.
        var (l, r) = _svc.DiffLines("int x = 1;", "int y = 1;");
        // Left has "int" unchanged, " " unchanged, "x" removed, " = 1;" unchanged.
        l.Should().Contain(s => s.Kind == TokenKind.Removed && s.Text == "x");
        r.Should().Contain(s => s.Kind == TokenKind.Added && s.Text == "y");
        // Unchanged prefix / suffix tokens must exist.
        l.Should().Contain(s => s.Kind == TokenKind.Unchanged && s.Text.Contains("int"));
        l.Should().Contain(s => s.Kind == TokenKind.Unchanged && s.Text.Contains("1"));
    }

    [Fact]
    public void DiffLines_PunctuationEdit_DiffIsNarrow()
    {
        var (l, r) = _svc.DiffLines("arr[0]", "arr[1]");
        l.Should().Contain(s => s.Kind == TokenKind.Removed && s.Text == "0");
        r.Should().Contain(s => s.Kind == TokenKind.Added && s.Text == "1");
        // Brackets stay unchanged.
        l.Should().Contain(s => s.Kind == TokenKind.Unchanged && s.Text.Contains("["));
        r.Should().Contain(s => s.Kind == TokenKind.Unchanged && s.Text.Contains("]"));
    }

    [Fact]
    public void DiffLines_ColumnRanges_AreOneBasedAndContiguous()
    {
        var (l, _) = _svc.DiffLines("hello world", "hello earth");
        int lastEnd = 1;
        foreach (var seg in l)
        {
            seg.StartColumn.Should().Be(lastEnd);
            seg.EndColumnExclusive.Should().BeGreaterThan(seg.StartColumn);
            lastEnd = seg.EndColumnExclusive;
        }
        lastEnd.Should().Be("hello world".Length + 1);
    }

    [Fact]
    public void DiffLines_UnicodeContent_DoesNotCrash()
    {
        var (l, r) = _svc.DiffLines("αβ γ", "αβ δ");
        l.Should().NotBeEmpty();
        r.Should().NotBeEmpty();
    }
}
