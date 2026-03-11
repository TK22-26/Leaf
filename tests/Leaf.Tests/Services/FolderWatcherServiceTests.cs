using System.IO;
using FluentAssertions;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.Services;

public class FolderWatcherServiceTests : IDisposable
{
    private readonly FolderWatcherService _sut;
    private readonly string _testDirectory;
    private readonly List<string> _discoveredRepos;

    public FolderWatcherServiceTests()
    {
        _sut = new FolderWatcherService();
        _testDirectory = Path.Combine(Path.GetTempPath(), "FolderWatcherTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDirectory);
        _discoveredRepos = [];
        _sut.RepositoryDiscovered += (s, path) => _discoveredRepos.Add(path);
    }

    public void Dispose()
    {
        _sut.Dispose();
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup failures
            }
        }
    }

    #region ScanFolderAsync Tests

    [Fact]
    public async Task ScanFolderAsync_EmptyFolder_ReturnsEmpty()
    {
        // Act
        var result = await _sut.ScanFolderAsync(_testDirectory);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanFolderAsync_FolderWithGitRepo_ReturnsRepoPath()
    {
        // Arrange
        var repoPath = Path.Combine(_testDirectory, "MyRepo");
        Directory.CreateDirectory(repoPath);
        Directory.CreateDirectory(Path.Combine(repoPath, ".git"));

        // Act
        var result = await _sut.ScanFolderAsync(_testDirectory);

        // Assert
        result.Should().ContainSingle();
        result.First().Should().Be(repoPath);
    }

    [Fact]
    public async Task ScanFolderAsync_MultipleRepos_ReturnsAllRepoPaths()
    {
        // Arrange
        var repo1 = Path.Combine(_testDirectory, "Repo1");
        var repo2 = Path.Combine(_testDirectory, "Repo2");
        var repo3 = Path.Combine(_testDirectory, "SubDir", "Repo3");
        Directory.CreateDirectory(Path.Combine(repo1, ".git"));
        Directory.CreateDirectory(Path.Combine(repo2, ".git"));
        Directory.CreateDirectory(Path.Combine(repo3, ".git"));

        // Act
        var result = (await _sut.ScanFolderAsync(_testDirectory)).ToList();

        // Assert
        result.Should().HaveCount(3);
        result.Should().Contain(repo1);
        result.Should().Contain(repo2);
        result.Should().Contain(repo3);
    }

    [Fact]
    public async Task ScanFolderAsync_FolderWithoutGit_ReturnsEmpty()
    {
        // Arrange
        var nonRepo = Path.Combine(_testDirectory, "NotARepo");
        Directory.CreateDirectory(nonRepo);
        Directory.CreateDirectory(Path.Combine(nonRepo, "src"));

        // Act
        var result = await _sut.ScanFolderAsync(_testDirectory);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanFolderAsync_NonexistentFolder_ReturnsEmpty()
    {
        // Act
        var result = await _sut.ScanFolderAsync(Path.Combine(_testDirectory, "DoesNotExist"));

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region AddWatchedFolder Tests

    [Fact]
    public void AddWatchedFolder_ValidPath_DoesNotThrow()
    {
        // Act
        var action = () => _sut.AddWatchedFolder(_testDirectory);

        // Assert
        action.Should().NotThrow();
    }

    [Fact]
    public void AddWatchedFolder_NonexistentPath_DoesNotThrow()
    {
        // Act
        var action = () => _sut.AddWatchedFolder(Path.Combine(_testDirectory, "DoesNotExist"));

        // Assert
        action.Should().NotThrow();
    }

    [Fact]
    public void AddWatchedFolder_NullPath_DoesNotThrow()
    {
        // Act
        var action = () => _sut.AddWatchedFolder(null!);

        // Assert
        action.Should().NotThrow();
    }

    [Fact]
    public void AddWatchedFolder_EmptyPath_DoesNotThrow()
    {
        // Act
        var action = () => _sut.AddWatchedFolder(string.Empty);

        // Assert
        action.Should().NotThrow();
    }

    [Fact]
    public void AddWatchedFolder_DuplicatePath_DoesNotThrow()
    {
        // Arrange
        _sut.AddWatchedFolder(_testDirectory);

        // Act
        var action = () => _sut.AddWatchedFolder(_testDirectory);

        // Assert
        action.Should().NotThrow();
    }

    #endregion

    #region RemoveWatchedFolder Tests

    [Fact]
    public void RemoveWatchedFolder_ExistingWatcher_DoesNotThrow()
    {
        // Arrange
        _sut.AddWatchedFolder(_testDirectory);

        // Act
        var action = () => _sut.RemoveWatchedFolder(_testDirectory);

        // Assert
        action.Should().NotThrow();
    }

    [Fact]
    public void RemoveWatchedFolder_NonexistentWatcher_DoesNotThrow()
    {
        // Act
        var action = () => _sut.RemoveWatchedFolder(_testDirectory);

        // Assert
        action.Should().NotThrow();
    }

    #endregion

    #region StopAll Tests

    [Fact]
    public void StopAll_WithActiveWatchers_DoesNotThrow()
    {
        // Arrange
        _sut.AddWatchedFolder(_testDirectory);

        // Act
        var action = () => _sut.StopAll();

        // Assert
        action.Should().NotThrow();
    }

    [Fact]
    public void StopAll_WithNoWatchers_DoesNotThrow()
    {
        // Act
        var action = () => _sut.StopAll();

        // Assert
        action.Should().NotThrow();
    }

    #endregion

    #region StartWatching Tests

    [Fact]
    public void StartWatching_MultiplePaths_DoesNotThrow()
    {
        // Arrange
        var dir1 = Path.Combine(_testDirectory, "Dir1");
        var dir2 = Path.Combine(_testDirectory, "Dir2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        // Act
        var action = () => _sut.StartWatching([dir1, dir2]);

        // Assert
        action.Should().NotThrow();
    }

    [Fact]
    public void StartWatching_EmptyList_DoesNotThrow()
    {
        // Act
        var action = () => _sut.StartWatching([]);

        // Assert
        action.Should().NotThrow();
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Act
        var action = () =>
        {
            _sut.Dispose();
            _sut.Dispose();
        };

        // Assert
        action.Should().NotThrow();
    }

    #endregion

    #region RepositoryDiscovered Event Tests

    [Fact]
    public async Task RepositoryDiscovered_WhenGitDirCreated_RaisesEvent()
    {
        // Arrange
        _sut.AddWatchedFolder(_testDirectory);
        var repoPath = Path.Combine(_testDirectory, "NewRepo");

        // Act - create a new repo directory with .git
        Directory.CreateDirectory(repoPath);
        Directory.CreateDirectory(Path.Combine(repoPath, ".git"));

        // Wait for debounce and event processing
        await Task.Delay(800);

        // Assert
        _discoveredRepos.Should().Contain(repoPath);
    }

    #endregion
}
