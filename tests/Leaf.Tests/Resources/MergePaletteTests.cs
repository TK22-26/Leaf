using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using Xunit;

namespace Leaf.Tests.Resources;

/// <summary>
/// Verifies the V1 merge palette / typography tokens defined in
/// <c>src/Leaf/Resources/Merge/*.xaml</c>. These tokens are the foundation every
/// later wave builds on — every merge control looks them up at render time, so
/// renaming or accidentally removing one would quietly break the merge editor
/// without a build error.
/// </summary>
public class MergePaletteTests
{
    [StaFact]
    public void PaletteDictionary_ResolvesEveryExpectedBrushToken()
    {
        var dict = LoadMergeDictionary();

        foreach (var key in ExpectedBrushKeys)
        {
            dict[key].Should().BeOfType<SolidColorBrush>(
                because: $"token '{key}' must be a SolidColorBrush so merge controls can pick it up as a Brush");
        }
    }

    [StaFact]
    public void PaletteBrushes_AreFrozen_ForHotRenderPaths()
    {
        // V1 plan requirement: palette brushes must be Frozen so controls can
        // cache them cross-thread without dispatcher-affinity blowups and so
        // the per-frame cost of passing them to DrawingContext stays zero.
        var dict = LoadMergeDictionary();

        foreach (var key in ExpectedBrushKeys)
        {
            var brush = (SolidColorBrush)dict[key]!;
            brush.IsFrozen.Should().BeTrue(
                because: $"palette brush '{key}' must be Frozen — unfrozen brushes on the render path " +
                         "burn dispatcher time and can't cross thread boundaries");
        }
    }

    [StaFact]
    public void PaletteDictionary_ResolvesEveryExpectedColorToken()
    {
        var dict = LoadMergeDictionary();

        foreach (var (key, expected) in ExpectedColors)
        {
            dict[key].Should().BeOfType<Color>(because: $"token '{key}' must be a Color resource");
            var actual = (Color)dict[key]!;
            actual.Should().Be(expected, because: $"token '{key}' dark value must match the palette spec");
        }
    }

    [StaFact]
    public void PaletteDictionary_BrushColorsMatchTheColorTokens()
    {
        var dict = LoadMergeDictionary();

        // Every Merge.<group>.<role> Brush must read its .Color sibling. Assert
        // the resolved Color on each brush equals the Color token so a rename of
        // the backing Color key breaks loudly.
        foreach (var (brushKey, colorKey) in BrushColorPairs)
        {
            var brush = (SolidColorBrush)dict[brushKey]!;
            var color = (Color)dict[colorKey]!;
            brush.Color.Should().Be(color, because: $"{brushKey} must resolve to {colorKey}");
        }
    }

    [StaFact]
    public void TypographyDictionary_HasFontFamilyAndSizeTokens()
    {
        var dict = LoadMergeDictionary();

        dict["Merge.FontFamily.Chrome"].Should().BeOfType<FontFamily>();
        dict["Merge.FontFamily.Code"].Should().BeOfType<FontFamily>();

        dict["Merge.Code.Normal.Size"].Should().Be(13.0);
        dict["Merge.Code.Small.Size"].Should().Be(11.0);
        dict["Merge.Type.Body.Size"].Should().Be(14.0);
        dict["Merge.Type.Caption.Size"].Should().Be(12.0);
        dict["Merge.Type.Title.Size"].Should().Be(20.0);
    }

    [StaFact]
    public void TypographyDictionary_CodeFontFamily_IncludesJetBrainsMonoWithFallback()
    {
        var dict = LoadMergeDictionary();
        var codeFamily = (FontFamily)dict["Merge.FontFamily.Code"]!;

        // The fallback chain matters: JetBrains Mono embed must be tried first,
        // then system Consolas for environments where the embed did not register.
        codeFamily.Source.Should().Contain("JetBrains Mono");
        codeFamily.Source.Should().Contain("Consolas");
    }

    [Fact]
    public void PaletteBrushes_AreReadableFromASecondStaThread()
    {
        // V3 regression guard for a V1 bug: brushes whose Color was bound via
        // {DynamicResource Merge.*.Color} were attached to the STA thread that
        // first parsed the dictionary, so any later STA test that tried to
        // consume the brush blew up with "calling thread cannot access this
        // object because a different thread owns it". V3 switched to inline
        // brush+color definitions in MergePaletteDark.xaml — this test pins
        // that fix by deliberately crossing STA thread boundaries.
        SolidColorBrush? brush = null;
        RunOnNewSta(() =>
        {
            var dict = LoadMergeDictionary();
            brush = (SolidColorBrush)dict["Merge.State.Resolved"]!;
        });

        Exception? caught = null;
        Color color = default;
        RunOnNewSta(() =>
        {
            try { color = brush!.Color; }
            catch (Exception e) { caught = e; }
        });

        caught.Should().BeNull(because: "merge palette brushes must be thread-agnostic");
        color.Should().Be(Color.FromRgb(0x22, 0xC5, 0x5E));
    }

    private static void RunOnNewSta(Action a)
    {
        var t = new System.Threading.Thread(() => a());
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start();
        t.Join();
    }

    private static ResourceDictionary LoadMergeDictionary()
    {
        // Brushes in MergePalette.xaml reference Colors via {DynamicResource ...}
        // — that lookup only resolves inside a live resource scope, so we merge
        // the palette into Application.Resources once per test run.
        EnsureMergeDictionaryInApplication();
        return Application.Current.Resources;
    }

    private static readonly object _lock = new();
    private static bool _merged;

    private static void EnsureMergeDictionaryInApplication()
    {
        lock (_lock)
        {
            if (Application.Current is null)
            {
                // Another test class may have raced with us across a different
                // lock; tolerate the "already created" error rather than
                // double-creating the AppDomain-wide Application singleton.
                try { _ = new Application(); }
                catch (InvalidOperationException) { }
            }
            if (_merged) return;

            var dict = new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/Leaf;component/Resources/Merge/Merge.xaml",
                    UriKind.Absolute),
            };
            Application.Current!.Resources.MergedDictionaries.Add(dict);
            _merged = true;
        }
    }

    // Fluent 2 palette dark values. Renaming or retinting any entry here must be
    // a deliberate visual-audit decision — the test is the contract.
    private static readonly IReadOnlyDictionary<string, Color> ExpectedColors =
        new Dictionary<string, Color>
        {
            // Ours
            ["Merge.Ours.BgSubtle.Color"] = Color.FromArgb(0x1A, 0x2B, 0x4A, 0x6E),
            ["Merge.Ours.BgStrong.Color"] = Color.FromArgb(0x99, 0x2B, 0x4A, 0x6E),
            ["Merge.Ours.Accent.Color"] = Color.FromArgb(0xFF, 0x4A, 0x88, 0xC4),
            ["Merge.Ours.Text.Color"] = Color.FromArgb(0xFF, 0xB4, 0xD4, 0xFF),
            ["Merge.Ours.Border.Color"] = Color.FromArgb(0xFF, 0x2B, 0x4A, 0x6E),

            // Theirs
            ["Merge.Theirs.BgSubtle.Color"] = Color.FromArgb(0x1A, 0x1A, 0x50, 0x35),
            ["Merge.Theirs.BgStrong.Color"] = Color.FromArgb(0x99, 0x1A, 0x50, 0x35),
            ["Merge.Theirs.Accent.Color"] = Color.FromArgb(0xFF, 0x3D, 0xA0, 0x5C),
            ["Merge.Theirs.Text.Color"] = Color.FromArgb(0xFF, 0xA8, 0xE6, 0xB8),
            ["Merge.Theirs.Border.Color"] = Color.FromArgb(0xFF, 0x1A, 0x50, 0x35),

            // State
            ["Merge.State.Unresolved.Color"] = Color.FromArgb(0xFF, 0xE0, 0x44, 0x44),
            ["Merge.State.Resolved.Color"] = Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E),
            ["Merge.State.Manual.Color"] = Color.FromArgb(0xFF, 0xFF, 0xC4, 0x4D),

            // Surface
            ["Merge.Surface.1.Color"] = Color.FromArgb(0xFF, 0x18, 0x18, 0x18),
            ["Merge.Surface.2.Color"] = Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E),
            ["Merge.Surface.3.Color"] = Color.FromArgb(0xFF, 0x25, 0x25, 0x25),

            // Borders + text
            ["Merge.Border.Strong.Color"] = Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A),
            ["Merge.Text.Primary.Color"] = Color.FromArgb(0xFF, 0xE4, 0xE4, 0xE4),
            ["Merge.Text.Secondary.Color"] = Color.FromArgb(0xFF, 0xD0, 0xD0, 0xD0),
            ["Merge.Text.Tertiary.Color"] = Color.FromArgb(0xFF, 0x88, 0x88, 0x88),

            // Derived / closeout tokens — pinned so renames fail loudly.
            ["Merge.State.Resolved.Overlay.Color"] = Color.FromArgb(0x44, 0x22, 0xC5, 0x5E),
            ["Merge.Shadow.Color"] = Color.FromArgb(0xFF, 0x00, 0x00, 0x00),
        };

    [StaFact]
    public void ShadowOpacity_Token_IsPresentAndDarkThemeDefault()
    {
        // Merge.Shadow.Opacity is a plain double, not a Color — handled
        // separately from ExpectedColors. Pins the dark-theme default of
        // 0.15 so a V8 light-theme edit bumping it doesn't silently land.
        var dict = LoadMergeDictionary();
        dict["Merge.Shadow.Opacity"].Should().BeOfType<double>();
        ((double)dict["Merge.Shadow.Opacity"]!).Should().BeApproximately(0.15, 0.001);
    }

    // Every semantic brush token that merge controls reference — flat list kept
    // independent of the color palette so a missing brush surfaces clearly.
    private static readonly string[] ExpectedBrushKeys = new[]
    {
        "Merge.Ours.BgSubtle",  "Merge.Ours.BgStrong",  "Merge.Ours.Accent",  "Merge.Ours.Text",  "Merge.Ours.Border",
        "Merge.Theirs.BgSubtle","Merge.Theirs.BgStrong","Merge.Theirs.Accent","Merge.Theirs.Text","Merge.Theirs.Border",
        "Merge.Base.BgSubtle",  "Merge.Base.BgStrong",  "Merge.Base.Accent",  "Merge.Base.Text",  "Merge.Base.Border",
        "Merge.State.Unresolved","Merge.State.Resolved","Merge.State.AiPending",
        "Merge.State.Error",    "Merge.State.Warning",  "Merge.State.Manual",
        "Merge.Surface.1", "Merge.Surface.2", "Merge.Surface.3", "Merge.Surface.4", "Merge.Surface.5",
        "Merge.Border.Subtle", "Merge.Border.Strong", "Merge.Border.Focus",
        "Merge.Text.Primary", "Merge.Text.Secondary", "Merge.Text.Tertiary",
        "Merge.Text.Disabled", "Merge.Text.OnAccent",
    };

    // Sample of (brush -> color) pairings — confirming the indirection layer
    // works end to end without testing every single token (the palette contract
    // is the XAML file itself, not this list).
    private static readonly (string BrushKey, string ColorKey)[] BrushColorPairs = new[]
    {
        ("Merge.Ours.Accent", "Merge.Ours.Accent.Color"),
        ("Merge.Theirs.Accent", "Merge.Theirs.Accent.Color"),
        ("Merge.State.Resolved", "Merge.State.Resolved.Color"),
        ("Merge.Surface.2", "Merge.Surface.2.Color"),
        ("Merge.Text.Primary", "Merge.Text.Primary.Color"),
    };
}
