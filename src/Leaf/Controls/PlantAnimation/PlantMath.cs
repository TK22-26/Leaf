using System.Windows;

namespace Leaf.Controls.PlantAnimation;

/// <summary>
/// Mathematical utilities for plant geometry — Bezier curves, easing, and growth functions.
/// </summary>
public static class PlantMath
{
    /// <summary>
    /// Gompertz growth curve — asymmetric sigmoid that models real plant growth.
    /// Fast early acceleration, slow tapering.
    /// </summary>
    public static double Gompertz(double t, double a = 1.0, double b = 3.0, double c = 7.0)
    {
        return a * Math.Exp(-b * Math.Exp(-c * t));
    }

    /// <summary>
    /// Smooth sigmoid for general eased transitions.
    /// </summary>
    public static double Sigmoid(double t, double steepness = 10.0, double midpoint = 0.5)
    {
        return 1.0 / (1.0 + Math.Exp(-steepness * (t - midpoint)));
    }

    public static double EaseOutCubic(double t) => 1.0 - Math.Pow(1.0 - Clamp01(t), 3);
    public static double EaseOutQuart(double t) => 1.0 - Math.Pow(1.0 - Clamp01(t), 4);
    public static double EaseOutQuad(double t) { double c = Clamp01(t); return 1.0 - (1.0 - c) * (1.0 - c); }
    public static double EaseInOutCubic(double t)
    {
        double c = Clamp01(t);
        return c < 0.5 ? 4 * c * c * c : 1 - Math.Pow(-2 * c + 2, 3) / 2;
    }
    public static double EaseOutBack(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        double c = Clamp01(t);
        return 1 + c3 * Math.Pow(c - 1, 3) + c1 * Math.Pow(c - 1, 2);
    }

    public static double Clamp01(double v) => Math.Max(0, Math.Min(1, v));
    public static double Lerp(double a, double b, double t) => a + (b - a) * Clamp01(t);

    /// <summary>
    /// Evaluate a cubic Bezier curve at parameter t.
    /// </summary>
    public static Point Bezier(double t, Point p0, Point p1, Point p2, Point p3)
    {
        double u = 1 - t;
        double uu = u * u;
        double uuu = uu * u;
        double tt = t * t;
        double ttt = tt * t;

        return new Point(
            uuu * p0.X + 3 * uu * t * p1.X + 3 * u * tt * p2.X + ttt * p3.X,
            uuu * p0.Y + 3 * uu * t * p1.Y + 3 * u * tt * p2.Y + ttt * p3.Y);
    }

    /// <summary>
    /// First derivative (tangent) of a cubic Bezier at parameter t.
    /// </summary>
    public static Vector BezierTangent(double t, Point p0, Point p1, Point p2, Point p3)
    {
        double u = 1 - t;
        return new Vector(
            3 * u * u * (p1.X - p0.X) + 6 * u * t * (p2.X - p1.X) + 3 * t * t * (p3.X - p2.X),
            3 * u * u * (p1.Y - p0.Y) + 6 * u * t * (p2.Y - p1.Y) + 3 * t * t * (p3.Y - p2.Y));
    }

    /// <summary>
    /// Normal (perpendicular to tangent) at a point on a Bezier curve.
    /// </summary>
    public static Vector BezierNormal(double t, Point p0, Point p1, Point p2, Point p3)
    {
        var tangent = BezierTangent(t, p0, p1, p2, p3);
        var normal = new Vector(-tangent.Y, tangent.X);
        normal.Normalize();
        return normal;
    }

    /// <summary>
    /// Stem thickness tapering — thicker at base, thin at tip, with concave profile.
    /// </summary>
    public static double StemThickness(double t, double baseThickness, double tipThickness, double taper = 1.6)
    {
        double factor = Math.Pow(1.0 - Clamp01(t), taper);
        return tipThickness + (baseThickness - tipThickness) * factor;
    }
}
