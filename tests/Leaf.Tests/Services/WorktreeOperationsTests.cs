using System.IO;
using FluentAssertions;
using Leaf.Services.Git.Operations;
using Xunit;

namespace Leaf.Tests.Services;

public class WorktreeOperationsTests
{
    #region ParseWorktreeListOutput Tests

    [Fact]
    public void ParseWorktreeListOutput_SingleMainWorktree_ReturnsOneWorktree()
    {
        // Arrange
        var output = """
            worktree /path/to/main
            HEAD abc123def456789012345678901234567890abcd
            branch refs/heads/main
            """;

        // Act
        var worktrees = WorktreeOperations.ParseWorktreeListOutput(output);

        // Assert
        worktrees.Should().HaveCount(1);
        worktrees[0].Path.Should().Be(Path.GetFullPath("/path/to/main"));
        worktrees[0].HeadSha.Should().Be("abc123def456789012345678901234567890abcd");
        worktrees[0].BranchName.Should().Be("main");
        worktrees[0].IsMainWorktree.Should().BeTrue();
        worktrees[0].IsDetached.Should().BeFalse();
        worktrees[0].IsLocked.Should().BeFalse();
    }

    [Fact]
    public void ParseWorktreeListOutput_MultipleWorktrees_ReturnsAllWorktrees()
    {
        // Arrange
        var output = """
            worktree /path/to/main
            HEAD abc123def456789012345678901234567890abcd
            branch refs/heads/main

            worktree /path/to/feature
            HEAD def456789012345678901234567890abcdef12
            branch refs/heads/feature/my-feature
            """;

        // Act
        var worktrees = WorktreeOperations.ParseWorktreeListOutput(output);

        // Assert
        worktrees.Should().HaveCount(2);

        worktrees[0].Path.Should().Be(Path.GetFullPath("/path/to/main"));
        worktrees[0].BranchName.Should().Be("main");
        worktrees[0].IsMainWorktree.Should().BeTrue();

        worktrees[1].Path.Should().Be(Path.GetFullPath("/path/to/feature"));
        worktrees[1].BranchName.Should().Be("feature/my-feature");
        worktrees[1].IsMainWorktree.Should().BeFalse();
    }

    [Fact]
    public void ParseWorktreeListOutput_DetachedWorktree_SetsIsDetachedTrue()
    {
        // Arrange
        var output = """
            worktree /path/to/main
            HEAD abc123def456789012345678901234567890abcd
            branch refs/heads/main

            worktree /path/to/detached
            HEAD def456789012345678901234567890abcdef12
            detached
            """;

        // Act
        var worktrees = WorktreeOperations.ParseWorktreeListOutput(output);

        // Assert
        worktrees.Should().HaveCount(2);

        worktrees[1].Path.Should().Be(Path.GetFullPath("/path/to/detached"));
        worktrees[1].IsDetached.Should().BeTrue();
        worktrees[1].BranchName.Should().BeNull();
    }

    [Fact]
    public void ParseWorktreeListOutput_LockedWorktreeWithoutReason_SetsIsLockedTrue()
    {
        // Arrange
        var output = """
            worktree /path/to/main
            HEAD abc123def456789012345678901234567890abcd
            branch refs/heads/main

            worktree /path/to/locked
            HEAD def456789012345678901234567890abcdef12
            branch refs/heads/feature
            locked
            """;

        // Act
        var worktrees = WorktreeOperations.ParseWorktreeListOutput(output);

        // Assert
        worktrees.Should().HaveCount(2);

        worktrees[1].Path.Should().Be(Path.GetFullPath("/path/to/locked"));
        worktrees[1].IsLocked.Should().BeTrue();
        worktrees[1].LockReason.Should().BeNull();
    }

    [Fact]
    public void ParseWorktreeListOutput_LockedWorktreeWithReason_SetsLockReason()
    {
        // Arrange
        var output = """
            worktree /path/to/main
            HEAD abc123def456789012345678901234567890abcd
            branch refs/heads/main

            worktree /path/to/locked
            HEAD def456789012345678901234567890abcdef12
            branch refs/heads/feature
            locked Work in progress
            """;

        // Act
        var worktrees = WorktreeOperations.ParseWorktreeListOutput(output);

        // Assert
        worktrees.Should().HaveCount(2);

        worktrees[1].Path.Should().Be(Path.GetFullPath("/path/to/locked"));
        worktrees[1].IsLocked.Should().BeTrue();
        worktrees[1].LockReason.Should().Be("Work in progress");
    }

    [Fact]
    public void ParseWorktreeListOutput_EmptyOutput_ReturnsEmptyList()
    {
        // Arrange
        var output = "";

        // Act
        var worktrees = WorktreeOperations.ParseWorktreeListOutput(output);

        // Assert
        worktrees.Should().BeEmpty();
    }

    [Fact]
    public void ParseWorktreeListOutput_WindowsStyleLineEndings_ParsesCorrectly()
    {
        // Arrange
        var output = "worktree C:\\Users\\test\\repo\r\nHEAD abc123def456789012345678901234567890abcd\r\nbranch refs/heads/main\r\n";

        // Act
        var worktrees = WorktreeOperations.ParseWorktreeListOutput(output);

        // Assert
        worktrees.Should().HaveCount(1);
        worktrees[0].Path.Should().Be("C:\\Users\\test\\repo");
        worktrees[0].BranchName.Should().Be("main");
    }

    [Fact]
    public void ParseWorktreeListOutput_WindowsPath_ParsesCorrectly()
    {
        // Arrange
        var output = """
            worktree C:\Users\test\repos\project
            HEAD abc123def456789012345678901234567890abcd
            branch refs/heads/main

            worktree C:\Users\test\repos\project-feature
            HEAD def456789012345678901234567890abcdef12
            branch refs/heads/feature/test
            """;

        // Act
        var worktrees = WorktreeOperations.ParseWorktreeListOutput(output);

        // Assert
        worktrees.Should().HaveCount(2);
        worktrees[0].Path.Should().Be(@"C:\Users\test\repos\project");
        worktrees[1].Path.Should().Be(@"C:\Users\test\repos\project-feature");
    }

    [Fact]
    public void ParseWorktreeListOutput_ThreeWorktrees_FirstIsMainRestAreNot()
    {
        // Arrange
        var output = """
            worktree /path/to/main
            HEAD abc123def456789012345678901234567890abcd
            branch refs/heads/main

            worktree /path/to/feature1
            HEAD def456789012345678901234567890abcdef12
            branch refs/heads/feature1

            worktree /path/to/feature2
            HEAD 789012345678901234567890abcdef12345678
            branch refs/heads/feature2
            """;

        // Act
        var worktrees = WorktreeOperations.ParseWorktreeListOutput(output);

        // Assert
        worktrees.Should().HaveCount(3);
        worktrees[0].IsMainWorktree.Should().BeTrue();
        worktrees[1].IsMainWorktree.Should().BeFalse();
        worktrees[2].IsMainWorktree.Should().BeFalse();
    }

    [Fact]
    public void ParseWorktreeListOutput_ComplexScenario_ParsesAllFieldsCorrectly()
    {
        // Arrange - Main worktree, a feature worktree, a locked worktree with reason, and a detached worktree
        var output = """
            worktree /repos/project
            HEAD abc123def456789012345678901234567890abcd
            branch refs/heads/main

            worktree /repos/project-feature
            HEAD def456789012345678901234567890abcdef12
            branch refs/heads/feature/cool-stuff

            worktree /repos/project-locked
            HEAD 111222333444555666777888999000aaabbbccc
            branch refs/heads/release/v2.0
            locked Do not delete - important work

            worktree /repos/project-detached
            HEAD 999888777666555444333222111000cccbbbaaa
            detached
            """;

        // Act
        var worktrees = WorktreeOperations.ParseWorktreeListOutput(output);

        // Assert
        worktrees.Should().HaveCount(4);

        // Main worktree
        worktrees[0].Path.Should().Be(Path.GetFullPath("/repos/project"));
        worktrees[0].BranchName.Should().Be("main");
        worktrees[0].IsMainWorktree.Should().BeTrue();
        worktrees[0].IsDetached.Should().BeFalse();
        worktrees[0].IsLocked.Should().BeFalse();

        // Feature worktree
        worktrees[1].Path.Should().Be(Path.GetFullPath("/repos/project-feature"));
        worktrees[1].BranchName.Should().Be("feature/cool-stuff");
        worktrees[1].IsMainWorktree.Should().BeFalse();
        worktrees[1].IsDetached.Should().BeFalse();
        worktrees[1].IsLocked.Should().BeFalse();

        // Locked worktree
        worktrees[2].Path.Should().Be(Path.GetFullPath("/repos/project-locked"));
        worktrees[2].BranchName.Should().Be("release/v2.0");
        worktrees[2].IsMainWorktree.Should().BeFalse();
        worktrees[2].IsDetached.Should().BeFalse();
        worktrees[2].IsLocked.Should().BeTrue();
        worktrees[2].LockReason.Should().Be("Do not delete - important work");

        // Detached worktree
        worktrees[3].Path.Should().Be(Path.GetFullPath("/repos/project-detached"));
        worktrees[3].BranchName.Should().BeNull();
        worktrees[3].IsMainWorktree.Should().BeFalse();
        worktrees[3].IsDetached.Should().BeTrue();
        worktrees[3].IsLocked.Should().BeFalse();
    }

    #endregion

    #region GenerateDefaultWorktreePath Tests

    [Fact]
    public void GenerateDefaultWorktreePath_SimpleBranchName_ReturnsSiblingPath()
    {
        // Arrange
        var repoPath = @"C:\repos\myproject";
        var branchName = "feature";

        // Act
        var result = WorktreeOperations.GenerateDefaultWorktreePath(repoPath, branchName);

        // Assert
        result.Should().Be(@"C:\repos\myproject-feature");
    }

    [Fact]
    public void GenerateDefaultWorktreePath_BranchWithSlash_ReplacesSlashWithDash()
    {
        // Arrange
        var repoPath = @"C:\repos\myproject";
        var branchName = "feature/my-cool-feature";

        // Act
        var result = WorktreeOperations.GenerateDefaultWorktreePath(repoPath, branchName);

        // Assert
        result.Should().Be(@"C:\repos\myproject-feature-my-cool-feature");
    }

    [Fact]
    public void GenerateDefaultWorktreePath_BranchWithMultipleSlashes_ReplacesAllSlashes()
    {
        // Arrange
        var repoPath = @"C:\repos\myproject";
        var branchName = "feature/team/user/task";

        // Act
        var result = WorktreeOperations.GenerateDefaultWorktreePath(repoPath, branchName);

        // Assert
        result.Should().Be(@"C:\repos\myproject-feature-team-user-task");
    }

    [Fact]
    public void GenerateDefaultWorktreePath_PathWithDifferentSeparators_ReturnsSiblingPath()
    {
        // Arrange - On Windows, Path.Combine normalizes forward slashes to backslashes
        var repoPath = @"C:\Users\test\repos\myproject";
        var branchName = "develop";

        // Act
        var result = WorktreeOperations.GenerateDefaultWorktreePath(repoPath, branchName);

        // Assert
        result.Should().Be(@"C:\Users\test\repos\myproject-develop");
    }

    [Fact]
    public void GenerateDefaultWorktreePath_RepoPathWithTrailingSeparator_HandlesCorrectly()
    {
        // Arrange - Note: Path.GetDirectoryName on "C:\repos\myproject\" returns "C:\repos\myproject"
        // and then we append to the parent, which is "C:\repos"
        var repoPath = @"C:\repos\myproject";
        var branchName = "feature";

        // Act
        var result = WorktreeOperations.GenerateDefaultWorktreePath(repoPath + Path.DirectorySeparatorChar, branchName);

        // Assert - The path generation should produce the same result with or without trailing separator
        var expectedResult = WorktreeOperations.GenerateDefaultWorktreePath(repoPath, branchName);
        result.Should().Be(expectedResult);
    }

    [Fact]
    public void GenerateDefaultWorktreePath_GitFlowReleaseBranch_ReplacesSlash()
    {
        // Arrange
        var repoPath = @"C:\repos\myproject";
        var branchName = "release/v1.0.0";

        // Act
        var result = WorktreeOperations.GenerateDefaultWorktreePath(repoPath, branchName);

        // Assert
        result.Should().Be(@"C:\repos\myproject-release-v1.0.0");
    }

    [Fact]
    public void GenerateDefaultWorktreePath_GitFlowHotfixBranch_ReplacesSlash()
    {
        // Arrange
        var repoPath = @"C:\repos\myproject";
        var branchName = "hotfix/urgent-fix";

        // Act
        var result = WorktreeOperations.GenerateDefaultWorktreePath(repoPath, branchName);

        // Assert
        result.Should().Be(@"C:\repos\myproject-hotfix-urgent-fix");
    }

    #endregion
}
