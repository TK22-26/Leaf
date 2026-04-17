using FluentAssertions;
using Leaf.Models;
using Xunit;

namespace Leaf.Tests.Models;

/// <summary>
/// Guards against preset-list drift: every tool that ships with Leaf
/// needs both a Diff and a Merge entry, every arg template uses the
/// documented placeholders, and TryGet is case-insensitive.
/// </summary>
public class ExternalToolPresetsTests
{
    [Fact]
    public void All_HasDiffAndMerge_ForEveryName()
    {
        var namesByKind = ExternalToolPresets.All
            .GroupBy(t => t.Name)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Kind).ToHashSet());

        foreach (var (name, kinds) in namesByKind)
        {
            kinds.Should().Contain(ExternalToolKind.Diff,
                $"preset '{name}' must expose a diff entry");
            kinds.Should().Contain(ExternalToolKind.Merge,
                $"preset '{name}' must expose a merge entry");
        }
    }

    [Theory]
    [InlineData("vscode")]
    [InlineData("bcomp")]
    [InlineData("kdiff3")]
    [InlineData("p4merge")]
    [InlineData("winmerge")]
    [InlineData("meld")]
    [InlineData("araxis")]
    public void TryGet_ResolvesKnownPresets(string name)
    {
        ExternalToolPresets.TryGet(name, ExternalToolKind.Diff).Should().NotBeNull();
        ExternalToolPresets.TryGet(name, ExternalToolKind.Merge).Should().NotBeNull();
    }

    [Fact]
    public void TryGet_IsCaseInsensitive()
    {
        ExternalToolPresets.TryGet("VSCODE", ExternalToolKind.Diff).Should().NotBeNull();
        ExternalToolPresets.TryGet("KdIfF3", ExternalToolKind.Merge).Should().NotBeNull();
    }

    [Fact]
    public void TryGet_UnknownName_ReturnsNull()
    {
        ExternalToolPresets.TryGet("does-not-exist", ExternalToolKind.Diff).Should().BeNull();
    }

    [Fact]
    public void DiffPresets_UseLocalAndRemotePlaceholders()
    {
        foreach (var preset in ExternalToolPresets.Diff)
        {
            preset.ArgsTemplate.Should().Contain("$LOCAL",
                $"{preset.Name} diff template must reference $LOCAL");
            preset.ArgsTemplate.Should().Contain("$REMOTE",
                $"{preset.Name} diff template must reference $REMOTE");
        }
    }

    [Fact]
    public void MergePresets_UseAllFourPlaceholders()
    {
        foreach (var preset in ExternalToolPresets.Merge)
        {
            preset.ArgsTemplate.Should().Contain("$LOCAL",
                $"{preset.Name} merge template must reference $LOCAL");
            preset.ArgsTemplate.Should().Contain("$REMOTE",
                $"{preset.Name} merge template must reference $REMOTE");
            preset.ArgsTemplate.Should().Contain("$MERGED",
                $"{preset.Name} merge template must reference $MERGED");
        }
    }

    [Fact]
    public void BuiltIn_IsMarkedBuiltIn()
    {
        ExternalTool.BuiltIn(ExternalToolKind.Diff).IsBuiltIn.Should().BeTrue();
        ExternalTool.BuiltIn(ExternalToolKind.Merge).IsBuiltIn.Should().BeTrue();
    }

    [Fact]
    public void Presets_AreNotFlaggedAsBuiltIn()
    {
        foreach (var preset in ExternalToolPresets.All)
        {
            preset.IsBuiltIn.Should().BeFalse(
                $"preset '{preset.Name}' must not use the built-in sentinel name");
        }
    }
}
