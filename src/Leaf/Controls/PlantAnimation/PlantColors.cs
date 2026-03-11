using System.Windows.Media;

namespace Leaf.Controls.PlantAnimation;

/// <summary>
/// Natural plant color generation using HSL color space.
/// Produces realistic gradients for stems, branches, and leaves.
/// </summary>
public static class PlantColors
{
    // --- Stem Colors ---

    public static Color StemLight => HslToColor(108, 0.42, 0.36);
    public static Color StemMid => HslToColor(115, 0.38, 0.26);
    public static Color StemDark => HslToColor(122, 0.34, 0.16);

    // --- Leaf Colors ---

    /// <summary>
    /// Generate a leaf color based on its age and position.
    /// </summary>
    /// <param name="age">0 = new growth (bright yellow-green), 1 = mature (deep green)</param>
    /// <param name="positionOnLeaf">0 = base, 1 = tip (tips are slightly lighter)</param>
    /// <param name="variation">Per-leaf random offset for uniqueness (-0.5 to 0.5)</param>
    public static Color LeafColor(double age, double positionOnLeaf, double variation)
    {
        // Hue: young=82 (yellow-green) -> mature=130 (deep green)
        double hue = 82 + age * 48 + variation * 12;

        // Saturation: vivid young growth, slightly muted mature
        double sat = 0.72 - age * 0.18 + variation * 0.05;

        // Lightness: bright young, darker mature, tips a touch lighter
        double lum = 0.52 - age * 0.14 + positionOnLeaf * 0.06 + variation * 0.04;

        return HslToColor(hue, Math.Max(0.1, Math.Min(1, sat)), Math.Max(0.1, Math.Min(0.9, lum)));
    }

    /// <summary>
    /// Create a gradient brush for a leaf, base to tip.
    /// </summary>
    public static LinearGradientBrush CreateLeafBrush(double age, double variation)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0.5),
            EndPoint = new System.Windows.Point(1, 0.5)
        };
        brush.GradientStops.Add(new GradientStop(LeafColor(age, 0.0, variation), 0.0));
        brush.GradientStops.Add(new GradientStop(LeafColor(age, 0.35, variation), 0.35));
        brush.GradientStops.Add(new GradientStop(LeafColor(age, 1.0, variation + 0.1), 1.0));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Create a gradient brush for the leaf's vein/midrib (slightly darker).
    /// </summary>
    public static SolidColorBrush CreateVeinBrush(double age, double variation)
    {
        var color = LeafColor(age, 0.3, variation - 0.15);
        var darker = Color.FromRgb(
            (byte)(color.R * 0.7),
            (byte)(color.G * 0.75),
            (byte)(color.B * 0.65));
        var brush = new SolidColorBrush(darker);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// HSL to WPF Color.
    /// h = hue in degrees (0-360), s = saturation (0-1), l = lightness (0-1)
    /// </summary>
    public static Color HslToColor(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = l - c / 2;

        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
