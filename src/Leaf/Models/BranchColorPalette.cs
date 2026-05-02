using System.Globalization;
using System.Windows.Media;

namespace Leaf.Models;

/// <summary>
/// A named, ordered list of colours used to assign colours to branches in
/// the graph. Branches without an explicit user override are mapped to a
/// palette slot via a stable hash of the (normalised) branch name.
///
/// <para>Palettes are JSON-friendly POCOs so they round-trip cleanly
/// through <c>AppSettings.CustomBranchColorPalettes</c>. Built-in palettes
/// (default, Okabe-Ito colour-blind safe, pastel, high-contrast) are
/// constructed at runtime in <see cref="Leaf.Services.BranchColorPaletteRegistry"/>
/// rather than persisted, so updates to the shipped palettes apply
/// automatically on next launch.</para>
/// </summary>
public sealed class BranchColorPalette
{
    /// <summary>
    /// Stable identifier used in <see cref="Services.AppSettings.DefaultBranchColorPaletteId"/>
    /// and <see cref="RepositoryInfo.BranchColorOverrides"/>. For built-in
    /// palettes this is one of the constants on
    /// <see cref="Leaf.Services.BranchColorPaletteRegistry"/>; for custom
    /// palettes it's a GUID string created at the time the palette was added.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// User-facing name shown in the settings selector and the right-click
    /// colour picker. For built-in palettes this is set when the registry
    /// hands out the palette; for custom palettes the user supplies it.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Hex colour strings (e.g. <c>#3478F6</c> or <c>#FF3478F6</c>) that
    /// make up the palette. Order matters — the stable-hash algorithm
    /// indexes into this list, so the same branch name maps to the same
    /// slot every time as long as the list itself is unchanged.
    /// </summary>
    public List<string> Colors { get; set; } = [];

    /// <summary>
    /// True for the four shipped palettes (default, Okabe-Ito, pastel,
    /// high-contrast). Used by the settings UI to disable Edit/Delete on
    /// rows that aren't user-owned, and by the registry to skip persisting
    /// them.
    /// </summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// Parse <see cref="Colors"/> into <see cref="Color"/> values. Invalid
    /// or empty entries are skipped — the result is guaranteed non-empty
    /// because the registry refuses to publish a palette with zero usable
    /// colours, and <see cref="Leaf.Services.BranchColorService"/> falls
    /// back to the registry's default palette if a palette ever ends up
    /// empty after filtering (e.g. a user-edited palette saved with all
    /// invalid hexes).
    /// </summary>
    public List<Color> ParsedColors()
    {
        var result = new List<Color>(Colors.Count);
        foreach (var hex in Colors)
        {
            if (TryParseColor(hex, out var color))
                result.Add(color);
        }
        return result;
    }

    /// <summary>
    /// Parse a hex colour string in <c>#RRGGBB</c> or <c>#AARRGGBB</c>
    /// form. Returns false on any parse failure — callers must decide
    /// whether that is fatal (e.g. picker user input) or skippable
    /// (e.g. malformed entry in a saved custom palette).
    /// </summary>
    public static bool TryParseColor(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;

        var trimmed = hex.Trim();
        if (trimmed[0] == '#') trimmed = trimmed[1..];

        if (trimmed.Length is not 6 and not 8) return false;

        var hasAlpha = trimmed.Length == 8;
        try
        {
            byte a = hasAlpha
                ? byte.Parse(trimmed[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : (byte)0xFF;
            byte r = byte.Parse(trimmed.Substring(hasAlpha ? 2 : 0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte g = byte.Parse(trimmed.Substring(hasAlpha ? 4 : 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte b = byte.Parse(trimmed.Substring(hasAlpha ? 6 : 4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            color = Color.FromArgb(a, r, g, b);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// Format a <see cref="Color"/> as a 6- or 8-character uppercase hex
    /// string with a leading <c>#</c>. Alpha is dropped when fully opaque
    /// so saved palettes stay readable.
    /// </summary>
    public static string FormatColor(Color color)
    {
        return color.A == 0xFF
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
