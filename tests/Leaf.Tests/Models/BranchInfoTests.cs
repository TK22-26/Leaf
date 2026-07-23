using FluentAssertions;
using Leaf.Models;
using Xunit;

namespace Leaf.Tests.Models;

public class BranchInfoTests
{
    #region DisplayName Tests

    // The sidebar tree renders the first path segment of Name as a folder
    // (MainViewModel.BuildDirectoryGrouping groups by the first '/'), so a
    // branch shown under a folder must display only the remainder — never
    // repeat the folder's own name.

    [Fact]
    public void DisplayName_BranchUnderFolder_StripsFolderPrefix()
    {
        var branch = new BranchInfo { Name = "task/CULV-10-wire-support" };

        branch.DisplayName.Should().Be("CULV-10-wire-support");
    }

    [Fact]
    public void DisplayName_BranchWithNoFolder_ReturnsNameUnchanged()
    {
        var branch = new BranchInfo { Name = "develop" };

        branch.DisplayName.Should().Be("develop");
    }

    [Fact]
    public void DisplayName_DeeperPath_StripsOnlyFirstSegment()
    {
        // Grouping only nests one level, so a branch under the "feature"
        // folder keeps the rest of its path as a single leaf label.
        var branch = new BranchInfo { Name = "feature/foo/bar" };

        branch.DisplayName.Should().Be("foo/bar");
    }

    [Fact]
    public void DisplayName_GitFlowFeature_StripsPrefix()
    {
        var branch = new BranchInfo { Name = "feature/issue-42" };

        branch.DisplayName.Should().Be("issue-42");
    }

    [Fact]
    public void DisplayName_LeadingSlash_LeftUnchanged()
    {
        // A name whose only slash is at position 0 is not foldered
        // (BuildDirectoryGrouping requires slashIndex > 0), so DisplayName
        // must mirror that and leave it untouched.
        var branch = new BranchInfo { Name = "/weird" };

        branch.DisplayName.Should().Be("/weird");
    }

    [Fact]
    public void DisplayName_Empty_ReturnsEmpty()
    {
        var branch = new BranchInfo { Name = string.Empty };

        branch.DisplayName.Should().BeEmpty();
    }

    [Fact]
    public void DisplayName_DoesNotMutateName()
    {
        // Name is load-bearing for checkout/commands; DisplayName is
        // purely presentational and must not alter it.
        var branch = new BranchInfo { Name = "task/CULV-10" };

        _ = branch.DisplayName;

        branch.Name.Should().Be("task/CULV-10");
    }

    #endregion
}
