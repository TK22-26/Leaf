#nullable enable
using System.IO;
using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using Leaf.Services;
using Leaf.Tests.Controls.Merge;
using Xunit;

namespace Leaf.Tests.Services.Merge;

/// <summary>
/// Pins the user-palette-override behaviour added in the post-V8 closeout.
/// Exercises <see cref="MergeThemeSwitcher.ApplyPaletteToUmbrella"/> against
/// a synthetic umbrella so the tests don't mutate
/// <see cref="Application.Current"/>.Resources — avoiding the race with
/// every other merge test that reads the process-global palette.
/// </summary>
public class MergeThemeSwitcherOverrideTests
{
    [StaFact]
    public void ApplyPaletteToUmbrella_WithValidOverride_AppendsOverrideDictionary()
    {
        var umbrella = BuildUmbrella();
        var tempFile = WriteOverride("Merge.Ours.Accent", Color.FromRgb(0xAB, 0xCD, 0xEF));
        try
        {
            var changed = MergeThemeSwitcher.ApplyPaletteToUmbrella(umbrella, isLight: false, customPalettePath: tempFile);

            changed.Should().BeTrue(because: "installing a new override is a palette change");
            var overrideBrush = FindInTree(umbrella, "Merge.Ours.Accent");
            overrideBrush.Should().NotBeNull(because: "override must be reachable from the umbrella after ApplyPalette");
            overrideBrush!.Color.Should().Be(Color.FromRgb(0xAB, 0xCD, 0xEF),
                because: "override is appended last and wins MergedDictionaries lookup");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [StaFact]
    public void ApplyPaletteToUmbrella_WithNullPath_RemovesPreviouslyAppliedOverride()
    {
        var umbrella = BuildUmbrella();
        var tempFile = WriteOverride("Merge.Theirs.Accent", Color.FromRgb(0x11, 0x22, 0x33));
        try
        {
            MergeThemeSwitcher.ApplyPaletteToUmbrella(umbrella, isLight: false, customPalettePath: tempFile);
            FindInTree(umbrella, "Merge.Theirs.Accent")!.Color
                .Should().Be(Color.FromRgb(0x11, 0x22, 0x33));

            MergeThemeSwitcher.ApplyPaletteToUmbrella(umbrella, isLight: false, customPalettePath: null);

            // Override cleared → base palette value should be back.
            FindInTree(umbrella, "Merge.Theirs.Accent")!.Color
                .Should().NotBe(Color.FromRgb(0x11, 0x22, 0x33),
                    because: "clearing the override must remove the overriding dictionary, not leave it stacked");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [StaFact]
    public void ApplyPaletteToUmbrella_WithMissingFile_LeavesBasePaletteUntouched()
    {
        var umbrella = BuildUmbrella();
        var before = FindInTree(umbrella, "Merge.Ours.Accent")!.Color;

        var act = () => MergeThemeSwitcher.ApplyPaletteToUmbrella(
            umbrella, isLight: false,
            customPalettePath: @"C:\this\path\does\not\exist.xaml");
        act.Should().NotThrow(
            because: "a missing override file is a setting not a crash — the user may type ahead of saving");

        FindInTree(umbrella, "Merge.Ours.Accent")!.Color
            .Should().Be(before, because: "a missing file must leave the base palette untouched");
    }

    [StaFact]
    public void ApplyPaletteToUmbrella_Repeated_DoesNotStackOverrides()
    {
        var umbrella = BuildUmbrella();
        var first = WriteOverride("Merge.Ours.Accent", Color.FromRgb(0x10, 0x20, 0x30));
        var second = WriteOverride("Merge.Ours.Accent", Color.FromRgb(0x40, 0x50, 0x60));
        try
        {
            MergeThemeSwitcher.ApplyPaletteToUmbrella(umbrella, isLight: false, customPalettePath: first);
            MergeThemeSwitcher.ApplyPaletteToUmbrella(umbrella, isLight: false, customPalettePath: second);

            // Second apply should REPLACE the first override, not stack.
            var override2 = FindInTree(umbrella, "Merge.Ours.Accent");
            override2!.Color.Should().Be(Color.FromRgb(0x40, 0x50, 0x60));

            // And there should be only one tagged override dict in the tree.
            CountTaggedOverrides(umbrella).Should().Be(1,
                because: "repeat calls must clean up the prior override dictionary before appending the new one");
        }
        finally
        {
            if (File.Exists(first)) File.Delete(first);
            if (File.Exists(second)) File.Delete(second);
        }
    }

    /// <summary>
    /// Build a synthetic palette-swap umbrella that mimics the real
    /// <c>MergePalette.xaml</c>: named with a URI whose path suffix is
    /// <c>MergePalette.xaml</c>, and containing a single
    /// <c>MergePaletteDark.xaml</c> style entry with real palette tokens.
    /// Avoids any dependency on <see cref="Application.Current"/>.
    /// </summary>
    private static ResourceDictionary BuildUmbrella()
    {
        // Ensure WPF's pack-URI resolver + BAML cache is warm before we
        // load the palette — MergePaletteTestFixture does that lazily and
        // guards against the xunit parallel-class race that otherwise
        // corrupts the internal resource cache.
        MergePaletteTestFixture.Ensure();
        var palette = new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/Leaf;component/Resources/Merge/MergePaletteDark.xaml",
                UriKind.Absolute),
        };
        var umbrella = new ResourceDictionary();
        umbrella.MergedDictionaries.Add(palette);
        return umbrella;
    }

    private static string WriteOverride(string key, Color colour)
    {
        var path = Path.GetTempFileName() + ".xaml";
        var xaml = $$"""
            <ResourceDictionary
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <SolidColorBrush x:Key="{{key}}" Color="#{{colour.A:X2}}{{colour.R:X2}}{{colour.G:X2}}{{colour.B:X2}}"/>
            </ResourceDictionary>
            """;
        File.WriteAllText(path, xaml);
        return path;
    }

    /// <summary>
    /// Walk every nested <see cref="ResourceDictionary"/> reachable from
    /// <paramref name="root"/> and return the last-wins brush for
    /// <paramref name="key"/>. Mirrors WPF's MergedDictionaries precedence
    /// (last wins) without depending on <see cref="ResourceDictionary"/>'s
    /// indexer semantics, which vary with whether the tree is hosted by
    /// an <see cref="Application"/>.
    /// </summary>
    private static SolidColorBrush? FindInTree(ResourceDictionary root, string key)
    {
        SolidColorBrush? latest = null;
        Visit(root);
        return latest;

        void Visit(ResourceDictionary rd)
        {
            foreach (var md in rd.MergedDictionaries) Visit(md);
            if (rd.Contains(key) && rd[key] is SolidColorBrush scb) latest = scb;
        }
    }

    private static int CountTaggedOverrides(ResourceDictionary root)
    {
        // ResourceDictionary.Contains walks MergedDictionaries too, which
        // would double-count a marker that only lives in a child dict
        // (umbrella "contains" the marker via its merged override). Walk
        // Keys (own-entries only) to pin the count to the dictionaries
        // that actually carry the marker.
        int count = 0;
        Visit(root);
        return count;

        void Visit(ResourceDictionary rd)
        {
            foreach (var k in rd.Keys)
            {
                if (Equals(k, MergeThemeSwitcher.CustomOverrideMarker)) { count++; break; }
            }
            foreach (var md in rd.MergedDictionaries) Visit(md);
        }
    }
}
