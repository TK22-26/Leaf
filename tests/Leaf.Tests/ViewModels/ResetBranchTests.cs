using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.ViewModels;

public class ResetBranchTests
{
    [Fact]
    public void ResetCurrentBranchRequest_StoresCommitAndMode()
    {
        var commit = new CommitInfo { Sha = "abc1234567", MessageShort = "test" };
        var request = new ResetCurrentBranchRequest(commit, GitResetMode.Soft);

        request.Commit.Should().BeSameAs(commit);
        request.Mode.Should().Be(GitResetMode.Soft);
    }

    [Fact]
    public void ResetCurrentBranchRequest_RecordEquality_Works()
    {
        var commit = new CommitInfo { Sha = "abc1234567", MessageShort = "test" };
        var a = new ResetCurrentBranchRequest(commit, GitResetMode.Hard);
        var b = new ResetCurrentBranchRequest(commit, GitResetMode.Hard);
        var c = new ResetCurrentBranchRequest(commit, GitResetMode.Soft);

        a.Should().Be(b);
        a.Should().NotBe(c);
    }
}
