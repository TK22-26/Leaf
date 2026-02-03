using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Leaf.Graph;
using Leaf.Models;

namespace Leaf.Controls.GitGraph;

public partial class GitGraphCanvas
{
    // Popup for showing branch names tooltip
    private System.Windows.Controls.Primitives.Popup? _branchTooltipPopup;
    private StackPanel? _branchTooltipPanel;

    private void ShowBranchTooltip(List<BranchLabel> branches, Rect tagRect)
    {
        if (_branchTooltipPopup == null)
        {
            _branchTooltipPanel = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6),
                Child = _branchTooltipPanel,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 12,
                    ShadowDepth = 4,
                    Opacity = 0.4
                }
            };

            _branchTooltipPopup = new System.Windows.Controls.Primitives.Popup
            {
                Child = border,
                AllowsTransparency = true,
                PlacementTarget = this,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Relative,
                StaysOpen = true
            };
        }

        // Clear and rebuild branch items
        _branchTooltipPanel!.Children.Clear();

        // Measure to align icons to the right edge of the tooltip
        var tooltipDpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        const double circleSize = 10;
        const double circleRightMargin = 8;
        const double nameRightMargin = 8;
        double maxNameWidth = 0;
        double maxIconWidth = 0;

        foreach (var branch in branches)
        {
            var nameFormatted = new FormattedText(
                branch.Name,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                12,
                Brushes.White,
                tooltipDpi);
            nameFormatted.SetFontWeight(branch.IsCurrent ? FontWeights.SemiBold : FontWeights.Normal);
            maxNameWidth = Math.Max(maxNameWidth, nameFormatted.Width);

            var iconTextMeasure = "";
            if (branch.IsLocal) iconTextMeasure += ComputerIcon;
            if (branch.IsLocal && branch.IsRemote) iconTextMeasure += " ";
            if (branch.IsRemote) iconTextMeasure += CloudIcon;

            if (!string.IsNullOrEmpty(iconTextMeasure))
            {
                var iconFormatted = new FormattedText(
                    iconTextMeasure,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    IconTypeface,
                    11,
                    Brushes.White,
                    tooltipDpi);
                maxIconWidth = Math.Max(maxIconWidth, iconFormatted.Width + nameRightMargin);
            }
        }

        double rowWidth = circleSize + circleRightMargin + maxNameWidth + maxIconWidth;

        foreach (var branch in branches)
        {
            var branchBrush = GraphBuilder.GetBranchColor(branch.Name);

            // Create a row: colored circle + name (left) + icons (right)
            var row = new Grid
            {
                Margin = new Thickness(4, 3, 4, 3),
                Width = rowWidth
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Colored circle
            var circle = new System.Windows.Shapes.Ellipse
            {
                Width = circleSize,
                Height = circleSize,
                Fill = branchBrush,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(circle, 0);
            row.Children.Add(circle);

            // Branch name
            var nameText = new TextBlock
            {
                Text = branch.Name,
                Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                FontSize = 12,
                FontFamily = new FontFamily("Segoe UI"),
                FontWeight = branch.IsCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0, 0, nameRightMargin, 0)
            };
            Grid.SetColumn(nameText, 1);
            row.Children.Add(nameText);

            // Icons (local/remote)
            var iconText = "";
            if (branch.IsLocal) iconText += ComputerIcon;
            if (branch.IsLocal && branch.IsRemote) iconText += " ";
            if (branch.IsRemote) iconText += CloudIcon;

            if (!string.IsNullOrEmpty(iconText))
            {
                var icons = new TextBlock
                {
                    Text = iconText,
                    Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                    FontSize = 11,
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(icons, 2);
                row.Children.Add(icons);
            }

            _branchTooltipPanel.Children.Add(row);
        }

        _branchTooltipPopup.HorizontalOffset = tagRect.Right + 10;
        _branchTooltipPopup.VerticalOffset = tagRect.Top - 4;
        _branchTooltipPopup.IsOpen = true;
    }

    private void HideBranchTooltip()
    {
        if (_branchTooltipPopup != null)
        {
            _branchTooltipPopup.IsOpen = false;
        }
    }
}
