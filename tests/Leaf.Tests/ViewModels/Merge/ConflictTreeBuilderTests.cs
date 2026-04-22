#nullable enable
using FluentAssertions;
using Leaf.Models;
using Leaf.ViewModels.Merge;
using Xunit;

namespace Leaf.Tests.ViewModels.Merge;

/// <summary>
/// Unit tests for <see cref="ConflictTreeBuilder"/>. Covers folder grouping,
/// separator normalization, resolved-vs-unresolved rollup, and the "folders
/// before files" ordering invariant that drives the <c>ConflictFileTree</c>
/// display.
/// </summary>
public class ConflictTreeBuilderTests
{
    private static ConflictInfo File(string path, bool resolved = false, int conflictCount = 1) =>
        new()
        {
            FilePath = path,
            IsResolved = resolved,
            ConflictCount = conflictCount,
        };

    [Fact]
    public void EmptyInput_ReturnsEmptyList()
    {
        var tree = ConflictTreeBuilder.Build(Array.Empty<ConflictInfo>());
        tree.Should().BeEmpty();
    }

    [Fact]
    public void RootLevelFiles_ReturnFlatLeaves()
    {
        var tree = ConflictTreeBuilder.Build(new[]
        {
            File("a.cs"),
            File("b.cs"),
        });

        tree.Should().HaveCount(2);
        tree.All(n => n.IsFolder).Should().BeFalse();
        tree.Select(n => n.DisplayName).Should().BeEquivalentTo(new[] { "a.cs", "b.cs" },
            config => config.WithStrictOrdering());
    }

    [Fact]
    public void NestedPaths_GroupByFolder()
    {
        var tree = ConflictTreeBuilder.Build(new[]
        {
            File("src/Foo.cs"),
            File("src/Bar.cs"),
            File("docs/readme.md"),
        });

        tree.Should().HaveCount(2);
        tree[0].IsFolder.Should().BeTrue();
        tree[0].DisplayName.Should().Be("docs",
            because: "folders sort alphabetically — 'docs' precedes 'src'");
        tree[0].Children.Single().DisplayName.Should().Be("readme.md");

        tree[1].DisplayName.Should().Be("src");
        tree[1].Children.Select(c => c.DisplayName).Should().BeEquivalentTo(
            new[] { "Bar.cs", "Foo.cs" },
            config => config.WithStrictOrdering());
    }

    [Fact]
    public void MixedFoldersAndRootFiles_FoldersComeFirst()
    {
        var tree = ConflictTreeBuilder.Build(new[]
        {
            File("zeta.cs"),
            File("src/Foo.cs"),
            File("alpha.cs"),
        });

        tree.Should().HaveCount(3);
        tree[0].IsFolder.Should().BeTrue();
        tree[1].DisplayName.Should().Be("alpha.cs",
            because: "root files are sorted alphabetically after folders");
        tree[2].DisplayName.Should().Be("zeta.cs");
    }

    [Fact]
    public void WindowsPathSeparators_NormalizedWithUnix()
    {
        var tree = ConflictTreeBuilder.Build(new[]
        {
            File(@"src\Foo.cs"),
            File("src/Bar.cs"),
        });

        tree.Should().HaveCount(1, because: "both paths share the same folder after normalization");
        tree[0].DisplayName.Should().Be("src");
        tree[0].Children.Should().HaveCount(2);
    }

    [Fact]
    public void DeepNesting_PreservedThroughTree()
    {
        var tree = ConflictTreeBuilder.Build(new[]
        {
            File("a/b/c/d/deep.cs"),
        });

        var depth = 0;
        var node = tree[0];
        while (node.IsFolder)
        {
            depth++;
            node = node.Children.Single();
        }

        depth.Should().Be(4, because: "a/b/c/d → four folder levels before the leaf");
        node.DisplayName.Should().Be("deep.cs");
    }

    [Fact]
    public void UnresolvedCount_AggregatesUpTree()
    {
        var tree = ConflictTreeBuilder.Build(new[]
        {
            File("src/a.cs", resolved: false, conflictCount: 2),
            File("src/b.cs", resolved: true,  conflictCount: 3),
            File("src/utils/c.cs", resolved: false, conflictCount: 1),
        });

        var srcFolder = tree.Single(n => n.DisplayName == "src");
        srcFolder.UnresolvedCount.Should().Be(3,
            because: "2 (a.cs unresolved) + 0 (b.cs resolved) + 1 (c.cs unresolved) = 3");
        srcFolder.IsResolved.Should().BeFalse();
    }

    [Fact]
    public void ResolvedFolder_HasZeroUnresolvedCount_AndIsResolvedTrue()
    {
        var tree = ConflictTreeBuilder.Build(new[]
        {
            File("done/x.cs", resolved: true, conflictCount: 5),
            File("done/y.cs", resolved: true, conflictCount: 2),
        });

        var folder = tree.Single(n => n.DisplayName == "done");
        folder.UnresolvedCount.Should().Be(0);
        folder.IsResolved.Should().BeTrue();
    }

    [Fact]
    public void FileLeaf_UnresolvedCount_UsesConflictCountWhenNotResolved()
    {
        var tree = ConflictTreeBuilder.Build(new[] { File("x.cs", resolved: false, conflictCount: 7) });
        tree.Single().UnresolvedCount.Should().Be(7);
    }

    [Fact]
    public void FileLeaf_UnresolvedCount_UsesExactConflictCount_NoClamp()
    {
        // ConflictInfo.ConflictCount defaults to 1 in the model and every
        // upstream path sets it ≥ 1 before the tree is built. No clamp here
        // — a zero on an unresolved file would be an upstream bug, and the
        // UI badge honestly reflecting "(0)" is the correct failure mode.
        var tree = ConflictTreeBuilder.Build(new[] { File("x.cs", resolved: false, conflictCount: 0) });
        tree.Single().UnresolvedCount.Should().Be(0);
    }

    [Fact]
    public void NullOrEmptyFilePath_Throws()
    {
        var bad = new ConflictInfo { FilePath = string.Empty };
        FluentActions.Invoking(() => ConflictTreeBuilder.Build(new[] { bad }))
            .Should().Throw<ArgumentException>(
                because: "every ConflictInfo from the git-plumbing path has a non-empty FilePath — " +
                         "silent fallback would produce a nameless leaf attached to the root");
    }

    [Fact]
    public void MultipleIndependentSubtrees_AlphabeticalAndCountCorrect()
    {
        var tree = ConflictTreeBuilder.Build(new[]
        {
            File("b/y.cs"),
            File("a/x.cs"),
            File("b/z.cs"),
        });

        tree.Should().HaveCount(2);
        tree[0].DisplayName.Should().Be("a");
        tree[0].Children.Single().DisplayName.Should().Be("x.cs");
        tree[1].DisplayName.Should().Be("b");
        tree[1].Children.Select(c => c.DisplayName).Should().BeEquivalentTo(
            new[] { "y.cs", "z.cs" },
            config => config.WithStrictOrdering());
    }

    [Fact]
    public void NullInput_Throws()
    {
        FluentActions.Invoking(() => ConflictTreeBuilder.Build(null!))
            .Should().Throw<ArgumentNullException>();
    }
}
