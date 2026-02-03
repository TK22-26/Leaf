using FluentAssertions;
using Leaf.Models;
using Xunit;

namespace Leaf.Tests.Models;

public class DiffHunkTests
{
    [Fact]
    public void Header_ShouldFormatCorrectly()
    {
        // Arrange
        var hunk = new DiffHunk
        {
            OldStartLine = 10,
            OldLineCount = 5,
            NewStartLine = 12,
            NewLineCount = 8
        };

        // Act
        var header = hunk.Header;

        // Assert
        header.Should().Be("@@ -10,5 +12,8 @@");
    }

    [Fact]
    public void Header_WithSingleLineChange_ShouldFormatCorrectly()
    {
        // Arrange
        var hunk = new DiffHunk
        {
            OldStartLine = 1,
            OldLineCount = 1,
            NewStartLine = 1,
            NewLineCount = 1
        };

        // Act
        var header = hunk.Header;

        // Assert
        header.Should().Be("@@ -1,1 +1,1 @@");
    }

    [Fact]
    public void LinesAdded_ShouldCountAddedLines()
    {
        // Arrange
        var hunk = new DiffHunk
        {
            Lines =
            [
                new DiffLine { Type = DiffLineType.Unchanged, Text = "context" },
                new DiffLine { Type = DiffLineType.Added, Text = "new line 1" },
                new DiffLine { Type = DiffLineType.Added, Text = "new line 2" },
                new DiffLine { Type = DiffLineType.Deleted, Text = "old line" },
                new DiffLine { Type = DiffLineType.Unchanged, Text = "context" }
            ]
        };

        // Act
        var linesAdded = hunk.LinesAdded;

        // Assert
        linesAdded.Should().Be(2);
    }

    [Fact]
    public void LinesDeleted_ShouldCountDeletedLines()
    {
        // Arrange
        var hunk = new DiffHunk
        {
            Lines =
            [
                new DiffLine { Type = DiffLineType.Unchanged, Text = "context" },
                new DiffLine { Type = DiffLineType.Deleted, Text = "old line 1" },
                new DiffLine { Type = DiffLineType.Deleted, Text = "old line 2" },
                new DiffLine { Type = DiffLineType.Deleted, Text = "old line 3" },
                new DiffLine { Type = DiffLineType.Added, Text = "new line" },
                new DiffLine { Type = DiffLineType.Unchanged, Text = "context" }
            ]
        };

        // Act
        var linesDeleted = hunk.LinesDeleted;

        // Assert
        linesDeleted.Should().Be(3);
    }

    [Fact]
    public void LinesAdded_WithNoAddedLines_ShouldReturnZero()
    {
        // Arrange
        var hunk = new DiffHunk
        {
            Lines =
            [
                new DiffLine { Type = DiffLineType.Unchanged, Text = "context" },
                new DiffLine { Type = DiffLineType.Deleted, Text = "old line" }
            ]
        };

        // Act
        var linesAdded = hunk.LinesAdded;

        // Assert
        linesAdded.Should().Be(0);
    }

    [Fact]
    public void LinesDeleted_WithNoDeletedLines_ShouldReturnZero()
    {
        // Arrange
        var hunk = new DiffHunk
        {
            Lines =
            [
                new DiffLine { Type = DiffLineType.Unchanged, Text = "context" },
                new DiffLine { Type = DiffLineType.Added, Text = "new line" }
            ]
        };

        // Act
        var linesDeleted = hunk.LinesDeleted;

        // Assert
        linesDeleted.Should().Be(0);
    }

    [Fact]
    public void EmptyHunk_ShouldHaveZeroCountsAndEmptyLines()
    {
        // Arrange
        var hunk = new DiffHunk();

        // Act & Assert
        hunk.Lines.Should().BeEmpty();
        hunk.LinesAdded.Should().Be(0);
        hunk.LinesDeleted.Should().Be(0);
    }

    [Fact]
    public void Context_ShouldBeSettableAndGettable()
    {
        // Arrange
        var hunk = new DiffHunk
        {
            Context = "void MyMethod()"
        };

        // Assert
        hunk.Context.Should().Be("void MyMethod()");
    }

    [Fact]
    public void Index_ShouldBeSettableAndGettable()
    {
        // Arrange
        var hunk = new DiffHunk
        {
            Index = 5
        };

        // Assert
        hunk.Index.Should().Be(5);
    }
}
