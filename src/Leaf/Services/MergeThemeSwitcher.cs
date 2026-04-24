#nullable enable
using System.Windows;
using Microsoft.Win32;

namespace Leaf.Services;

/// <summary>
/// Swaps the merge-editor palette dictionary (Dark ↔ Light) to match the
/// current OS theme (V8). The app-level <c>Application.ThemeMode="System"</c>
/// makes the Fluent WinUI controls repaint on theme change, but the merge
/// editor's palette lives in its own <c>Resources/Merge/Merge.xaml</c>
/// umbrella that doesn't participate in the Fluent swap. This service
/// keeps that umbrella in lockstep with the OS theme.
/// </summary>
/// <remarks>
/// <para>
/// The umbrella <c>Merge.xaml</c> starts with <c>MergePaletteDark.xaml</c>
/// as its first merged dictionary. On <see cref="Initialize"/> we read
/// the current OS theme; if it's light, we replace the dark dictionary
/// in the Merge umbrella with the light one. Subsequent theme changes
/// (user flipping Windows Settings) arrive via
/// <see cref="SystemEvents.UserPreferenceChanged"/> and re-run the swap.
/// </para>
/// <para>
/// Registry-based theme detection is used rather than WPF's
/// <c>Application.ActualThemeMode</c> because the latter requires the
/// ThemeMode to be explicitly set to System; reading the Personalize
/// key directly works across any WPF version and matches what the Fluent
/// renderer itself reads.
/// </para>
/// </remarks>
public static class MergeThemeSwitcher
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightValueName = "AppsUseLightTheme";

    private static readonly Uri DarkPaletteUri = new(
        "pack://application:,,,/Leaf;component/Resources/Merge/MergePaletteDark.xaml",
        UriKind.Absolute);
    private static readonly Uri LightPaletteUri = new(
        "pack://application:,,,/Leaf;component/Resources/Merge/MergePaletteLight.xaml",
        UriKind.Absolute);
    private static readonly Uri UmbrellaUri = new(
        "pack://application:,,,/Leaf;component/Resources/Merge/MergePalette.xaml",
        UriKind.Absolute);

    private static bool _initialized;
    private static bool _currentIsLight;

    /// <summary>
    /// Read the OS theme once, apply the matching palette, and subscribe
    /// to preference-change events so future flips propagate. Idempotent
    /// — repeated calls after the first have no effect, so App.OnStartup
    /// is the natural single call site.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _currentIsLight = DetectLightTheme();
        ApplyPalette(_currentIsLight);

        // Static handler — the service lives for the app's lifetime so no
        // explicit unsubscribe is needed. Windows raises this for any
        // user-preference change; we filter to General category
        // (colour / theme) before re-checking the registry.
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>
    /// Current resolved theme — useful for tests + manual debugging.
    /// Returns <c>true</c> when light, <c>false</c> when dark.
    /// </summary>
    public static bool CurrentIsLight => _currentIsLight;

    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        var isLight = DetectLightTheme();
        if (isLight == _currentIsLight) return;
        _currentIsLight = isLight;
        // SystemEvents fire on a dedicated Windows message thread — hop
        // to the UI thread so we mutate Application.Resources on its
        // owning dispatcher. Using BeginInvoke (async, ordered) matches
        // the MergeMotionHelpers fire-and-forget pattern.
        var app = Application.Current;
        if (app is null) return;
        app.Dispatcher.BeginInvoke(() => ApplyPalette(isLight));
    }

    private static void ApplyPalette(bool isLight)
    {
        var app = Application.Current;
        if (app is null) return;
        var umbrella = FindMergeUmbrella(app.Resources);
        if (umbrella is null) return;

        // Find the existing palette entry by Source match. The umbrella's
        // first merged dictionary is the palette (dark on disk); we swap
        // it in place so the swap is atomic from the WPF binding
        // perspective — every {DynamicResource Merge.*} re-resolves
        // through the umbrella on the next lookup and sees the new colour.
        var desired = isLight ? LightPaletteUri : DarkPaletteUri;
        var other = isLight ? DarkPaletteUri : LightPaletteUri;

        for (int i = 0; i < umbrella.MergedDictionaries.Count; i++)
        {
            var md = umbrella.MergedDictionaries[i];
            if (md.Source == desired) return; // already correct
            if (md.Source == other)
            {
                umbrella.MergedDictionaries[i] = new ResourceDictionary { Source = desired };
                return;
            }
        }
    }

    private static ResourceDictionary? FindMergeUmbrella(ResourceDictionary root)
    {
        // Walk the merged-dictionary tree and return the Merge.xaml
        // umbrella. Works regardless of where in App.xaml the merge
        // entry sits, so reordering App.xaml's merged dictionaries
        // doesn't break the swap.
        foreach (var md in root.MergedDictionaries)
        {
            if (md.Source == UmbrellaUri) return md;
            var nested = FindMergeUmbrella(md);
            if (nested is not null) return nested;
        }
        return null;
    }

    /// <summary>
    /// Read <c>HKCU\...\Personalize\AppsUseLightTheme</c> (0 = dark,
    /// non-zero = light). Returns <c>false</c> (dark) on failure — the
    /// dark palette is the pre-V8 default, so an unreadable registry
    /// leaves behaviour unchanged rather than swapping unexpectedly.
    /// Exposed <c>internal</c> so tests can stub the value.
    /// </summary>
    internal static bool DetectLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            if (key is null) return false;
            var raw = key.GetValue(AppsUseLightValueName);
            return raw is int i && i != 0;
        }
        catch
        {
            return false;
        }
    }
}
