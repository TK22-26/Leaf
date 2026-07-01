#nullable enable
using System.Collections.ObjectModel;
using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Leaf.ViewModels;
using Moq;
using Xunit;

namespace Leaf.Tests.ViewModels;

public class CommandPaletteViewModelTests
{
    [Fact]
    public void BranchMode_IncludesRemoteOnlyBranchesForPrWorkflowSearch()
    {
        var repo = new RepositoryInfo
        {
            Name = "repo",
            Path = "C:/repo",
            LocalBranches = new ObservableCollection<BranchInfo>
            {
                new()
                {
                    Name = "main",
                    IsCurrent = true,
                    TipSha = "local-main"
                }
            },
            RemoteBranches = new ObservableCollection<BranchInfo>
            {
                new()
                {
                    Name = "origin/users/jacob/pr-123",
                    IsRemote = true,
                    RemoteName = "origin",
                    TipSha = "remote-pr"
                },
                new()
                {
                    Name = "origin/HEAD",
                    IsRemote = true,
                    RemoteName = "origin"
                }
            }
        };

        BranchInfo? selected = null;
        var vm = new CommandPaletteViewModel(
            RepositoryService(),
            () => repo,
            _ => { },
            branch => selected = branch);

        vm.OpenBranchSearch();
        vm.SearchText = "#users/jacob/pr-123";

        vm.FilteredResults.Should().ContainSingle();
        vm.FilteredResults[0].DisplayName.Should().Be("origin/users/jacob/pr-123");

        vm.Confirm();
        selected.Should().NotBeNull();
        selected!.IsRemote.Should().BeTrue();
        selected.RemoteName.Should().Be("origin");
    }

    private static IRepositoryManagementService RepositoryService()
    {
        var service = new Mock<IRepositoryManagementService>();
        service.SetupGet(s => s.PinnedRepositories).Returns(new ObservableCollection<RepositoryInfo>());
        service.SetupGet(s => s.RecentRepositories).Returns(new ObservableCollection<RepositoryInfo>());
        service.SetupGet(s => s.RepositoryGroups).Returns(new ObservableCollection<RepositoryGroup>());
        service.SetupGet(s => s.RepositoryRootItems).Returns(new ObservableCollection<object>());
        return service.Object;
    }
}
