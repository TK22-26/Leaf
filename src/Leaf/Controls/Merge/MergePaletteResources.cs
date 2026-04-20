#nullable enable
using System.Collections;
using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Baml2006;
using System.Windows.Media;
using System.Xaml;

namespace Leaf.Controls.Merge;

/// <summary>
/// Resolves merge palette tokens from <c>Resources/Merge/*.xaml</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two sources are consulted, in order of preference:
/// <list type="number">
///   <item><see cref="Application.Current"/>.Resources — honours live theme
///   swap once the running app has merged <c>Merge.xaml</c> into its
///   Application resources (the V8 light-theme swap re-points this entry).</item>
///   <item>An assembly-local load that flattens every leaf palette BAML
///   directly from Leaf's <c>.g.resources</c> manifest, bypassing the
///   umbrella dictionaries (<c>Merge.xaml</c>, <c>MergePalette.xaml</c>)
///   whose <c>&lt;ResourceDictionary Source="…"/&gt;</c> references require
///   pack-URI resolution that doesn't work without a live Application.
///   Used when the host has not merged the palette yet — unit tests that
///   touch static members of merge controls, design-time previewers, and
///   early startup paths.</item>
/// </list>
/// A missing token is a programming error, not a recoverable condition —
/// <see cref="Resolve{T}"/> throws rather than silently substituting a
/// plausible value. That keeps the palette XAML the single source of truth
/// for every merge colour and typography token; no hex literals or synced
/// constants live outside it.
/// </para>
/// <para>
/// Merge controls freeze their brushes at static-init time for per-frame
/// render speed. V8 will revisit this when live light/dark swap lands:
/// frozen statics need invalidation on theme change so the palette actually
/// swaps.
/// </para>
/// </remarks>
internal static class MergePaletteResources
{
    // Assembly-local palette, loaded lazily on first access by reading the
    // compiled BAML streams directly out of the Leaf assembly's embedded
    // .g.resources container. This path intentionally avoids pack:// URIs
    // and Application.ResourceAssembly entirely — both are fragile across
    // xunit test ordering (ResourceAssembly is write-once, so whichever test
    // touches WPF resources first claims it forever). The live app still
    // prefers Application.Current.Resources via TryFindResource above; this
    // fallback only runs in environments where the palette hasn't been merged.
    private static readonly Lazy<ResourceDictionary> _localPalette = new(LoadPaletteFromEmbeddedBaml);

    // Umbrella dictionaries only declare MergedDictionaries with relative
    // <ResourceDictionary Source="..."/> references that need pack-URI
    // resolution to follow. That resolution can't work in this code path
    // (no Application.ResourceAssembly guarantee), so we skip the umbrellas
    // explicitly by name and rely on the leaf files' inline content. This
    // explicit-skip list makes an umbrella rename show up as a missing file
    // instead of silently falling out through a swallowed exception.
    private static readonly HashSet<string> UmbrellaResourceKeys = new(StringComparer.Ordinal)
    {
        "resources/merge/merge.baml",
        "resources/merge/mergepalette.baml",
    };

    private static ResourceDictionary LoadPaletteFromEmbeddedBaml()
    {
        // Enumerate every `resources/merge/*.baml` entry in Leaf's compiled
        // resource container, skipping known umbrella files. Adding a new
        // leaf palette file requires no code change; adding a new umbrella
        // requires adding its name to UmbrellaResourceKeys.
        var flattened = new ResourceDictionary();
        var assembly = typeof(MergePaletteResources).Assembly;
        var resourceSetName = assembly.GetName().Name + ".g.resources";
        using var resourceStream = assembly.GetManifestResourceStream(resourceSetName)
            ?? throw new InvalidOperationException(
                $"Embedded resource set '{resourceSetName}' was not found on the Leaf assembly.");
        using var reader = new ResourceReader(resourceStream);

        var loadedAtLeastOne = false;
        foreach (DictionaryEntry entry in reader)
        {
            var resourceKey = (string)entry.Key;
            if (!resourceKey.StartsWith("resources/merge/", StringComparison.Ordinal)) continue;
            if (!resourceKey.EndsWith(".baml", StringComparison.Ordinal)) continue;
            if (UmbrellaResourceKeys.Contains(resourceKey)) continue;
            if (entry.Value is not Stream bamlStream) continue;

            using (bamlStream)
            {
                var bamlReader = new Baml2006Reader(bamlStream);
                var writer = new XamlObjectWriter(bamlReader.SchemaContext);
                while (bamlReader.Read())
                {
                    writer.WriteNode(bamlReader);
                }
                if (writer.Result is ResourceDictionary loaded)
                {
                    foreach (var loadedKey in loaded.Keys)
                    {
                        flattened[loadedKey] = loaded[loadedKey];
                    }
                    loadedAtLeastOne = true;
                }
            }
        }

        if (!loadedAtLeastOne)
        {
            throw new InvalidOperationException(
                "No `resources/merge/*.baml` leaf files loaded from Leaf.g.resources. " +
                "Palette fallback path is broken — check that palette BAML files " +
                "are shipping inside the Leaf assembly's resource container.");
        }
        return flattened;
    }

    /// <summary>
    /// Look up any palette token of type <typeparamref name="T"/> — Color,
    /// SolidColorBrush, FontFamily, double, FontWeight, Style, or anything
    /// else the merge resource dictionaries publish. Prefers
    /// <see cref="Application.Current"/>.Resources (so V8 theme swap tracks)
    /// and falls back to the assembly-local palette load for tests /
    /// design-time where the host has not merged the palette yet.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The key was not found in either source, or the value at that key is
    /// not of type <typeparamref name="T"/>. Palette XAML is the single
    /// source of truth; a missing or mistyped key means the XAML has been
    /// edited inconsistently with this call site.
    /// </exception>
    public static T Resolve<T>(string key)
    {
        if (Application.Current is { } app && app.TryFindResource(key) is T appMatch)
        {
            return appMatch;
        }
        if (_localPalette.Value[key] is T localMatch)
        {
            return localMatch;
        }
        throw new InvalidOperationException(
            $"Merge palette token '{key}' (type {typeof(T).Name}) was not found. " +
            "Check src/Leaf/Resources/Merge/*.xaml — every key referenced from " +
            "code must exist there.");
    }

    /// <summary>
    /// Look up a <see cref="Color"/> token. Convenience wrapper around
    /// <see cref="Resolve{T}"/>.
    /// </summary>
    public static Color ResolveColor(string key) => Resolve<Color>(key);

    /// <summary>
    /// Look up a <see cref="Color"/> key and return a frozen
    /// <see cref="SolidColorBrush"/> wrapping it. Throws if the key is
    /// missing (see <see cref="Resolve{T}"/>).
    /// </summary>
    public static SolidColorBrush ResolveFrozenBrush(string colorKey)
    {
        var brush = new SolidColorBrush(ResolveColor(colorKey));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Copy <paramref name="c"/> with a new alpha channel. Used by merge
    /// controls that tint a palette colour beyond the ~5 standard alphas
    /// exposed as BgSubtle/BgStrong tokens (minimap swatches, connection
    /// curves, overlay greens). Kept here so the helper lives with the rest
    /// of the palette plumbing rather than being duplicated across controls.
    /// </summary>
    public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
}
