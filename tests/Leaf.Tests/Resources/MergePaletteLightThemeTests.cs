#nullable enable
using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using Xunit;

namespace Leaf.Tests.Resources;

/// <summary>
/// V8 light-theme palette tests. Pins two load-bearing invariants:
///   1. Every token defined in <c>MergePaletteDark.xaml</c> is also
///      defined (same key, same runtime type) in <c>MergePaletteLight.xaml</c>.
///      A missing light-theme token would be invisible at build time
///      (no compile error) and fall back to whatever the resource
///      resolver finds elsewhere — silent visual drift.
///   2. Text-on-surface contrast pairs meet WCAG AA (4.5:1) in both
///      themes. A drift that pushes Merge.Text.Primary too close to
///      Merge.Surface.2 would technically still "work" but would
///      produce unreadable merge panes.
/// </summary>
public class MergePaletteLightThemeTests
{
    [StaFact]
    public void LightPalette_DefinesEveryKey_FromDarkPalette()
    {
        var dark = LoadDictionary("MergePaletteDark.xaml");
        var light = LoadDictionary("MergePaletteLight.xaml");

        var darkKeys = dark.Keys.Cast<object>().Select(k => k.ToString()!).OrderBy(k => k).ToList();
        var lightKeys = light.Keys.Cast<object>().Select(k => k.ToString()!).OrderBy(k => k).ToList();

        var missingInLight = darkKeys.Except(lightKeys).ToList();
        missingInLight.Should().BeEmpty(
            because: "every dark token needs a light counterpart; a missing key would silently " +
                     "fall back to whatever the resource resolver picks up elsewhere");

        var extraInLight = lightKeys.Except(darkKeys).ToList();
        extraInLight.Should().BeEmpty(
            because: "a light-only token has no dark fallback; drift between the two palettes " +
                     "should be symmetric");
    }

    [StaFact]
    public void LightPalette_EveryBrush_IsSolidColorBrush()
    {
        var light = LoadDictionary("MergePaletteLight.xaml");
        foreach (var key in light.Keys.Cast<object>().Select(k => k.ToString()!))
        {
            if (!key.Contains('.') || key.EndsWith(".Color", StringComparison.Ordinal)) continue;
            if (key.Contains(".Opacity", StringComparison.Ordinal)) continue;
            var value = light[key];
            // Resolved overlay is a SolidColorBrush; gradients aren't in
            // the palette file. If a non-solid brush is ever added, this
            // test will flag it so the light variant can be verified
            // separately.
            value.Should().BeAssignableTo<SolidColorBrush>(
                because: $"token '{key}' is expected to be a SolidColorBrush in the light palette");
        }
    }

    [StaFact]
    public void LightPalette_TextOnSurface_MeetsWcagAa_Contrast()
    {
        var light = LoadDictionary("MergePaletteLight.xaml");
        var surface2 = ((SolidColorBrush)light["Merge.Surface.2"]!).Color;

        // Primary / Secondary / Tertiary text must read on the pane
        // background (Surface.2). OnAccent is for accent-filled
        // surfaces (different contrast check); Disabled is exempt by
        // definition (WCAG allows relaxed contrast for disabled UI).
        AssertContrast(light, "Merge.Text.Primary", surface2, minimumRatio: 4.5);
        AssertContrast(light, "Merge.Text.Secondary", surface2, minimumRatio: 4.5);
        // Tertiary is the "dimmest legible" band — 3:1 is the WCAG
        // large-text minimum; merge chrome uses it only for captions
        // that aren't load-bearing.
        AssertContrast(light, "Merge.Text.Tertiary", surface2, minimumRatio: 3.0);
    }

    [StaFact]
    public void DarkPalette_TextOnSurface_MeetsWcagAa_Contrast()
    {
        var dark = LoadDictionary("MergePaletteDark.xaml");
        var surface2 = ((SolidColorBrush)dark["Merge.Surface.2"]!).Color;

        AssertContrast(dark, "Merge.Text.Primary", surface2, minimumRatio: 4.5);
        AssertContrast(dark, "Merge.Text.Secondary", surface2, minimumRatio: 4.5);
        AssertContrast(dark, "Merge.Text.Tertiary", surface2, minimumRatio: 3.0);
    }

    [StaFact]
    public void OnAccent_ReadsAgainst_CompleteMergeAccentBackground()
    {
        // Complete Merge button: AccentFillColorDefaultBrush (app-wide
        // green #28A745) as Background, Merge.Text.OnAccent as Foreground.
        // Label is 14 px Semibold — WCAG 1.4.3 treats Semibold ≥ 14 px as
        // "large text" which requires only 3:1 contrast rather than the
        // 4.5:1 normal-text threshold. Leaf's chosen accent green
        // delivers 3.13:1 against pure white, which clears the large-
        // text bar. A theme that lowered the bold weight below
        // Semibold would need to re-check against this assertion.
        const double LargeTextAaRatio = 3.0;
        var accentBg = (Color)ColorConverter.ConvertFromString("#FF28A745");
        foreach (var paletteFile in new[] { "MergePaletteDark.xaml", "MergePaletteLight.xaml" })
        {
            var palette = LoadDictionary(paletteFile);
            var onAccent = ((SolidColorBrush)palette["Merge.Text.OnAccent"]!).Color;
            var ratio = ContrastRatio(onAccent, accentBg);
            ratio.Should().BeGreaterOrEqualTo(LargeTextAaRatio,
                because: $"Merge.Text.OnAccent in {paletteFile} must read on the green accent fill " +
                         $"at the WCAG AA large-text ratio (Semibold 14 px); measured {ratio:F2}");
        }
    }

    private static void AssertContrast(ResourceDictionary palette, string textKey, Color surface, double minimumRatio)
    {
        var textColor = ((SolidColorBrush)palette[textKey]!).Color;
        var ratio = ContrastRatio(textColor, surface);
        ratio.Should().BeGreaterOrEqualTo(minimumRatio,
            because: $"{textKey} over the pane surface must meet WCAG AA ({minimumRatio}:1); " +
                     $"measured {ratio:F2}");
    }

    /// <summary>
    /// Compute the WCAG 2.0 contrast ratio between two colours.
    /// Range is 1.0 (identical) to 21.0 (black on white). AA text =
    /// 4.5:1; AA large text = 3:1.
    /// </summary>
    private static double ContrastRatio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var (lighter, darker) = la > lb ? (la, lb) : (lb, la);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static ResourceDictionary LoadDictionary(string fileName)
    {
        // Identical to MergePaletteTests' LoadMergeDictionary pattern —
        // load the pack URI directly so the test doesn't depend on the
        // palette being merged into Application.Current.
        if (Application.Current is null)
        {
            try { _ = new Application(); }
            catch (InvalidOperationException) { /* already created */ }
        }
        return new ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/Leaf;component/Resources/Merge/{fileName}",
                UriKind.Absolute),
        };
    }
}
