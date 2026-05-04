#nullable enable
using System.IO;
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

    // Absolute path to the user's custom palette override, or null when
    // no override is active. Set at Initialize time (from AppSettings)
    // and replaceable at runtime via SetCustomPalettePath so a future
    // settings UI can swap it without a restart.
    private static string? _customPalettePath;

    /// <summary>
    /// Read the OS theme once, apply the matching palette, and subscribe
    /// to preference-change events so future flips propagate. Idempotent
    /// — repeated calls after the first have no effect, so App.OnStartup
    /// is the natural single call site. <paramref name="customPalettePath"/>
    /// is the user's palette override (from <c>AppSettings</c>); when
    /// non-null and the file exists, it is merged last so its tokens
    /// override the shipped dark/light palette.
    /// </summary>
    public static void Initialize(string? customPalettePath = null)
    {
        if (_initialized) return;
        _initialized = true;

        _customPalettePath = NormalizeOverridePath(customPalettePath);
        _currentIsLight = DetectLightTheme();
        ApplyPalette(_currentIsLight);

        // Static handler — the service lives for the app's lifetime so no
        // explicit unsubscribe is needed. Windows raises this for any
        // user-preference change; we filter to General category
        // (colour / theme) before re-checking the registry.
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>
    /// Swap the custom palette override at runtime. Pass <c>null</c> or
    /// an empty string to clear the override. Re-applies the active
    /// theme so the change propagates through <see cref="PaletteChanged"/>
    /// to cached-brush controls. Safe to call from the UI thread only —
    /// mutates <see cref="Application.Resources"/>.
    /// </summary>
    public static void SetCustomPalettePath(string? path)
    {
        var normalized = NormalizeOverridePath(path);
        if (normalized == _customPalettePath) return;
        _customPalettePath = normalized;
        ApplyPalette(_currentIsLight);
    }

    /// <summary>
    /// Empty strings collapse to null so downstream code can use a single
    /// "no override" condition. Path validity (existence, XAML well-
    /// formedness) is checked lazily at apply time — letting the user
    /// set a path to a file they haven't written yet is harmless.
    /// </summary>
    private static string? NormalizeOverridePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path;

    /// <summary>
    /// Current resolved theme — useful for tests + manual debugging.
    /// Returns <c>true</c> when light, <c>false</c> when dark.
    /// </summary>
    public static bool CurrentIsLight => _currentIsLight;

    /// <summary>
    /// Raised after the merge palette dictionary has been swapped. Merge
    /// controls that cache palette-derived brushes in static fields
    /// subscribe here so they can re-resolve on a runtime theme flip —
    /// <c>{DynamicResource Merge.*}</c> bindings re-resolve automatically,
    /// but <c>private static readonly SolidColorBrush</c> caches (hot-
    /// path rendering in OnRender) don't see the swap without explicit
    /// invalidation.
    /// </summary>
    public static event EventHandler? PaletteChanged;

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

    // Key used on the custom-override ResourceDictionary's local resources
    // so we can recognise our own entries on re-apply. WPF doesn't let us
    // match by Source for file:// dictionaries loaded from XAML readers,
    // so we mark the dictionary with a sentinel key instead.
    internal const string CustomOverrideMarker = "Merge.Palette.CustomOverride";

    private static void ApplyPalette(bool isLight)
    {
        var app = Application.Current;
        if (app is null) return;
        var umbrella = FindMergeUmbrella(app.Resources);
        if (umbrella is null) return;

        if (ApplyPaletteToUmbrella(umbrella, isLight, _customPalettePath))
        {
            PaletteChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Pure "given an umbrella, install the desired base palette + custom
    /// override" operation. Factored out of <see cref="ApplyPalette"/> so
    /// tests can exercise the core swap logic against a synthetic
    /// umbrella — no mutation of <see cref="Application.Current"/>, no
    /// race against other test classes. Returns <c>true</c> when either
    /// the base palette or the override list changed — the signal the
    /// caller uses to fire <see cref="PaletteChanged"/>.
    /// </summary>
    internal static bool ApplyPaletteToUmbrella(ResourceDictionary umbrella, bool isLight, string? customPalettePath)
    {
        ArgumentNullException.ThrowIfNull(umbrella);

        // Step 1: swap the base palette (Dark ↔ Light) in place. This
        // matches V8's original behaviour — atomic from the WPF binding
        // perspective so every {DynamicResource Merge.*} re-resolves on
        // the next lookup through the umbrella. Matches by filename
        // suffix so a relative-loaded child (XAML-declared Source)
        // compares equal to the absolute pack URI we want to install.
        var desired = isLight ? LightPaletteUri : DarkPaletteUri;
        var desiredSuffix = isLight ? "MergePaletteLight.xaml" : "MergePaletteDark.xaml";
        var otherSuffix = isLight ? "MergePaletteDark.xaml" : "MergePaletteLight.xaml";
        var baseSwapped = false;

        for (int i = 0; i < umbrella.MergedDictionaries.Count; i++)
        {
            var md = umbrella.MergedDictionaries[i];
            var src = md.Source?.OriginalString;
            if (src is null) continue;
            if (src.EndsWith(desiredSuffix, StringComparison.OrdinalIgnoreCase))
            {
                baseSwapped = true;
                break;
            }
            if (src.EndsWith(otherSuffix, StringComparison.OrdinalIgnoreCase))
            {
                umbrella.MergedDictionaries[i] = new ResourceDictionary { Source = desired };
                baseSwapped = true;
                break;
            }
        }

        // Step 2: remove the previous custom-override dictionary, if any.
        // Re-applying always starts from a clean base so repeat calls
        // with different override paths don't stack.
        RemoveExistingOverride(umbrella);

        // Step 3: append the current user override, if any, so its tokens
        // win at DynamicResource lookup time (later entries override
        // earlier ones in MergedDictionaries).
        var overrideDict = TryLoadOverride(customPalettePath);
        if (overrideDict is not null)
        {
            umbrella.MergedDictionaries.Add(overrideDict);
        }

        return baseSwapped || overrideDict is not null;
    }

    private static void RemoveExistingOverride(ResourceDictionary umbrella)
    {
        // Own-entries-only marker check. ResourceDictionary.Contains walks
        // MergedDictionaries, which would falsely claim the palette dict
        // "contains" the marker if somebody later moved the marker into
        // a nested dict. Scanning Keys keeps the match strict.
        for (int i = umbrella.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            var md = umbrella.MergedDictionaries[i];
            var ownEntryMarker = false;
            foreach (var k in md.Keys)
            {
                if (Equals(k, CustomOverrideMarker)) { ownEntryMarker = true; break; }
            }
            if (ownEntryMarker)
            {
                umbrella.MergedDictionaries.RemoveAt(i);
            }
        }
    }

    private static ResourceDictionary? TryLoadOverride(string? path)
    {
        if (path is null) return null;
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            if (System.Windows.Markup.XamlReader.Load(stream) is not ResourceDictionary dict)
                return null;
            // Tag the loaded dictionary so we can find + remove it later.
            // Any string value works; we just need Contains(marker) to
            // return true.
            dict[CustomOverrideMarker] = true;
            return dict;
        }
        catch (Exception ex)
        {
            // Malformed XAML or IO error — log once, then leave the
            // override unapplied. Failing loudly mid-app with an unhandled
            // exception would crash on every palette swap; falling back
            // to the shipped palette lets the user fix their file.
            System.Diagnostics.Trace.WriteLine(
                $"[MergeThemeSwitcher] custom palette '{path}' failed to load: {ex.Message}");
            return null;
        }
    }

    private static ResourceDictionary? FindMergeUmbrella(ResourceDictionary root)
    {
        // Walk the merged-dictionary tree and return the palette-swap
        // umbrella (MergePalette.xaml). Matches by URI path suffix rather
        // than full URI equality — the inner dictionary may be stored with
        // a relative URI (declared in XAML as Source="MergePalette.xaml")
        // or an absolute pack URI depending on how the parent was loaded.
        // Full equality fails the former case; suffix match covers both.
        foreach (var md in root.MergedDictionaries)
        {
            if (IsPaletteSwapUmbrella(md)) return md;
            var nested = FindMergeUmbrella(md);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static bool IsPaletteSwapUmbrella(ResourceDictionary md)
    {
        if (md.Source is null) return false;
        var s = md.Source.OriginalString;
        // Match ".../MergePalette.xaml" — either absolute pack URI or
        // relative. Keeps the swap contract semantic ("the file named
        // MergePalette.xaml anywhere in the tree") rather than tied to
        // a specific URI shape.
        return s.EndsWith("MergePalette.xaml", StringComparison.OrdinalIgnoreCase)
            && !s.EndsWith("MergePaletteDark.xaml", StringComparison.OrdinalIgnoreCase)
            && !s.EndsWith("MergePaletteLight.xaml", StringComparison.OrdinalIgnoreCase);
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
