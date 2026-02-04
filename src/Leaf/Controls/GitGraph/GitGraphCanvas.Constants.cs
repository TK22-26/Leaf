using System.Windows;
using System.Windows.Media;

namespace Leaf.Controls.GitGraph;

public partial class GitGraphCanvas
{
    #region Rendering Constants

    private static readonly Brush LabelTextBrush = Brushes.White;
    private static readonly Pen LabelBorderPen = new Pen(Brushes.Transparent, 0);

    // Icons from Segoe Fluent Icons
    private const string ComputerIcon = "\uE7F4"; // Computer/Desktop
    private const string CloudIcon = "\uE753"; // Cloud
    private const string StashIcon = "\uE7B8"; // Box/Package icon for stashes

    // Custom logo geometries for GitHub and Azure DevOps
    private static readonly Geometry GitHubLogoGeometry = Geometry.Parse("M48.854 0C21.839 0 0 22 0 49.217c0 21.756 13.993 40.172 33.405 46.69 2.427.49 3.316-1.059 3.316-2.362 0-1.141-.08-5.052-.08-9.127-13.59 2.934-16.42-5.867-16.42-5.867-2.184-5.704-5.42-7.17-5.42-7.17-4.448-3.015.324-3.015.324-3.015 4.934.326 7.523 5.052 7.523 5.052 4.367 7.496 11.404 5.378 14.235 4.074.404-3.178 1.699-5.378 3.074-6.6-10.839-1.141-22.243-5.378-22.243-24.283 0-5.378 1.94-9.778 5.014-13.2-.485-1.222-2.184-6.275.486-13.038 0 0 4.125-1.304 13.426 5.052a46.97 46.97 0 0 1 12.214-1.63c4.125 0 8.33.571 12.213 1.63 9.302-6.356 13.427-5.052 13.427-5.052 2.67 6.763.97 11.816.485 13.038 3.155 3.422 5.015 7.822 5.015 13.2 0 18.905-11.404 23.06-22.324 24.283 1.78 1.548 3.316 4.481 3.316 9.126 0 6.6-.08 11.897-.08 13.526 0 1.304.89 2.853 3.316 2.364 19.412-6.52 33.405-24.935 33.405-46.691C97.707 22 75.788 0 48.854 0z");
    private static readonly Geometry AzureDevOpsLogoGeometry = Geometry.Parse("M17,4v9.74l-4,3.28-6.2-2.26V17L3.29,12.41l10.23.8V4.44Zm-3.41.49L7.85,1V3.29L2.58,4.84,1,6.87v4.61l2.26,1V6.57Z");

    // Logo bounds for scaling (viewBox sizes)
    private const double GitHubLogoWidth = 98;
    private const double GitHubLogoHeight = 96;
    private const double AzureDevOpsLogoWidth = 18;
    private const double AzureDevOpsLogoHeight = 18;

    // Stash color (purple/violet for distinctiveness)
    private static readonly Color StashColor = Color.FromRgb(136, 82, 179); // Purple

    private static readonly Typeface LabelTypeface = new Typeface(
        new FontFamily("Segoe UI"),
        FontStyles.Normal,
        FontWeights.SemiBold,
        FontStretches.Normal);

    private static readonly Typeface IconTypeface = new Typeface(
        new FontFamily("Segoe Fluent Icons"),
        FontStyles.Normal,
        FontWeights.Normal,
        FontStretches.Normal);

    private const double GhostTagOpacity = 0.4;

    // Animation constants
    private const double AnimationDuration = 100; // milliseconds - snappy expand/collapse
    private const double AnimationStep = 16.67; // ~60fps - standard refresh rate

    #endregion
}
