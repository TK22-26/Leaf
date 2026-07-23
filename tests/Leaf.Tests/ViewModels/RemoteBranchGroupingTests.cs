using FluentAssertions;
using Leaf.Models;
using Leaf.ViewModels;
using Xunit;

namespace Leaf.Tests.ViewModels;

/// <summary>
/// Tests for the REMOTE-sidebar grouping (MainViewModel.BuildRemoteGroups /
/// ResolveRemoteNamespace). Regression guard for the bug where orphaned
/// refs/remotes/&lt;x&gt;/* hierarchies (no configured remote) were dumped
/// under the origin node via a `RemoteName ?? "origin"` fallback and
/// masqueraded as origin branches that fetch/prune could never clear.
/// </summary>
public class RemoteBranchGroupingTests
{
    private static BranchInfo Remote(string canonical, string? libgit2RemoteName, string tip = "abc1234")
    {
        // FriendlyName for refs/remotes/X/Y is "X/Y".
        var friendly = canonical.StartsWith("refs/remotes/") ? canonical["refs/remotes/".Length..] : canonical;
        return new BranchInfo
        {
            FullName = canonical,
            Name = friendly,
            IsRemote = true,
            RemoteName = libgit2RemoteName,
            TipSha = tip,
        };
    }

    private static RemoteInfo Origin() => new()
    {
        Name = "origin",
        Url = "https://dev.azure.com/Org/Proj/_git/Proj",
    };

    // ─── ResolveRemoteNamespace ─────────────────────────────────────────

    [Fact]
    public void Resolve_OriginBranch_ReturnsOrigin()
    {
        var b = Remote("refs/remotes/origin/develop", "origin");
        MainViewModel.ResolveRemoteNamespace(b, ["origin"]).Should().Be("origin");
    }

    [Fact]
    public void Resolve_OrphanedRef_ReturnsItsOwnNamespace_NotOrigin()
    {
        // libgit2 returns null RemoteName for a ref with no configured remote.
        var b = Remote("refs/remotes/esm-c2/16e7eb724bd1b2fd", libgit2RemoteName: null);
        MainViewModel.ResolveRemoteNamespace(b, ["origin"]).Should().Be("esm-c2");
    }

    [Fact]
    public void Resolve_RemoteNameContainingSlash_LongestMatchWins()
    {
        var b = Remote("refs/remotes/team/origin/main", null);
        MainViewModel.ResolveRemoteNamespace(b, ["origin", "team/origin"]).Should().Be("team/origin");
    }

    [Fact]
    public void Resolve_NonCanonicalFullName_FallsBackToFriendlyFirstSegment()
    {
        var b = new BranchInfo { FullName = "", Name = "upstream/feature/x", IsRemote = true, RemoteName = null };
        MainViewModel.ResolveRemoteNamespace(b, ["origin"]).Should().Be("upstream");
    }

    // ─── BuildRemoteGroups ──────────────────────────────────────────────

    [Fact]
    public void Build_OrphanedRefs_GetOwnGroups_NeverUnderOrigin()
    {
        var branches = new[]
        {
            Remote("refs/remotes/origin/develop", "origin"),
            Remote("refs/remotes/origin/master", "origin"),
            Remote("refs/remotes/esm-c2/16e7eb724bd1b2fd", null),
            Remote("refs/remotes/esm-c3/286ae7a51703d6b0", null),
            Remote("refs/remotes/esm-cand/406752ecb0e26e00", null),
        };

        var groups = MainViewModel.BuildRemoteGroups(branches, [Origin()], "origin");

        // origin holds ONLY its real branches — none of the esm-* debris.
        var origin = groups.Single(g => g.Name == "origin");
        origin.IsOrphaned.Should().BeFalse();
        origin.DirectoryGroups.Should().BeEmpty("develop/master have no '/' prefix");
        origin.Branches.Select(b => b.Name).Should().BeEquivalentTo("develop", "master");

        // Each orphaned namespace is its own flagged group, separate from origin.
        var orphaned = groups.Where(g => g.IsOrphaned).Select(g => g.Name).ToList();
        orphaned.Should().BeEquivalentTo("esm-c2", "esm-c3", "esm-cand");
        groups.Single(g => g.Name == "esm-c2").ServiceDisplayName.Should().Contain("Local-only");

        // Configured remotes sort before orphaned ones.
        groups[0].Name.Should().Be("origin");
        groups.Skip(1).Should().OnlyContain(g => g.IsOrphaned);
    }

    [Fact]
    public void Build_NormalRepo_SingleOriginGroup_WithDirectoryFolders()
    {
        var branches = new[]
        {
            Remote("refs/remotes/origin/develop", "origin"),
            Remote("refs/remotes/origin/feature/foo", "origin"),
            Remote("refs/remotes/origin/feature/bar", "origin"),
        };

        var groups = MainViewModel.BuildRemoteGroups(branches, [Origin()], "origin");

        groups.Should().ContainSingle();
        var origin = groups[0];
        origin.IsOrphaned.Should().BeFalse();
        origin.IsDefault.Should().BeTrue();
        origin.Branches.Select(b => b.Name).Should().BeEquivalentTo("develop");
        origin.DirectoryGroups.Should().ContainSingle(d => d.Name == "feature");
    }

    [Fact]
    public void Build_ConfiguredRemoteWithNoBranches_StillGetsGroup()
    {
        var upstream = new RemoteInfo { Name = "upstream", Url = "https://github.com/o/r" };
        var branches = new[] { Remote("refs/remotes/origin/develop", "origin") };

        var groups = MainViewModel.BuildRemoteGroups(branches, [Origin(), upstream], "origin");

        groups.Select(g => g.Name).Should().BeEquivalentTo("origin", "upstream");
        groups.Single(g => g.Name == "upstream").Branches.Should().BeEmpty();
        groups.Should().OnlyContain(g => !g.IsOrphaned);
    }
}
