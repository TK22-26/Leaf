#nullable enable
using System.IO;
using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.Services;

/// <summary>
/// Tests for the cascade rule on <see cref="RepositoryManagementService.RemoveRepository"/>.
/// Ensures parent removal correctly:
///   • cascades to <c>IsUserAdded == false</c> children (removes them).
///   • promotes <c>IsUserAdded == true</c> children to top-level
///     (clears <c>ParentRepositoryPath</c>, keeps the entry).
///   • recurses through deep chains, one level at a time (no
///     re-parenting — option A from the design discussion).
/// </summary>
public class RepositoryRemovalCascadeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _settings;
    private readonly RepositoryManagementService _service;

    public RepositoryRemovalCascadeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"leaf-repo-cascade-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        // Subdirectories that pretend to be repos so RepositoryInfo.Exists
        // returns true and the entries don't get pruned on load.
        for (int i = 1; i <= 5; i++)
        {
            var d = Path.Combine(_tempDir, $"r{i}");
            Directory.CreateDirectory(Path.Combine(d, ".git"));
        }
        _settings = new SettingsService(_tempDir);
        _service = new RepositoryManagementService(_settings);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private RepositoryInfo MakeRepo(string subdir, string? parentPath = null, bool isUserAdded = true)
    {
        var path = Path.Combine(_tempDir, subdir);
        return new RepositoryInfo
        {
            Path = path,
            Name = subdir,
            ParentRepositoryPath = parentPath,
            IsUserAdded = isUserAdded,
        };
    }

    [Fact]
    public void RemoveParent_CascadesAutoAddedChild()
    {
        var parent = MakeRepo("r1");
        var child = MakeRepo("r2", parentPath: parent.Path, isUserAdded: false);
        _service.AddRepository(parent);
        _service.AddRepository(child);

        _service.RemoveRepository(parent);

        _service.FindRepository(parent.Path).Should().BeNull("parent removed");
        _service.FindRepository(child.Path).Should().BeNull(
            "auto-added child cascades when its parent is removed");
    }

    [Fact]
    public void RemoveParent_PromotesUserAddedChild_KeepsEntry_ClearsParentLink()
    {
        var parent = MakeRepo("r1");
        var child = MakeRepo("r2", parentPath: parent.Path, isUserAdded: true);
        _service.AddRepository(parent);
        _service.AddRepository(child);

        _service.RemoveRepository(parent);

        _service.FindRepository(parent.Path).Should().BeNull();
        var promoted = _service.FindRepository(child.Path);
        promoted.Should().NotBeNull("user-added child survives parent removal");
        promoted!.ParentRepositoryPath.Should().BeNull(
            "promoted entry's parent link is cleared so the back-button stops pointing at a deleted repo");
        promoted.IsUserAdded.Should().BeTrue();
    }

    [Fact]
    public void RemoveDeepParent_CascadesRecursively_AllAutoAdded()
    {
        // super → mid → leaf, all auto-added. Removing super should
        // cascade through mid and take leaf with it. Option A: cascade
        // follows the chain one step at a time, no re-parenting.
        var super = MakeRepo("r1");
        var mid = MakeRepo("r2", parentPath: super.Path, isUserAdded: false);
        var leaf = MakeRepo("r3", parentPath: mid.Path, isUserAdded: false);
        _service.AddRepository(super);
        _service.AddRepository(mid);
        _service.AddRepository(leaf);

        _service.RemoveRepository(super);

        _service.FindRepository(super.Path).Should().BeNull();
        _service.FindRepository(mid.Path).Should().BeNull();
        _service.FindRepository(leaf.Path).Should().BeNull(
            "auto-added grandchild cascades through its auto-added parent");
    }

    [Fact]
    public void RemoveDeepParent_PromotesUserAddedDescendant_NoReparenting()
    {
        // super → mid (auto) → leaf (USER-ADDED). Removing super:
        //   • mid cascades (auto)
        //   • but cascading mid would promote leaf... wait — leaf's parent
        //     is mid, which is being cascade-removed. Leaf gets promoted
        //     during the recursion on mid. End state: super gone, mid gone,
        //     leaf survives at top-level with ParentRepositoryPath cleared.
        //     Option A: leaf is NOT re-parented to super (super is gone too).
        var super = MakeRepo("r1");
        var mid = MakeRepo("r2", parentPath: super.Path, isUserAdded: false);
        var leaf = MakeRepo("r3", parentPath: mid.Path, isUserAdded: true);
        _service.AddRepository(super);
        _service.AddRepository(mid);
        _service.AddRepository(leaf);

        _service.RemoveRepository(super);

        _service.FindRepository(super.Path).Should().BeNull();
        _service.FindRepository(mid.Path).Should().BeNull();
        var survivingLeaf = _service.FindRepository(leaf.Path);
        survivingLeaf.Should().NotBeNull(
            "user-added grandchild survives parent cascade");
        survivingLeaf!.ParentRepositoryPath.Should().BeNull(
            "no re-parenting — leaf stands alone at top level");
    }

    [Fact]
    public void RemoveParent_LeavesIndependentRepoAlone()
    {
        // An unrelated repo (no parent link) must be untouched by a
        // parent removal somewhere else in the list.
        var parent = MakeRepo("r1");
        var child = MakeRepo("r2", parentPath: parent.Path, isUserAdded: false);
        var unrelated = MakeRepo("r3");
        _service.AddRepository(parent);
        _service.AddRepository(child);
        _service.AddRepository(unrelated);

        _service.RemoveRepository(parent);

        _service.FindRepository(unrelated.Path).Should().NotBeNull();
        _service.FindRepository(unrelated.Path)!.ParentRepositoryPath.Should().BeNull();
    }

    [Fact]
    public void RemoveLeaf_DoesNotAffectParent()
    {
        // Sanity: removing a child doesn't cascade upward.
        var parent = MakeRepo("r1");
        var child = MakeRepo("r2", parentPath: parent.Path, isUserAdded: false);
        _service.AddRepository(parent);
        _service.AddRepository(child);

        _service.RemoveRepository(child);

        _service.FindRepository(parent.Path).Should().NotBeNull();
        _service.FindRepository(child.Path).Should().BeNull();
    }

    [Fact]
    public void AddRepository_PromotesExistingAutoAddedEntry_WhenUserExplicitlyReAdds()
    {
        // Auto-added first (e.g. via submodule open), then the user
        // explicitly invokes "Add Repository" pointing at the same path.
        // The entry should flip to IsUserAdded=true so a later parent
        // removal promotes rather than cascades.
        var parent = MakeRepo("r1");
        var child = MakeRepo("r2", parentPath: parent.Path, isUserAdded: false);
        _service.AddRepository(parent);
        _service.AddRepository(child);

        // Same path, but the new RepositoryInfo declares user intent.
        _service.AddRepository(MakeRepo("r2", parentPath: null, isUserAdded: true));

        var entry = _service.FindRepository(child.Path);
        entry.Should().NotBeNull();
        entry!.IsUserAdded.Should().BeTrue("explicit re-add promotes the existing auto-added entry");
    }

    [Fact]
    public void AddRepository_DoesNotDemoteUserAddedEntry_WhenSubmoduleOpenReAdds()
    {
        // Reverse of the test above: existing user-added entry, the
        // submodule-open path tries to add it again with IsUserAdded=false.
        // The flag should NOT regress to false.
        var parent = MakeRepo("r1");
        var child = MakeRepo("r2");
        _service.AddRepository(parent);
        _service.AddRepository(child);

        _service.AddRepository(MakeRepo("r2", parentPath: parent.Path, isUserAdded: false));

        var entry = _service.FindRepository(child.Path);
        entry.Should().NotBeNull();
        entry!.IsUserAdded.Should().BeTrue("user-added flag is sticky once set");
    }
}
