using System.IO;
using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.Services;

/// <summary>
/// Tests for §5.14 <see cref="BranchColorPaletteRegistry"/>: built-in
/// presence + ordering, custom palette CRUD via the registry's public
/// surface, and the precedence/fallback rules.
/// </summary>
public class BranchColorPaletteRegistryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _settings;
    private readonly BranchColorPaletteRegistry _registry;

    public BranchColorPaletteRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "leaf-tests", Guid.NewGuid().ToString("N"));
        _settings = new SettingsService(_tempDir);
        _registry = new BranchColorPaletteRegistry(_settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void GetAll_ReturnsFourBuiltInsByDefault()
    {
        var all = _registry.GetAll();
        all.Should().HaveCount(4);
        all.Select(p => p.Id).Should().BeEquivalentTo(
            BranchColorPaletteRegistry.DefaultId,
            BranchColorPaletteRegistry.OkabeItoId,
            BranchColorPaletteRegistry.PastelId,
            BranchColorPaletteRegistry.HighContrastId);
        all.Should().AllSatisfy(p => p.IsBuiltIn.Should().BeTrue());
    }

    [Fact]
    public void GetById_UnknownIdFallsBackToDefault()
    {
        var palette = _registry.GetById("does-not-exist");
        palette.Id.Should().Be(BranchColorPaletteRegistry.DefaultId);
    }

    [Fact]
    public void GetById_NullOrEmptyFallsBackToDefault()
    {
        _registry.GetById(null).Id.Should().Be(BranchColorPaletteRegistry.DefaultId);
        _registry.GetById(string.Empty).Id.Should().Be(BranchColorPaletteRegistry.DefaultId);
        _registry.GetById("   ").Id.Should().Be(BranchColorPaletteRegistry.DefaultId);
    }

    [Fact]
    public void Default_AlwaysReturnsTheBuiltInDefaultPalette()
    {
        _registry.Default.Id.Should().Be(BranchColorPaletteRegistry.DefaultId);
        _registry.Default.IsBuiltIn.Should().BeTrue();
        _registry.Default.ParsedColors().Should().NotBeEmpty();
    }

    [Fact]
    public void AddOrUpdateCustom_RoundTripsThroughSettings()
    {
        var palette = new BranchColorPalette
        {
            Id = "test-1",
            DisplayName = "My palette",
            Colors = ["#FF0000", "#00FF00"],
        };

        _registry.AddOrUpdateCustom(palette);

        // Re-create the registry against the same settings folder to verify
        // it persisted, not just cached.
        var reload = new BranchColorPaletteRegistry(_settings);
        var stored = reload.GetById("test-1");
        stored.DisplayName.Should().Be("My palette");
        stored.IsBuiltIn.Should().BeFalse();
        stored.Colors.Should().BeEquivalentTo(["#FF0000", "#00FF00"]);
    }

    [Fact]
    public void AddOrUpdateCustom_RejectsBuiltInIds()
    {
        var palette = new BranchColorPalette
        {
            Id = BranchColorPaletteRegistry.DefaultId,
            DisplayName = "Hijack default",
            Colors = ["#FFFFFF"],
        };
        Action act = () => _registry.AddOrUpdateCustom(palette);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddOrUpdateCustom_RejectsEmptyColorList()
    {
        var palette = new BranchColorPalette
        {
            Id = "empty",
            DisplayName = "no colours",
            Colors = [],
        };
        Action act = () => _registry.AddOrUpdateCustom(palette);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddOrUpdateCustom_RejectsAllInvalidHexes()
    {
        var palette = new BranchColorPalette
        {
            Id = "garbage",
            DisplayName = "junk",
            Colors = ["not-a-color", "#ZZZZZZ"],
        };
        Action act = () => _registry.AddOrUpdateCustom(palette);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DeleteCustom_RemovesEntry()
    {
        _registry.AddOrUpdateCustom(new BranchColorPalette
        {
            Id = "to-delete",
            DisplayName = "tmp",
            Colors = ["#123456"],
        });

        _registry.DeleteCustom("to-delete");

        _registry.GetAll().Should().NotContain(p => p.Id == "to-delete");
    }

    [Fact]
    public void DeleteCustom_BuiltInIsNoOp()
    {
        _registry.DeleteCustom(BranchColorPaletteRegistry.DefaultId);
        // Built-in is still there.
        _registry.GetAll().Should().Contain(p => p.Id == BranchColorPaletteRegistry.DefaultId);
    }

    [Fact]
    public void AddOrUpdateCustom_FiresPalettesChanged()
    {
        var fired = 0;
        _registry.PalettesChanged += (_, _) => fired++;

        _registry.AddOrUpdateCustom(new BranchColorPalette
        {
            Id = "fires-event",
            DisplayName = "x",
            Colors = ["#000000"],
        });

        fired.Should().Be(1);
    }

    [Fact]
    public void DeleteCustom_FiresPalettesChangedOnlyWhenSomethingRemoved()
    {
        _registry.AddOrUpdateCustom(new BranchColorPalette
        {
            Id = "fires-once",
            DisplayName = "x",
            Colors = ["#000000"],
        });
        var fired = 0;
        _registry.PalettesChanged += (_, _) => fired++;

        _registry.DeleteCustom("fires-once");
        _registry.DeleteCustom("never-existed");

        fired.Should().Be(1);
    }

    [Fact]
    public void CloneBuiltInForEditing_AssignsNewIdAndCopyName()
    {
        var clone = _registry.CloneBuiltInForEditing(_registry.Default);
        clone.Id.Should().NotBe(_registry.Default.Id);
        clone.IsBuiltIn.Should().BeFalse();
        clone.DisplayName.Should().Contain(_registry.Default.DisplayName);
        clone.Colors.Should().BeEquivalentTo(_registry.Default.Colors);
    }
}
