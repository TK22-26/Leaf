using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Leaf.Models;

/// <summary>
/// Represents a Git branch with observable selection state.
/// </summary>
public partial class BranchInfo : ObservableObject
{
    /// <summary>
    /// True if this branch is selected in the tree view.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isHidden;

    [ObservableProperty]
    private bool _isSolo;

    /// <summary>
    /// Full name of the branch (e.g., "refs/heads/main").
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Friendly name of the branch (e.g., "main").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// GitFlow branch type classification (set when loading branches if GitFlow is enabled).
    /// </summary>
    public GitFlowBranchType GitFlowType { get; set; } = GitFlowBranchType.None;

    /// <summary>
    /// Gets the GitFlow color for this branch.
    /// Returns a unique color for features based on name hash, standard colors for other types.
    /// </summary>
    public Brush GitFlowColor => GetGitFlowColor();

    /// <summary>
    /// True if this is the currently checked out branch.
    /// </summary>
    public bool IsCurrent { get; set; }

    /// <summary>
    /// True if this is a remote tracking branch.
    /// </summary>
    public bool IsRemote { get; set; }

    /// <summary>
    /// Name of the remote (e.g., "origin") if this is a remote branch.
    /// </summary>
    public string? RemoteName { get; set; }

    /// <summary>
    /// Name of the upstream tracking branch (if any).
    /// </summary>
    public string? TrackingBranchName { get; set; }

    /// <summary>
    /// SHA of the commit this branch points to.
    /// </summary>
    public string TipSha { get; set; } = string.Empty;

    /// <summary>
    /// Number of commits ahead of tracking branch.
    /// </summary>
    public int AheadBy { get; set; }

    /// <summary>
    /// Number of commits behind tracking branch.
    /// </summary>
    public int BehindBy { get; set; }

    /// <summary>
    /// True if this is the main/master/develop branch (highway branch).
    /// </summary>
    public bool IsMainBranch => Name is "main" or "master" or "develop";

    /// <summary>
    /// Expand state for tree views.
    /// </summary>
    public bool IsExpanded { get; set; }

    #region GitFlow Colors

    // Standard GitFlow colors (industry standard from nvie.com and GitKraken)
    private static readonly SolidColorBrush MainColor = new(Color.FromRgb(0x6E, 0x40, 0xC9));     // Purple - royal color for main
    private static readonly SolidColorBrush DevelopColor = new(Color.FromRgb(0x1F, 0x88, 0x3D)); // Green
    private static readonly SolidColorBrush ReleaseColor = new(Color.FromRgb(0xBF, 0x87, 0x00)); // Yellow/Amber
    private static readonly SolidColorBrush HotfixColor = new(Color.FromRgb(0xCF, 0x22, 0x2E));  // Red
    private static readonly SolidColorBrush SupportColor = new(Color.FromRgb(0xE6, 0x6C, 0x00)); // Orange

    /// <summary>
    /// Gets the GitFlow color brush for this branch.
    /// Features get unique colors based on a hash of the branch name.
    /// </summary>
    private Brush GetGitFlowColor()
    {
        return GitFlowType switch
        {
            GitFlowBranchType.Main => MainColor,
            GitFlowBranchType.Develop => DevelopColor,
            GitFlowBranchType.Release => ReleaseColor,
            GitFlowBranchType.Hotfix => HotfixColor,
            GitFlowBranchType.Support => SupportColor,
            GitFlowBranchType.Feature => GenerateFeatureColor(Name),
            _ => Brushes.Transparent
        };
    }

    /// <summary>
    /// Generates a unique color for a feature branch based on the branch name.
    /// Colors are generated in the blue/purple spectrum for visual consistency.
    /// </summary>
    private static SolidColorBrush GenerateFeatureColor(string branchName)
    {
        // Extract just the feature name (after prefix)
        var featureName = branchName;
        var slashIndex = branchName.IndexOf('/');
        if (slashIndex >= 0 && slashIndex < branchName.Length - 1)
        {
            featureName = branchName[(slashIndex + 1)..];
        }

        // Generate a hash from the feature name
        var hash = GetStableHash(featureName);

        // Generate colors in the blue/purple spectrum (hue 200-280)
        // This keeps all features visually related while being distinct
        var hue = 200 + (hash % 80); // Hue range: 200-280 (blue to purple)
        var saturation = 0.65 + (((hash >> 8) % 20) / 100.0); // 65-85%
        var lightness = 0.45 + (((hash >> 16) % 15) / 100.0); // 45-60%

        var (r, g, b) = HslToRgb(hue / 360.0, saturation, lightness);
        return new SolidColorBrush(Color.FromRgb(r, g, b));
    }

    /// <summary>
    /// Generates a stable hash code for a string (consistent across sessions).
    /// </summary>
    private static int GetStableHash(string text)
    {
        unchecked
        {
            int hash = 17;
            foreach (var c in text)
            {
                hash = hash * 31 + c;
            }
            return Math.Abs(hash);
        }
    }

    /// <summary>
    /// Converts HSL color values to RGB.
    /// </summary>
    private static (byte r, byte g, byte b) HslToRgb(double h, double s, double l)
    {
        double r, g, b;

        if (Math.Abs(s) < 0.001)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3);
        }

        return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    #endregion
}
