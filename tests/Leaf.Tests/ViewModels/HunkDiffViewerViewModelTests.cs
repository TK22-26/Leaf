using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Leaf.Tests.Fakes;
using Leaf.ViewModels;
using Moq;
using Xunit;

namespace Leaf.Tests.ViewModels;

public class HunkDiffViewerViewModelTests
{
    private readonly FakeGitService _fakeGitService;
    private readonly Mock<IHunkService> _mockHunkService;
    private readonly HunkDiffViewerViewModel _sut;

    public HunkDiffViewerViewModelTests()
    {
        _fakeGitService = new FakeGitService();
        _mockHunkService = new Mock<IHunkService>();
        _sut = new HunkDiffViewerViewModel(_fakeGitService, _mockHunkService.Object);
    }

    #region LoadDiff Tests

    [Fact]
    public void LoadDiff_SetsFileNameAndPath()
    {
        // Arrange
        var diffResult = CreateDiffResult("test.cs", "src/test.cs");
        _mockHunkService.Setup(s => s.ParseHunks(diffResult, It.IsAny<int>()))
            .Returns(new List<DiffHunk>());

        // Act
        _sut.LoadDiff(diffResult, "/repo");

        // Assert
        _sut.FileName.Should().Be("test.cs");
        _sut.FilePath.Should().Be("src/test.cs");
        _sut.RepositoryPath.Should().Be("/repo");
    }

    [Fact]
    public void LoadDiff_ParsesHunks()
    {
        // Arrange
        var diffResult = CreateDiffResult("test.cs", "src/test.cs");
        var hunks = new List<DiffHunk>
        {
            new() { Index = 0, OldStartLine = 1, OldLineCount = 5, NewStartLine = 1, NewLineCount = 6 },
            new() { Index = 1, OldStartLine = 20, OldLineCount = 3, NewStartLine = 21, NewLineCount = 4 }
        };
        _mockHunkService.Setup(s => s.ParseHunks(diffResult, It.IsAny<int>()))
            .Returns(hunks);

        // Act
        _sut.LoadDiff(diffResult, "/repo");

        // Assert
        _sut.Hunks.Should().HaveCount(2);
        _sut.Hunks[0].Index.Should().Be(0);
        _sut.Hunks[1].Index.Should().Be(1);
    }

    [Fact]
    public void LoadDiff_SetsLinesAddedAndDeleted()
    {
        // Arrange
        var diffResult = CreateDiffResult("test.cs", "src/test.cs");
        diffResult.LinesAddedCount = 10;
        diffResult.LinesDeletedCount = 5;
        _mockHunkService.Setup(s => s.ParseHunks(diffResult, It.IsAny<int>()))
            .Returns(new List<DiffHunk>());

        // Act
        _sut.LoadDiff(diffResult, "/repo");

        // Assert
        _sut.LinesAdded.Should().Be(10);
        _sut.LinesDeleted.Should().Be(5);
    }

    [Fact]
    public void LoadDiff_SetsBinaryFlag()
    {
        // Arrange
        var diffResult = CreateDiffResult("image.png", "assets/image.png");
        diffResult.IsBinary = true;
        _mockHunkService.Setup(s => s.ParseHunks(diffResult, It.IsAny<int>()))
            .Returns(new List<DiffHunk>());

        // Act
        _sut.LoadDiff(diffResult, "/repo");

        // Assert
        _sut.IsBinary.Should().BeTrue();
    }

    #endregion

    #region RevertHunk Tests

    [Fact]
    public async Task RevertHunkAsync_GeneratesPatchAndCallsGitService()
    {
        // Arrange
        var hunk = CreateTestHunk();
        var diffResult = CreateDiffResult("test.cs", "src/test.cs");
        _mockHunkService.Setup(s => s.ParseHunks(diffResult, It.IsAny<int>()))
            .Returns(new List<DiffHunk> { hunk });
        _mockHunkService.Setup(s => s.GenerateHunkPatch("src/test.cs", hunk))
            .Returns("patch content");

        _sut.LoadDiff(diffResult, "/repo");

        // Act
        await _sut.RevertHunkAsync(hunk);

        // Assert
        _fakeGitService.RevertHunkCalls.Should().ContainSingle();
        _fakeGitService.RevertHunkCalls[0].RepoPath.Should().Be("/repo");
        _fakeGitService.RevertHunkCalls[0].PatchContent.Should().Be("patch content");
    }

    [Fact]
    public async Task RevertHunkAsync_RaisesHunkRevertedEvent()
    {
        // Arrange
        var hunk = CreateTestHunk();
        var diffResult = CreateDiffResult("test.cs", "src/test.cs");
        _mockHunkService.Setup(s => s.ParseHunks(diffResult, It.IsAny<int>()))
            .Returns(new List<DiffHunk> { hunk });
        _mockHunkService.Setup(s => s.GenerateHunkPatch("src/test.cs", hunk))
            .Returns("patch content");

        _sut.LoadDiff(diffResult, "/repo");

        bool eventRaised = false;
        _sut.HunkReverted += (_, _) => eventRaised = true;

        // Act
        await _sut.RevertHunkAsync(hunk);

        // Assert
        eventRaised.Should().BeTrue();
    }

    [Fact]
    public async Task RevertHunkAsync_WhenGitFails_DoesNotRaiseEvent()
    {
        // Arrange
        var hunk = CreateTestHunk();
        var diffResult = CreateDiffResult("test.cs", "src/test.cs");
        _mockHunkService.Setup(s => s.ParseHunks(diffResult, It.IsAny<int>()))
            .Returns(new List<DiffHunk> { hunk });
        _mockHunkService.Setup(s => s.GenerateHunkPatch("src/test.cs", hunk))
            .Returns("patch content");

        _fakeGitService.ShouldThrowOnRevertHunk = true;

        _sut.LoadDiff(diffResult, "/repo");

        bool eventRaised = false;
        _sut.HunkReverted += (_, _) => eventRaised = true;

        // Act
        await _sut.RevertHunkAsync(hunk);

        // Assert
        eventRaised.Should().BeFalse();
        _sut.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region StageHunk Tests

    [Fact]
    public async Task StageHunkAsync_GeneratesPatchAndCallsGitService()
    {
        // Arrange
        var hunk = CreateTestHunk();
        var diffResult = CreateDiffResult("test.cs", "src/test.cs");
        _mockHunkService.Setup(s => s.ParseHunks(diffResult, It.IsAny<int>()))
            .Returns(new List<DiffHunk> { hunk });
        _mockHunkService.Setup(s => s.GenerateHunkPatch("src/test.cs", hunk))
            .Returns("patch content");

        _sut.LoadDiff(diffResult, "/repo");

        // Act
        await _sut.StageHunkAsync(hunk);

        // Assert
        _fakeGitService.StageHunkCalls.Should().ContainSingle();
        _fakeGitService.StageHunkCalls[0].RepoPath.Should().Be("/repo");
        _fakeGitService.StageHunkCalls[0].PatchContent.Should().Be("patch content");
    }

    [Fact]
    public async Task StageHunkAsync_RaisesHunkStagedEvent()
    {
        // Arrange
        var hunk = CreateTestHunk();
        var diffResult = CreateDiffResult("test.cs", "src/test.cs");
        _mockHunkService.Setup(s => s.ParseHunks(diffResult, It.IsAny<int>()))
            .Returns(new List<DiffHunk> { hunk });
        _mockHunkService.Setup(s => s.GenerateHunkPatch("src/test.cs", hunk))
            .Returns("patch content");

        _sut.LoadDiff(diffResult, "/repo");

        bool eventRaised = false;
        _sut.HunkStaged += (_, _) => eventRaised = true;

        // Act
        await _sut.StageHunkAsync(hunk);

        // Assert
        eventRaised.Should().BeTrue();
    }

    #endregion

    #region Clear Tests

    [Fact]
    public void Clear_ResetsAllProperties()
    {
        // Arrange
        var diffResult = CreateDiffResult("test.cs", "src/test.cs");
        _mockHunkService.Setup(s => s.ParseHunks(diffResult, It.IsAny<int>()))
            .Returns(new List<DiffHunk> { CreateTestHunk() });
        _sut.LoadDiff(diffResult, "/repo");

        // Act
        _sut.Clear();

        // Assert
        _sut.FileName.Should().BeEmpty();
        _sut.FilePath.Should().BeEmpty();
        _sut.RepositoryPath.Should().BeEmpty();
        _sut.Hunks.Should().BeEmpty();
        _sut.LinesAdded.Should().Be(0);
        _sut.LinesDeleted.Should().Be(0);
        _sut.IsBinary.Should().BeFalse();
    }

    #endregion

    #region HasChanges Tests

    [Fact]
    public void HasChanges_WhenHunksExist_ReturnsTrue()
    {
        // Arrange
        var diffResult = CreateDiffResult("test.cs", "src/test.cs");
        _mockHunkService.Setup(s => s.ParseHunks(diffResult, It.IsAny<int>()))
            .Returns(new List<DiffHunk> { CreateTestHunk() });

        // Act
        _sut.LoadDiff(diffResult, "/repo");

        // Assert
        _sut.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void HasChanges_WhenNoHunks_ReturnsFalse()
    {
        // Arrange
        var diffResult = CreateDiffResult("test.cs", "src/test.cs");
        _mockHunkService.Setup(s => s.ParseHunks(diffResult, It.IsAny<int>()))
            .Returns(new List<DiffHunk>());

        // Act
        _sut.LoadDiff(diffResult, "/repo");

        // Assert
        _sut.HasChanges.Should().BeFalse();
    }

    #endregion

    #region Helper Methods

    private static FileDiffResult CreateDiffResult(string fileName, string filePath)
    {
        return new FileDiffResult
        {
            FileName = fileName,
            FilePath = filePath,
            Lines = [],
            LinesAddedCount = 0,
            LinesDeletedCount = 0
        };
    }

    private static DiffHunk CreateTestHunk()
    {
        return new DiffHunk
        {
            Index = 0,
            OldStartLine = 10,
            OldLineCount = 3,
            NewStartLine = 10,
            NewLineCount = 4,
            Lines =
            [
                new DiffLine { OldLineNumber = 10, NewLineNumber = 10, Type = DiffLineType.Unchanged, Text = "context" },
                new DiffLine { OldLineNumber = 11, NewLineNumber = null, Type = DiffLineType.Deleted, Text = "deleted" },
                new DiffLine { OldLineNumber = null, NewLineNumber = 11, Type = DiffLineType.Added, Text = "added 1" },
                new DiffLine { OldLineNumber = null, NewLineNumber = 12, Type = DiffLineType.Added, Text = "added 2" },
                new DiffLine { OldLineNumber = 12, NewLineNumber = 13, Type = DiffLineType.Unchanged, Text = "context" }
            ]
        };
    }

    #endregion
}
