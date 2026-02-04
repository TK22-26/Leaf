using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Leaf.Graph;
using Leaf.Models;

namespace Leaf.Controls.GitGraph;

public partial class GitGraphCanvas
{
    /// <summary>
    /// Ease-out function for smoother animation (fast start, slow end).
    /// </summary>
    private static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);

    private void DrawExpandedTagLabels(DrawingContext dc, GitTreeNode node, int rowOffset)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        int displayRow = node.RowIndex + rowOffset;
        double y = GetYForRow(node.RowIndex + rowOffset);
        double nodeX = GetXForColumn(node.ColumnIndex);

        var overflow = _hitTestService.GetTagOverflowByRow(displayRow);
        if (overflow == null)
            return;

        var tags = node.TagNames;
        if (tags.Count == 0)
            return;

        double labelX = overflow.Value.StartX;
        double fontSize = 11;
        double itemHeight = 18;
        double hPadding = 6;

        var ghostTextBrush = new SolidColorBrush(Color.FromArgb(
            (byte)(255 * GhostTagOpacity), 255, 255, 255));
        ghostTextBrush.Freeze();

        var firstNameFormatted = new FormattedText(
            tags[0],
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            fontSize,
            ghostTextBrush,
            dpi);

        int overflowCount = tags.Count - 1;
        var suffixFormatted = new FormattedText(
            $" +{overflowCount}",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            fontSize,
            ghostTextBrush,
            dpi);

        double tagWidth = hPadding + firstNameFormatted.Width + suffixFormatted.Width + hPadding;
        double tagHeight = itemHeight + ((tags.Count - 1) * itemHeight);
        double tagTop = y - itemHeight / 2;

        var firstBrush = GraphBuilder.GetBranchColor(tags[0]) as SolidColorBrush ?? Brushes.Gray;
        var firstColor = firstBrush.Color;
        var tagBgBrush = new SolidColorBrush(Color.FromArgb(
            (byte)(firstColor.A * GhostTagOpacity),
            firstColor.R, firstColor.G, firstColor.B));
        tagBgBrush.Freeze();

        var tagRect = new Rect(labelX, tagTop, tagWidth, tagHeight);
        dc.DrawRoundedRectangle(tagBgBrush, LabelBorderPen, tagRect, 4, 4);

        var hitAreas = new List<Rect>();

        double currentY = y;
        for (int i = 0; i < tags.Count; i++)
        {
            var tagName = tags[i];
            var nameFormatted = new FormattedText(
                tagName,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                fontSize,
                ghostTextBrush,
                dpi);

            if (i > 0)
            {
                var tagBrush = GraphBuilder.GetBranchColor(tagName) as SolidColorBrush ?? Brushes.Gray;
                var tagColor = tagBrush.Color;
                var rowBrush = new SolidColorBrush(Color.FromArgb(
                    (byte)(tagColor.A * GhostTagOpacity),
                    tagColor.R, tagColor.G, tagColor.B));
                rowBrush.Freeze();
                var rowRect = new Rect(labelX, currentY - itemHeight / 2, tagWidth, itemHeight);
                dc.DrawRoundedRectangle(rowBrush, null, rowRect, 3, 3);
            }

            dc.DrawText(nameFormatted, new Point(labelX + hPadding, currentY - nameFormatted.Height / 2));
            hitAreas.Add(new Rect(labelX, currentY - itemHeight / 2, tagWidth, itemHeight));
            currentY += itemHeight;
        }

        _hitTestService.RegisterExpandedTagHitArea(node.RowIndex, hitAreas);

        var linePen = new Pen(tagBgBrush, 1.5);
        linePen.Freeze();

        if (node.IsMerge)
        {
            double mergeRadius = NodeRadius * 0.875;
            var fullArea = _cacheService.GetFullArea(ActualWidth, ActualHeight);
            var mergeCircle = new EllipseGeometry(new Point(nodeX, y), mergeRadius, mergeRadius);
            var clipGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, fullArea, mergeCircle);
            clipGeometry.Freeze();

            dc.PushClip(clipGeometry);
            dc.DrawLine(linePen, new Point(labelX + tagWidth, y), new Point(nodeX, y));
            dc.Pop();
        }
        else
        {
            double lineEndX = nodeX - NodeRadius - 4;
            dc.DrawLine(linePen, new Point(labelX + tagWidth, y), new Point(lineEndX, y));
        }
    }

    /// <summary>
    /// Draw expanded tag showing all branches as rows inside one tall tag.
    /// </summary>
    private void DrawExpandedBranchLabels(DrawingContext dc, GitTreeNode node, double baseY, double nodeX, int rowOffset)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double labelX = 4;
        int displayRow = node.RowIndex + rowOffset;
        int nodeIndex = node.RowIndex;

        // Get animation progress (1.0 = fully expanded) with ease-out
        double rawProgress = _stateService.GetExpansionProgress(nodeIndex);
        if (rawProgress == 0.0) rawProgress = 1.0; // Default to fully expanded
        double progress = EaseOut(rawProgress);

        // Use first label's color for the expanded tag background
        var firstLabel = node.BranchLabels[0];
        Brush tagBgBrush = GraphBuilder.GetBranchColor(firstLabel.Name);

        // Calculate dimensions - use first label's width (same as collapsed state with +N)
        double firstFontSize = firstLabel.IsCurrent ? 13 : 11;
        double itemHeight = firstLabel.IsCurrent ? 22 : 18;
        double hPadding = firstLabel.IsCurrent ? 8 : 6;

        // Measure first label to get the collapsed tag width
        var firstNameFormatted = new FormattedText(
            firstLabel.Name,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            firstFontSize,
            LabelTextBrush,
            dpi);

        // Check if first label needs custom remote icon
        bool firstUseCustomRemoteIcon = firstLabel.IsRemote &&
            (firstLabel.RemoteType == RemoteType.GitHub || firstLabel.RemoteType == RemoteType.AzureDevOps);

        var firstIconText = "";
        if (firstLabel.IsLocal) firstIconText += ComputerIcon;
        if (firstLabel.IsLocal && firstLabel.IsRemote) firstIconText += " ";
        if (firstLabel.IsRemote && !firstUseCustomRemoteIcon) firstIconText += CloudIcon;

        var firstIconFormatted = new FormattedText(
            firstIconText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            IconTypeface,
            firstFontSize,
            LabelTextBrush,
            dpi);

        // Calculate custom icon size for first label
        double firstCustomIconSize = firstUseCustomRemoteIcon ? firstFontSize : 0;
        double firstCustomIconSpace = firstUseCustomRemoteIcon ? (firstIconFormatted.Width > 0 ? 2 : 0) : 0;

        // Include the "+N" suffix width in the calculation
        int overflowCount = node.BranchLabels.Count - 1;
        var suffixFormatted = new FormattedText(
            $" +{overflowCount}",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            firstFontSize,
            LabelTextBrush,
            dpi);

        // Tag width matches collapsed state
        double tagWidth = hPadding + firstNameFormatted.Width + 4 + firstIconFormatted.Width + firstCustomIconSpace + firstCustomIconSize + suffixFormatted.Width + hPadding;

        // Calculate heights
        double otherItemHeight = 22;
        double fullExpandedHeight = itemHeight + ((node.BranchLabels.Count - 1) * otherItemHeight);

        // Animate height from collapsed (itemHeight) to fully expanded
        double tagHeight = itemHeight + ((fullExpandedHeight - itemHeight) * progress);

        // Tag top stays at same position as collapsed tag (centered on baseY)
        double tagTop = baseY - itemHeight / 2;

        // Draw the single expanded tag background
        var tagRect = new Rect(labelX, tagTop, tagWidth, tagHeight);
        dc.DrawRoundedRectangle(tagBgBrush, LabelBorderPen, tagRect, 4, 4);

        // Clear and rebuild hit areas for this node
        var hitAreas = new List<(BranchLabel Label, Rect HitArea)>();

        // Draw each branch name as a row inside the tag
        double currentY = baseY;
        int branchIndex = 0;

        var hoveredItem = _stateService.HoveredExpandedItem;

        foreach (var label in node.BranchLabels)
        {
            double labelFontSize = label.IsCurrent ? 13 : 11;
            double currentItemHeight = branchIndex == 0 ? itemHeight : otherItemHeight;
            double itemHPadding = label.IsCurrent ? 8 : 6;

            // Only draw if within animated bounds
            double itemTop = currentY - currentItemHeight / 2;
            if (itemTop < tagTop + tagHeight)
            {
                bool isHovered = hoveredItem == (nodeIndex, branchIndex);

                Brush branchColorBrush = GraphBuilder.GetBranchColor(label.Name);
                var branchColor = ((SolidColorBrush)branchColorBrush).Color;

                // Draw border and background around non-first items
                if (branchIndex > 0)
                {
                    byte alpha = isHovered ? (byte)255 : (byte)200;
                    var bgBrush = new SolidColorBrush(Color.FromArgb(alpha, branchColor.R, branchColor.G, branchColor.B));
                    bgBrush.Freeze();
                    var borderRect = new Rect(labelX, itemTop, tagWidth, currentItemHeight);
                    dc.DrawRoundedRectangle(bgBrush, null, borderRect, 3, 3);
                }

                var nameFormatted = new FormattedText(
                    label.Name,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    labelFontSize,
                    LabelTextBrush,
                    dpi);
                nameFormatted.MaxLineCount = 1;
                nameFormatted.Trimming = TextTrimming.CharacterEllipsis;

                bool useCustomRemoteIconExp = label.IsRemote &&
                    (label.RemoteType == RemoteType.GitHub || label.RemoteType == RemoteType.AzureDevOps);

                var iconText = "";
                if (label.IsLocal) iconText += ComputerIcon;
                if (label.IsLocal && label.IsRemote) iconText += " ";
                if (label.IsRemote && !useCustomRemoteIconExp) iconText += CloudIcon;

                var iconFormatted = new FormattedText(
                    iconText,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    IconTypeface,
                    labelFontSize,
                    LabelTextBrush,
                    dpi);

                double customIconSizeExp = useCustomRemoteIconExp ? labelFontSize : 0;
                double customIconSpaceExp = useCustomRemoteIconExp ? (iconFormatted.Width > 0 ? 2 : 0) : 0;
                double iconBlockWidthExp = iconFormatted.Width + customIconSpaceExp + customIconSizeExp;
                double nameMaxWidthExp = tagWidth - (itemHPadding * 2) - (iconBlockWidthExp > 0 ? iconBlockWidthExp + 4 : 0);
                if (nameMaxWidthExp > 0)
                {
                    nameFormatted.MaxTextWidth = nameMaxWidthExp;
                }

                // Calculate opacity based on how much of the item is visible during animation
                double visibleRatio = Math.Min(1.0, Math.Max(0.0, (tagTop + tagHeight - itemTop) / currentItemHeight));

                if (visibleRatio > 0.3)
                {
                    dc.DrawText(nameFormatted, new Point(labelX + itemHPadding, currentY - nameFormatted.Height / 2));

                    double expIconX = labelX + itemHPadding + nameFormatted.Width + 4;
                    dc.DrawText(iconFormatted, new Point(expIconX, currentY - iconFormatted.Height / 2));

                    if (useCustomRemoteIconExp)
                    {
                        double customIconXExp = expIconX + iconFormatted.Width + customIconSpaceExp;
                        double customIconYExp = currentY - customIconSizeExp / 2;

                        Geometry iconGeometry = label.RemoteType == RemoteType.GitHub
                            ? GitHubLogoGeometry
                            : AzureDevOpsLogoGeometry;

                        double sourceWidth = label.RemoteType == RemoteType.GitHub ? GitHubLogoWidth : AzureDevOpsLogoWidth;
                        double sourceHeight = label.RemoteType == RemoteType.GitHub ? GitHubLogoHeight : AzureDevOpsLogoHeight;

                        double scale = customIconSizeExp / Math.Max(sourceWidth, sourceHeight);

                        var transform = new TransformGroup();
                        transform.Children.Add(new ScaleTransform(scale, scale));
                        transform.Children.Add(new TranslateTransform(customIconXExp, customIconYExp));
                        transform.Freeze();

                        dc.PushTransform(transform);
                        dc.DrawGeometry(LabelTextBrush, null, iconGeometry);
                        dc.Pop();
                    }
                }

                var itemHitRect = new Rect(labelX, itemTop, tagWidth, currentItemHeight);
                hitAreas.Add((label, itemHitRect));
            }

            if (branchIndex == 0)
            {
                currentY += itemHeight / 2 + otherItemHeight / 2;
            }
            else
            {
                currentY += otherItemHeight;
            }
            branchIndex++;
        }

        _hitTestService.RegisterExpandedItemHitArea(nodeIndex, hitAreas);

        // Draw connecting line from tag to node
        var linePen = new Pen(tagBgBrush, 1.5);
        linePen.Freeze();

        if (node.IsMerge)
        {
            double expandedMergeRadius = NodeRadius * 0.875;
            var fullArea = _cacheService.GetFullArea(ActualWidth, ActualHeight);
            var mergeCircle = new EllipseGeometry(new Point(nodeX, baseY), expandedMergeRadius, expandedMergeRadius);
            var clipGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, fullArea, mergeCircle);
            clipGeometry.Freeze();

            dc.PushClip(clipGeometry);
            dc.DrawLine(linePen, new Point(labelX + tagWidth, baseY), new Point(nodeX, baseY));
            dc.Pop();
        }
        else
        {
            double lineEndX = nodeX - NodeRadius - 4;
            dc.DrawLine(linePen, new Point(labelX + tagWidth, baseY), new Point(lineEndX, baseY));
        }

        // Store hit area for click-to-collapse
        _hitTestService.RegisterOverflowHitArea(displayRow, node.BranchLabels.Skip(1).ToList(), tagRect);
    }
}
