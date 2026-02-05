using FluentAssertions;
using Leaf.Models;
using Xunit;

namespace Leaf.Tests.Models;

public class WorktreeInfoTests
{
    #region DisplayName Tests

    [Fact]
    public void DisplayName_SimplePath_ReturnsFolderName()
    {
        // Arrange
        var worktree = new WorktreeInfo
        {
            Path = @"C:\repos\myproject"
        };

        // Act
        var displayName = worktree.DisplayName;

        // Assert
        displayName.Should().Be("myproject");
    }

    [Fact]
    public void DisplayName_PathWithTrailingSeparator_ReturnsFolderName()
    {
        // Arrange
        var worktree = new WorktreeInfo
        {
            Path = @"C:\repos\myproject\"
        };

        // Act
        var displayName = worktree.DisplayName;

        // Assert
        displayName.Should().Be("myproject");
    }

    [Fact]
    public void DisplayName_PathWithMultipleTrailingSeparators_ReturnsFolderName()
    {
        // Arrange
        var worktree = new WorktreeInfo
        {
            Path = @"C:\repos\myproject\\"
        };

        // Act
        var displayName = worktree.DisplayName;

        // Assert
        displayName.Should().Be("myproject");
    }

    [Fact]
    public void DisplayName_UnixPath_ReturnsFolderName()
    {
        // Arrange
        var worktree = new WorktreeInfo
        {
            Path = "/home/user/repos/myproject"
        };

        // Act
        var displayName = worktree.DisplayName;

        // Assert
        displayName.Should().Be("myproject");
    }

    [Fact]
    public void DisplayName_UnixPathWithTrailingSeparator_ReturnsFolderName()
    {
        // Arrange
        var worktree = new WorktreeInfo
        {
            Path = "/home/user/repos/myproject/"
        };

        // Act
        var displayName = worktree.DisplayName;

        // Assert
        displayName.Should().Be("myproject");
    }

    [Fact]
    public void DisplayName_WorktreeWithBranchSuffix_ReturnsFullFolderName()
    {
        // Arrange
        var worktree = new WorktreeInfo
        {
            Path = @"C:\repos\myproject-feature-branch"
        };

        // Act
        var displayName = worktree.DisplayName;

        // Assert
        displayName.Should().Be("myproject-feature-branch");
    }

    [Fact]
    public void DisplayName_EmptyPath_ReturnsEmptyString()
    {
        // Arrange
        var worktree = new WorktreeInfo
        {
            Path = ""
        };

        // Act
        var displayName = worktree.DisplayName;

        // Assert
        displayName.Should().BeEmpty();
    }

    #endregion

    #region Observable Property Tests

    [Fact]
    public void IsSelected_WhenSet_RaisesPropertyChanged()
    {
        // Arrange
        var worktree = new WorktreeInfo();
        var propertyChangedRaised = false;
        worktree.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(WorktreeInfo.IsSelected))
                propertyChangedRaised = true;
        };

        // Act
        worktree.IsSelected = true;

        // Assert
        propertyChangedRaised.Should().BeTrue();
        worktree.IsSelected.Should().BeTrue();
    }

    [Fact]
    public void IsLocked_WhenSet_RaisesPropertyChanged()
    {
        // Arrange
        var worktree = new WorktreeInfo();
        var propertyChangedRaised = false;
        worktree.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(WorktreeInfo.IsLocked))
                propertyChangedRaised = true;
        };

        // Act
        worktree.IsLocked = true;

        // Assert
        propertyChangedRaised.Should().BeTrue();
        worktree.IsLocked.Should().BeTrue();
    }

    [Fact]
    public void IsCurrent_WhenSet_RaisesPropertyChanged()
    {
        // Arrange
        var worktree = new WorktreeInfo();
        var propertyChangedRaised = false;
        worktree.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(WorktreeInfo.IsCurrent))
                propertyChangedRaised = true;
        };

        // Act
        worktree.IsCurrent = true;

        // Assert
        propertyChangedRaised.Should().BeTrue();
        worktree.IsCurrent.Should().BeTrue();
    }

    #endregion

    #region Default Values Tests

    [Fact]
    public void NewWorktreeInfo_HasDefaultValues()
    {
        // Arrange & Act
        var worktree = new WorktreeInfo();

        // Assert
        worktree.Path.Should().BeEmpty();
        worktree.HeadSha.Should().BeEmpty();
        worktree.BranchName.Should().BeNull();
        worktree.IsMainWorktree.Should().BeFalse();
        worktree.IsDetached.Should().BeFalse();
        worktree.IsLocked.Should().BeFalse();
        worktree.IsCurrent.Should().BeFalse();
        worktree.IsSelected.Should().BeFalse();
        worktree.IsExpanded.Should().BeFalse();
        worktree.LockReason.Should().BeNull();
    }

    #endregion

    #region Property Setters Tests

    [Fact]
    public void Properties_CanBeSetAndRetrieved()
    {
        // Arrange & Act
        var worktree = new WorktreeInfo
        {
            Path = @"C:\repos\test",
            HeadSha = "abc123def456",
            BranchName = "feature/test",
            IsMainWorktree = true,
            IsDetached = false,
            LockReason = "Testing",
            IsExpanded = true
        };

        // Assert
        worktree.Path.Should().Be(@"C:\repos\test");
        worktree.HeadSha.Should().Be("abc123def456");
        worktree.BranchName.Should().Be("feature/test");
        worktree.IsMainWorktree.Should().BeTrue();
        worktree.IsDetached.Should().BeFalse();
        worktree.LockReason.Should().Be("Testing");
        worktree.IsExpanded.Should().BeTrue();
    }

    #endregion
}
