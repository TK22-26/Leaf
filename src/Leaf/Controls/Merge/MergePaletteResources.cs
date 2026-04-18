#nullable enable
using System.Windows;
using System.Windows.Media;

namespace Leaf.Controls.Merge;

/// <summary>
/// Resolves palette tokens from <c>Resources/Merge/MergePalette.xaml</c> with
/// in-code fallbacks for environments where <c>Application.Current</c> is not
/// available (unit tests construct controls without <c>Application.Run</c>).
/// </summary>
/// <remarks>
/// <para>
/// Merge controls freeze their brushes at static-init time for per-frame render
/// speed — matching the pattern that already existed before this refactor. Token
/// lookup happens once, then the frozen brush is cached statically. V8 will
/// revisit this when light/dark theme swap lands: frozen statics need to be
/// invalidated on theme change so the palette actually swaps.
/// </para>
/// <para>
/// Keys follow the convention <c>Merge.&lt;group&gt;.&lt;role&gt;.Color</c> for
/// <see cref="Color"/> resources — the <see cref="SolidColorBrush"/> tokens
/// without the <c>.Color</c> suffix are for XAML consumers.
/// </para>
/// </remarks>
internal static class MergePaletteResources
{
    public static Color ResolveColor(string key, Color fallback)
    {
        if (Application.Current is { } app && app.TryFindResource(key) is Color c)
        {
            return c;
        }
        return fallback;
    }

    public static SolidColorBrush ResolveFrozenBrush(string colorKey, Color fallbackColor)
    {
        var brush = new SolidColorBrush(ResolveColor(colorKey, fallbackColor));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Copies <paramref name="c"/> with a new alpha channel. Used by controls
    /// that tint a palette colour beyond the ~5 standard alphas exposed as
    /// BgSubtle/BgStrong tokens (minimap swatches, connection curves, overlay
    /// greens). Kept here so the ubiquitous helper lives with the rest of the
    /// palette plumbing rather than being duplicated across every control.
    /// </summary>
    public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
}
