using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.Services;

public class HunkServiceTests
{
    private readonly HunkService _sut;

    public HunkServiceTests()
    {
        _sut = new HunkService();
    }

    #region ParseHunks Tests

    [Fact]
    public void ParseHunks_SingleChange_ReturnsOneHunk()
    {
        // Arrange
        var diffResult = CreateDiffResult(
            [
                new DiffLine { OldLineNumber = 1, NewLineNumber = 1, Type = DiffLineType.Unchanged, Text = "line 1" },
                new DiffLine { OldLineNumber = 2, NewLineNumber = 2, Type = DiffLineType.Unchanged, Text = "line 2" },
                new DiffLine { OldLineNumber = 3, NewLineNumber = 3, Type = DiffLineType.Unchanged, Text = "line 3" },
                new DiffLine { OldLineNumber = 4, NewLineNumber = null, Type = DiffLineType.Deleted, Text = "old line 4" },
                new DiffLine { OldLineNumber = null, NewLineNumber = 4, Type = DiffLineType.Added, Text = "new line 4" },
                new DiffLine { OldLineNumber = 5, NewLineNumber = 5, Type = DiffLineType.Unchanged, Text = "line 5" },
                new DiffLine { OldLineNumber = 6, NewLineNumber = 6, Type = DiffLineType.Unchanged, Text = "line 6" },
                new DiffLine { OldLineNumber = 7, NewLineNumber = 7, Type = DiffLineType.Unchanged, Text = "line 7" }
            ]);

        // Act
        var hunks = _sut.ParseHunks(diffResult, contextLines: 3);

        // Assert
        hunks.Should().HaveCount(1);
        hunks[0].LinesAdded.Should().Be(1);
        hunks[0].LinesDeleted.Should().Be(1);
    }

    [Fact]
    public void ParseHunks_TwoDistantChanges_ReturnsTwoHunks()
    {
        // Arrange - changes at lines 5 and 20, with 3 context lines they should be separate hunks
        var lines = new List<DiffLine>();

        // Lines 1-10 with change at line 5
        for (int i = 1; i <= 10; i++)
        {
            if (i == 5)
            {
                lines.Add(new DiffLine { OldLineNumber = i, NewLineNumber = null, Type = DiffLineType.Deleted, Text = $"old line {i}" });
                lines.Add(new DiffLine { OldLineNumber = null, NewLineNumber = i, Type = DiffLineType.Added, Text = $"new line {i}" });
            }
            else
            {
                lines.Add(new DiffLine { OldLineNumber = i, NewLineNumber = i, Type = DiffLineType.Unchanged, Text = $"line {i}" });
            }
        }

        // Lines 11-25 with change at line 20
        for (int i = 11; i <= 25; i++)
        {
            if (i == 20)
            {
                lines.Add(new DiffLine { OldLineNumber = i, NewLineNumber = null, Type = DiffLineType.Deleted, Text = $"old line {i}" });
                lines.Add(new DiffLine { OldLineNumber = null, NewLineNumber = i, Type = DiffLineType.Added, Text = $"new line {i}" });
            }
            else
            {
                lines.Add(new DiffLine { OldLineNumber = i, NewLineNumber = i, Type = DiffLineType.Unchanged, Text = $"line {i}" });
            }
        }

        var diffResult = CreateDiffResult(lines);

        // Act
        var hunks = _sut.ParseHunks(diffResult, contextLines: 3);

        // Assert
        hunks.Should().HaveCount(2);
        hunks[0].Index.Should().Be(0);
        hunks[1].Index.Should().Be(1);
    }

    [Fact]
    public void ParseHunks_TwoCloseChanges_ReturnsSingleMergedHunk()
    {
        // Arrange - changes at lines 5 and 10, close enough to merge with 3 context lines
        var lines = new List<DiffLine>();

        for (int i = 1; i <= 15; i++)
        {
            if (i == 5 || i == 10)
            {
                lines.Add(new DiffLine { OldLineNumber = i, NewLineNumber = null, Type = DiffLineType.Deleted, Text = $"old line {i}" });
                lines.Add(new DiffLine { OldLineNumber = null, NewLineNumber = i, Type = DiffLineType.Added, Text = $"new line {i}" });
            }
            else
            {
                lines.Add(new DiffLine { OldLineNumber = i, NewLineNumber = i, Type = DiffLineType.Unchanged, Text = $"line {i}" });
            }
        }

        var diffResult = CreateDiffResult(lines);

        // Act
        var hunks = _sut.ParseHunks(diffResult, contextLines: 3);

        // Assert
        hunks.Should().HaveCount(1);
        hunks[0].LinesAdded.Should().Be(2);
        hunks[0].LinesDeleted.Should().Be(2);
    }

    [Fact]
    public void ParseHunks_NoChanges_ReturnsEmptyList()
    {
        // Arrange
        var diffResult = CreateDiffResult(
            [
                new DiffLine { OldLineNumber = 1, NewLineNumber = 1, Type = DiffLineType.Unchanged, Text = "line 1" },
                new DiffLine { OldLineNumber = 2, NewLineNumber = 2, Type = DiffLineType.Unchanged, Text = "line 2" }
            ]);

        // Act
        var hunks = _sut.ParseHunks(diffResult, contextLines: 3);

        // Assert
        hunks.Should().BeEmpty();
    }

    [Fact]
    public void ParseHunks_ChangeAtStart_IncludesLimitedLeadingContext()
    {
        // Arrange - change at line 2, only 1 line of leading context available
        var diffResult = CreateDiffResult(
            [
                new DiffLine { OldLineNumber = 1, NewLineNumber = 1, Type = DiffLineType.Unchanged, Text = "line 1" },
                new DiffLine { OldLineNumber = 2, NewLineNumber = null, Type = DiffLineType.Deleted, Text = "old line 2" },
                new DiffLine { OldLineNumber = null, NewLineNumber = 2, Type = DiffLineType.Added, Text = "new line 2" },
                new DiffLine { OldLineNumber = 3, NewLineNumber = 3, Type = DiffLineType.Unchanged, Text = "line 3" },
                new DiffLine { OldLineNumber = 4, NewLineNumber = 4, Type = DiffLineType.Unchanged, Text = "line 4" },
                new DiffLine { OldLineNumber = 5, NewLineNumber = 5, Type = DiffLineType.Unchanged, Text = "line 5" }
            ]);

        // Act
        var hunks = _sut.ParseHunks(diffResult, contextLines: 3);

        // Assert
        hunks.Should().HaveCount(1);
        // Should have 1 leading context + 2 changes + 3 trailing context = 6 lines max
        // But since total file has 6 lines (including deleted), verify leading context is limited
        hunks[0].Lines.Should().Contain(l => l.Text == "line 1"); // Leading context
    }

    [Fact]
    public void ParseHunks_SetsCorrectLineNumbers()
    {
        // Arrange
        var diffResult = CreateDiffResult(
            [
                new DiffLine { OldLineNumber = 10, NewLineNumber = 10, Type = DiffLineType.Unchanged, Text = "context before" },
                new DiffLine { OldLineNumber = 11, NewLineNumber = null, Type = DiffLineType.Deleted, Text = "deleted" },
                new DiffLine { OldLineNumber = null, NewLineNumber = 11, Type = DiffLineType.Added, Text = "added" },
                new DiffLine { OldLineNumber = 12, NewLineNumber = 12, Type = DiffLineType.Unchanged, Text = "context after" }
            ]);

        // Act
        var hunks = _sut.ParseHunks(diffResult, contextLines: 3);

        // Assert
        hunks.Should().HaveCount(1);
        hunks[0].OldStartLine.Should().Be(10);
        hunks[0].NewStartLine.Should().Be(10);
    }

    #endregion

    #region GenerateHunkPatch Tests

    [Fact]
    public void GenerateHunkPatch_ProducesValidUnifiedDiff()
    {
        // Arrange
        var hunk = new DiffHunk
        {
            OldStartLine = 10,
            OldLineCount = 4,
            NewStartLine = 10,
            NewLineCount = 4,
            Lines =
            [
                new DiffLine { OldLineNumber = 10, NewLineNumber = 10, Type = DiffLineType.Unchanged, Text = "context" },
                new DiffLine { OldLineNumber = 11, NewLineNumber = null, Type = DiffLineType.Deleted, Text = "old line" },
                new DiffLine { OldLineNumber = null, NewLineNumber = 11, Type = DiffLineType.Added, Text = "new line" },
                new DiffLine { OldLineNumber = 12, NewLineNumber = 12, Type = DiffLineType.Unchanged, Text = "context" }
            ]
        };

        // Act
        var patch = _sut.GenerateHunkPatch("src/MyFile.cs", hunk);

        // Assert
        patch.Should().Contain("--- a/src/MyFile.cs");
        patch.Should().Contain("+++ b/src/MyFile.cs");
        patch.Should().Contain("@@ -10,4 +10,4 @@");
        patch.Should().Contain(" context");
        patch.Should().Contain("-old line");
        patch.Should().Contain("+new line");
    }

    [Fact]
    public void GenerateHunkPatch_WithAddedLinesOnly_ProducesCorrectPatch()
    {
        // Arrange
        var hunk = new DiffHunk
        {
            OldStartLine = 5,
            OldLineCount = 2,
            NewStartLine = 5,
            NewLineCount = 4,
            Lines =
            [
                new DiffLine { OldLineNumber = 5, NewLineNumber = 5, Type = DiffLineType.Unchanged, Text = "before" },
                new DiffLine { OldLineNumber = null, NewLineNumber = 6, Type = DiffLineType.Added, Text = "new 1" },
                new DiffLine { OldLineNumber = null, NewLineNumber = 7, Type = DiffLineType.Added, Text = "new 2" },
                new DiffLine { OldLineNumber = 6, NewLineNumber = 8, Type = DiffLineType.Unchanged, Text = "after" }
            ]
        };

        // Act
        var patch = _sut.GenerateHunkPatch("file.txt", hunk);

        // Assert
        patch.Should().Contain("+new 1");
        patch.Should().Contain("+new 2");
        patch.Should().NotContain("-new");
    }

    #endregion

    #region GenerateReversePatch Tests

    [Fact]
    public void GenerateReversePatch_SwapsAddedAndDeleted()
    {
        // Arrange
        var hunk = new DiffHunk
        {
            OldStartLine = 10,
            OldLineCount = 3,
            NewStartLine = 10,
            NewLineCount = 4,
            Lines =
            [
                new DiffLine { OldLineNumber = 10, NewLineNumber = 10, Type = DiffLineType.Unchanged, Text = "context" },
                new DiffLine { OldLineNumber = 11, NewLineNumber = null, Type = DiffLineType.Deleted, Text = "deleted line" },
                new DiffLine { OldLineNumber = null, NewLineNumber = 11, Type = DiffLineType.Added, Text = "added line 1" },
                new DiffLine { OldLineNumber = null, NewLineNumber = 12, Type = DiffLineType.Added, Text = "added line 2" },
                new DiffLine { OldLineNumber = 12, NewLineNumber = 13, Type = DiffLineType.Unchanged, Text = "context" }
            ]
        };

        // Act
        var reversePatch = _sut.GenerateReversePatch("file.cs", hunk);

        // Assert
        // In reverse patch, added becomes deleted and vice versa
        reversePatch.Should().Contain("-added line 1");
        reversePatch.Should().Contain("-added line 2");
        reversePatch.Should().Contain("+deleted line");
        // Line counts should be swapped
        reversePatch.Should().Contain("@@ -10,4 +10,3 @@");
    }

    [Fact]
    public void GenerateReversePatch_WithOnlyAdditions_RemovesThem()
    {
        // Arrange
        var hunk = new DiffHunk
        {
            OldStartLine = 5,
            OldLineCount = 2,
            NewStartLine = 5,
            NewLineCount = 4,
            Lines =
            [
                new DiffLine { OldLineNumber = 5, NewLineNumber = 5, Type = DiffLineType.Unchanged, Text = "context" },
                new DiffLine { OldLineNumber = null, NewLineNumber = 6, Type = DiffLineType.Added, Text = "added 1" },
                new DiffLine { OldLineNumber = null, NewLineNumber = 7, Type = DiffLineType.Added, Text = "added 2" },
                new DiffLine { OldLineNumber = 6, NewLineNumber = 8, Type = DiffLineType.Unchanged, Text = "context" }
            ]
        };

        // Act
        var reversePatch = _sut.GenerateReversePatch("file.txt", hunk);

        // Assert
        reversePatch.Should().Contain("-added 1");
        reversePatch.Should().Contain("-added 2");
        reversePatch.Should().NotContain("+added");
    }

    [Fact]
    public void GenerateReversePatch_WithOnlyDeletions_RestoresThem()
    {
        // Arrange
        var hunk = new DiffHunk
        {
            OldStartLine = 5,
            OldLineCount = 4,
            NewStartLine = 5,
            NewLineCount = 2,
            Lines =
            [
                new DiffLine { OldLineNumber = 5, NewLineNumber = 5, Type = DiffLineType.Unchanged, Text = "context" },
                new DiffLine { OldLineNumber = 6, NewLineNumber = null, Type = DiffLineType.Deleted, Text = "deleted 1" },
                new DiffLine { OldLineNumber = 7, NewLineNumber = null, Type = DiffLineType.Deleted, Text = "deleted 2" },
                new DiffLine { OldLineNumber = 8, NewLineNumber = 6, Type = DiffLineType.Unchanged, Text = "context" }
            ]
        };

        // Act
        var reversePatch = _sut.GenerateReversePatch("file.txt", hunk);

        // Assert
        reversePatch.Should().Contain("+deleted 1");
        reversePatch.Should().Contain("+deleted 2");
        reversePatch.Should().NotContain("-deleted");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ParseHunks_EmptyDiffResult_ReturnsEmptyList()
    {
        // Arrange
        var diffResult = CreateDiffResult([]);

        // Act
        var hunks = _sut.ParseHunks(diffResult, contextLines: 3);

        // Assert
        hunks.Should().BeEmpty();
    }

    [Fact]
    public void ParseHunks_OnlyAddedLines_ReturnsHunk()
    {
        // Arrange - new file scenario
        var diffResult = CreateDiffResult(
            [
                new DiffLine { OldLineNumber = null, NewLineNumber = 1, Type = DiffLineType.Added, Text = "line 1" },
                new DiffLine { OldLineNumber = null, NewLineNumber = 2, Type = DiffLineType.Added, Text = "line 2" },
                new DiffLine { OldLineNumber = null, NewLineNumber = 3, Type = DiffLineType.Added, Text = "line 3" }
            ]);

        // Act
        var hunks = _sut.ParseHunks(diffResult, contextLines: 3);

        // Assert
        hunks.Should().HaveCount(1);
        hunks[0].LinesAdded.Should().Be(3);
        hunks[0].LinesDeleted.Should().Be(0);
    }

    [Fact]
    public void ParseHunks_OnlyDeletedLines_ReturnsHunk()
    {
        // Arrange - deleted file scenario
        var diffResult = CreateDiffResult(
            [
                new DiffLine { OldLineNumber = 1, NewLineNumber = null, Type = DiffLineType.Deleted, Text = "line 1" },
                new DiffLine { OldLineNumber = 2, NewLineNumber = null, Type = DiffLineType.Deleted, Text = "line 2" },
                new DiffLine { OldLineNumber = 3, NewLineNumber = null, Type = DiffLineType.Deleted, Text = "line 3" }
            ]);

        // Act
        var hunks = _sut.ParseHunks(diffResult, contextLines: 3);

        // Assert
        hunks.Should().HaveCount(1);
        hunks[0].LinesAdded.Should().Be(0);
        hunks[0].LinesDeleted.Should().Be(3);
    }

    #endregion

    #region Helper Methods

    private static FileDiffResult CreateDiffResult(List<DiffLine> lines)
    {
        return new FileDiffResult
        {
            FileName = "test.cs",
            FilePath = "src/test.cs",
            Lines = lines,
            LinesAddedCount = lines.Count(l => l.Type == DiffLineType.Added),
            LinesDeletedCount = lines.Count(l => l.Type == DiffLineType.Deleted)
        };
    }

    #endregion
}
