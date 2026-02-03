using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Leaf.Graph;
using Leaf.Models;

namespace Leaf.Controls.GitGraph;

public partial class GitGraphCanvas
{
    private double DrawBranchLabels(DrawingContext dc, GitTreeNode node, int rowOffset = 0)
    {
        double y = GetYForRow(node.RowIndex + rowOffset);
        double nodeX = GetXForColumn(node.ColumnIndex);
        double labelX = 4; // Start from left edge with small padding
        double lastLabelRight = 0;
        Brush? lastLabelBrush = null;
        bool lastLabelIsCurrent = false;
        int drawnCount = 0;
        int displayRow = node.RowIndex + rowOffset;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Check if this row is expanded - if so, skip (drawn in final pass as overlay)
        int nodeIndex = node.RowIndex;
        if (_stateService.IsNodeExpanded(nodeIndex))
        {
            return 0;
        }

        foreach (var label in node.BranchLabels)
        {
            // Each branch gets a unique color generated from its name
            Brush bgBrush = GraphBuilder.GetBranchColor(label.Name);

            // Current branch gets enlarged styling
            double fontSize = label.IsCurrent ? 13 : 11;
            double iconFontSize = label.IsCurrent ? 13 : 11;
            double labelHeight = label.IsCurrent ? 22 : 18;
            double cornerRadius = label.IsCurrent ? 5 : 4;

            // Check if we need to draw a custom remote icon (GitHub/AzureDevOps)
            bool useCustomRemoteIcon = label.IsRemote &&
                (label.RemoteType == RemoteType.GitHub || label.RemoteType == RemoteType.AzureDevOps);

            // Build the label text with icons (icons AFTER name now)
            var iconText = "";
            if (label.IsLocal)
                iconText += ComputerIcon;
            if (label.IsLocal && label.IsRemote)
                iconText += " ";
            if (label.IsRemote && !useCustomRemoteIcon)
                iconText += CloudIcon;

            // Measure icon text
            var iconFormatted = new FormattedText(
                iconText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                IconTypeface,
                iconFontSize,
                LabelTextBrush,
                dpi);

            // Measure branch name text
            var nameFormatted = new FormattedText(
                label.Name,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                fontSize,
                LabelTextBrush,
                dpi);
            nameFormatted.MaxLineCount = 1;
            nameFormatted.Trimming = TextTrimming.CharacterEllipsis;

            // Calculate custom remote icon size
            double customIconSize = useCustomRemoteIcon ? iconFontSize : 0;
            double customIconSpace = useCustomRemoteIcon ? (iconFormatted.Width > 0 ? 2 : 0) : 0;

            // Calculate label dimensions
            double iconWidth = iconFormatted.Width;
            double nameWidth = nameFormatted.Width;
            double hPadding = label.IsCurrent ? 8 : 6;

            // Check if this is the last label that will fit - if more remain, add "+N" suffix
            int remainingAfterThis = node.BranchLabels.Count - drawnCount - 1;
            string overflowSuffix = "";
            double suffixWidth = 0;
            FormattedText? suffixFormatted = null;

            if (remainingAfterThis > 0)
            {
                overflowSuffix = $" +{remainingAfterThis}";
                suffixFormatted = new FormattedText(
                    overflowSuffix,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    fontSize,
                    LabelTextBrush,
                    dpi);
                suffixWidth = suffixFormatted.Width;
            }

            double iconBlockWidth = iconWidth + customIconSpace + customIconSize;
            double gapBetweenIconAndSuffix = (suffixWidth > 0 && iconBlockWidth > 0) ? 6 : 0;
            double rightSectionWidth = iconBlockWidth + gapBetweenIconAndSuffix + suffixWidth;
            double gapBetweenNameAndRight = rightSectionWidth > 0 ? 6 : 0;

            double availableWidth = LabelAreaWidth - 8 - labelX;
            double minRequiredWidth = (hPadding * 2) + rightSectionWidth + gapBetweenNameAndRight;

            if (availableWidth <= minRequiredWidth)
                break;

            double nameMaxWidth = availableWidth - minRequiredWidth;
            if (nameWidth > nameMaxWidth)
            {
                nameFormatted.MaxTextWidth = nameMaxWidth;
                nameFormatted.Trimming = TextTrimming.CharacterEllipsis;
                nameWidth = nameMaxWidth;
            }

            double totalWidth = (hPadding * 2) + gapBetweenNameAndRight + rightSectionWidth + nameWidth;

            // Draw rounded rectangle background
            var labelRect = new Rect(labelX, y - labelHeight / 2, totalWidth, labelHeight);
            dc.DrawRoundedRectangle(bgBrush, LabelBorderPen, labelRect, cornerRadius, cornerRadius);

            // Draw branch name first
            dc.DrawText(nameFormatted, new Point(labelX + hPadding, y - nameFormatted.Height / 2));

            // Right-align icons and suffix
            double rightSectionX = labelRect.Right - hPadding - rightSectionWidth;
            double suffixX = rightSectionX + rightSectionWidth - suffixWidth;
            double iconX = suffixWidth > 0
                ? suffixX - (iconBlockWidth > 0 ? gapBetweenIconAndSuffix + iconBlockWidth : 0)
                : rightSectionX + rightSectionWidth - iconBlockWidth;

            if (iconBlockWidth > 0)
            {
                dc.DrawText(iconFormatted, new Point(iconX, y - iconFormatted.Height / 2));
            }

            // Draw custom remote icon (GitHub/Azure DevOps) if needed
            if (useCustomRemoteIcon)
            {
                double customIconX = iconX + iconWidth + customIconSpace;
                double customIconY = y - customIconSize / 2;

                Geometry iconGeometry = label.RemoteType == RemoteType.GitHub
                    ? GitHubLogoGeometry
                    : AzureDevOpsLogoGeometry;

                double sourceWidth = label.RemoteType == RemoteType.GitHub ? GitHubLogoWidth : AzureDevOpsLogoWidth;
                double sourceHeight = label.RemoteType == RemoteType.GitHub ? GitHubLogoHeight : AzureDevOpsLogoHeight;

                double scale = customIconSize / Math.Max(sourceWidth, sourceHeight);

                var transform = new TransformGroup();
                transform.Children.Add(new ScaleTransform(scale, scale));
                transform.Children.Add(new TranslateTransform(customIconX, customIconY));
                transform.Freeze();

                dc.PushTransform(transform);
                dc.DrawGeometry(LabelTextBrush, null, iconGeometry);
                dc.Pop();
            }

            // Draw overflow suffix if present
            if (!string.IsNullOrEmpty(overflowSuffix) && suffixFormatted != null)
            {
                dc.DrawText(suffixFormatted, new Point(suffixX, y - suffixFormatted.Height / 2));

                // Store overflow info
                var overflowLabels = node.BranchLabels.Skip(drawnCount + 1).ToList();
                _hitTestService.RegisterOverflowHitArea(displayRow, overflowLabels, labelRect);
            }

            // Track the rightmost edge of labels and the last brush color
            lastLabelRight = labelX + totalWidth;
            lastLabelBrush = bgBrush;
            lastLabelIsCurrent = label.IsCurrent;
            drawnCount++;

            // If we added overflow suffix, we're done
            if (!string.IsNullOrEmpty(overflowSuffix))
                break;

            // Move X for next label
            labelX += totalWidth + 4;
        }

        // Draw connecting line from last label to the commit node
        if (node.BranchLabels.Count > 0 && lastLabelRight > 0 && lastLabelBrush != null)
        {
            double lineThickness = lastLabelIsCurrent ? 2.5 : 1.5;
            var linePen = new Pen(lastLabelBrush, lineThickness);
            linePen.Freeze();

            if (node.IsMerge)
            {
                double mergeRadius = NodeRadius * 0.875;
                var fullArea = _cacheService.GetFullArea(ActualWidth, ActualHeight);
                var mergeCircle = new EllipseGeometry(new Point(nodeX, y), mergeRadius, mergeRadius);
                var clipGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, fullArea, mergeCircle);
                clipGeometry.Freeze();

                dc.PushClip(clipGeometry);
                dc.DrawLine(linePen, new Point(lastLabelRight, y), new Point(nodeX, y));
                dc.Pop();
            }
            else
            {
                double lineEndX = nodeX - NodeRadius - 4;
                dc.DrawLine(linePen, new Point(lastLabelRight, y), new Point(lineEndX, y));
            }
        }

        return lastLabelRight;
    }

    private void DrawTagLabels(DrawingContext dc, GitTreeNode node, int rowOffset, double startX)
    {
        double y = GetYForRow(node.RowIndex + rowOffset);
        double nodeX = GetXForColumn(node.ColumnIndex);
        double labelX = Math.Max(4, startX);
        double lastLabelRight = 0;
        Brush? lastLabelBrush = null;
        int drawnCount = 0;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        int displayRow = node.RowIndex + rowOffset;

        if (_stateService.IsTagNodeExpanded(node.RowIndex))
        {
            _hitTestService.RegisterTagOverflowHitArea(displayRow, node.TagNames.ToList(), Rect.Empty, labelX);
            return;
        }

        var ghostTextBrush = new SolidColorBrush(Color.FromArgb(
            (byte)(255 * GhostTagOpacity), 255, 255, 255));
        ghostTextBrush.Freeze();

        foreach (var tagName in node.TagNames)
        {
            var baseBrush = GraphBuilder.GetBranchColor(tagName) as SolidColorBrush ?? Brushes.Gray;
            var baseColor = baseBrush.Color;
            var ghostBrush = new SolidColorBrush(Color.FromArgb(
                (byte)(baseColor.A * GhostTagOpacity),
                baseColor.R, baseColor.G, baseColor.B));
            ghostBrush.Freeze();

            double fontSize = 11;
            double labelHeight = 18;
            double cornerRadius = 4;
            double hPadding = 6;

            var nameFormatted = new FormattedText(
                tagName,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                fontSize,
                ghostTextBrush,
                dpi);

            double nameWidth = nameFormatted.Width;
            double totalWidth = hPadding + nameWidth + hPadding;

            int remainingAfterThis = node.TagNames.Count - drawnCount - 1;
            string overflowSuffix = "";
            double suffixWidth = 0;

            if (remainingAfterThis > 0)
            {
                var nextNameFormatted = new FormattedText(
                    node.TagNames[drawnCount + 1],
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    fontSize,
                    ghostTextBrush,
                    dpi);

                double nextEstWidth = hPadding * 2 + nextNameFormatted.Width;
                if (labelX + totalWidth + 4 + nextEstWidth > LabelAreaWidth - 8)
                {
                    overflowSuffix = $" +{remainingAfterThis}";
                    var suffixFormatted = new FormattedText(
                        overflowSuffix,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        LabelTypeface,
                        fontSize,
                        ghostTextBrush,
                        dpi);
                    suffixWidth = suffixFormatted.Width;
                    totalWidth += suffixWidth;
                }
            }

            if (labelX + totalWidth > LabelAreaWidth - 8)
                break;

            var labelRect = new Rect(labelX, y - labelHeight / 2, totalWidth, labelHeight);
            dc.DrawRoundedRectangle(ghostBrush, LabelBorderPen, labelRect, cornerRadius, cornerRadius);
            dc.DrawText(nameFormatted, new Point(labelX + hPadding, y - nameFormatted.Height / 2));

            if (!string.IsNullOrEmpty(overflowSuffix))
            {
                var suffixFormatted = new FormattedText(
                    overflowSuffix,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    fontSize,
                    ghostTextBrush,
                    dpi);
                dc.DrawText(suffixFormatted, new Point(labelX + hPadding + nameWidth, y - suffixFormatted.Height / 2));
                lastLabelRight = labelX + totalWidth;
                lastLabelBrush = ghostBrush;
                var overflowTags = node.TagNames.Skip(drawnCount + 1).ToList();
                _hitTestService.RegisterTagOverflowHitArea(displayRow, overflowTags, labelRect, labelX);
                break;
            }

            lastLabelRight = labelX + totalWidth;
            lastLabelBrush = ghostBrush;
            drawnCount++;
            labelX += totalWidth + 4;
        }

        if (lastLabelRight > 0 && lastLabelBrush != null)
        {
            var linePen = new Pen(lastLabelBrush, 1.5);
            linePen.Freeze();

            if (node.IsMerge)
            {
                double mergeRadius = NodeRadius * 0.875;
                var fullArea = _cacheService.GetFullArea(ActualWidth, ActualHeight);
                var mergeCircle = new EllipseGeometry(new Point(nodeX, y), mergeRadius, mergeRadius);
                var clipGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, fullArea, mergeCircle);
                clipGeometry.Freeze();

                dc.PushClip(clipGeometry);
                dc.DrawLine(linePen, new Point(lastLabelRight, y), new Point(nodeX, y));
                dc.Pop();
            }
            else
            {
                double lineEndX = nodeX - NodeRadius - 4;
                dc.DrawLine(linePen, new Point(lastLabelRight, y), new Point(lineEndX, y));
            }
        }
    }

    private void DrawGhostTag(DrawingContext dc, GitTreeNode node, int rowOffset = 0)
    {
        string labelText = node.PrimaryBranch ?? node.Sha[..7];

        double y = GetYForRow(node.RowIndex + rowOffset);
        double nodeX = GetXForColumn(node.ColumnIndex);
        double labelX = 4;

        Brush baseBrush = GraphBuilder.GetBranchColor(labelText);
        Color baseColor = ((SolidColorBrush)baseBrush).Color;
        var ghostBrush = new SolidColorBrush(Color.FromArgb(
            (byte)(baseColor.A * GhostTagOpacity),
            baseColor.R, baseColor.G, baseColor.B));
        ghostBrush.Freeze();

        var ghostTextBrush = new SolidColorBrush(Color.FromArgb(
            (byte)(255 * GhostTagOpacity), 255, 255, 255));
        ghostTextBrush.Freeze();

        bool isLocal = false;
        bool isRemote = false;
        RemoteType remoteType = RemoteType.Other;

        var matchingLabel = node.BranchLabels.FirstOrDefault(l =>
            l.Name.Equals(labelText, StringComparison.OrdinalIgnoreCase));
        if (matchingLabel != null)
        {
            isLocal = matchingLabel.IsLocal;
            isRemote = matchingLabel.IsRemote;
            remoteType = matchingLabel.RemoteType;
        }
        else if (node.PrimaryBranch != null)
        {
            isLocal = !labelText.Contains('/');
            isRemote = labelText.Contains('/');
        }

        bool useCustomRemoteIconGhost = isRemote && (remoteType == RemoteType.GitHub || remoteType == RemoteType.AzureDevOps);

        var iconText = "";
        if (isLocal)
            iconText += ComputerIcon;
        if (isLocal && isRemote)
            iconText += " ";
        if (isRemote && !useCustomRemoteIconGhost)
            iconText += CloudIcon;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var iconFormatted = new FormattedText(
            iconText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            IconTypeface,
            11,
            ghostTextBrush,
            dpi);

        var nameFormatted = new FormattedText(
            labelText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            11,
            ghostTextBrush,
            dpi);

        double ghostCustomIconSize = useCustomRemoteIconGhost ? 11 : 0;
        double ghostCustomIconSpace = useCustomRemoteIconGhost ? (iconFormatted.Width > 0 ? 2 : 0) : 0;

        double iconWidth = iconFormatted.Width;
        double nameWidth = nameFormatted.Width;
        double totalWidth = iconWidth > 0 || ghostCustomIconSize > 0
            ? 6 + nameWidth + 4 + iconWidth + ghostCustomIconSpace + ghostCustomIconSize + 6
            : 6 + nameWidth + 6;
        double labelHeight = 18;

        var labelRect = new Rect(labelX, y - labelHeight / 2, totalWidth, labelHeight);
        dc.DrawRoundedRectangle(ghostBrush, LabelBorderPen, labelRect, 4, 4);

        dc.DrawText(nameFormatted, new Point(labelX + 6, y - nameFormatted.Height / 2));

        double ghostIconX = labelX + 6 + nameWidth + 4;
        if (iconWidth > 0)
        {
            dc.DrawText(iconFormatted, new Point(ghostIconX, y - iconFormatted.Height / 2));
        }

        if (useCustomRemoteIconGhost)
        {
            double customIconXGhost = ghostIconX + iconWidth + ghostCustomIconSpace;
            double customIconYGhost = y - ghostCustomIconSize / 2;

            Geometry iconGeometry = remoteType == RemoteType.GitHub
                ? GitHubLogoGeometry
                : AzureDevOpsLogoGeometry;

            double sourceWidth = remoteType == RemoteType.GitHub ? GitHubLogoWidth : AzureDevOpsLogoWidth;
            double sourceHeight = remoteType == RemoteType.GitHub ? GitHubLogoHeight : AzureDevOpsLogoHeight;

            double scale = ghostCustomIconSize / Math.Max(sourceWidth, sourceHeight);

            var transform = new TransformGroup();
            transform.Children.Add(new ScaleTransform(scale, scale));
            transform.Children.Add(new TranslateTransform(customIconXGhost, customIconYGhost));
            transform.Freeze();

            dc.PushTransform(transform);
            dc.DrawGeometry(ghostTextBrush, null, iconGeometry);
            dc.Pop();
        }

        var linePen = new Pen(ghostBrush, 1.5);
        linePen.Freeze();

        if (node.IsMerge)
        {
            double ghostMergeRadius = NodeRadius * 0.875;
            var fullArea = _cacheService.GetFullArea(ActualWidth, ActualHeight);
            var mergeCircle = new EllipseGeometry(new Point(nodeX, y), ghostMergeRadius, ghostMergeRadius);
            var clipGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, fullArea, mergeCircle);
            clipGeometry.Freeze();

            dc.PushClip(clipGeometry);
            dc.DrawLine(linePen, new Point(labelX + totalWidth, y), new Point(nodeX, y));
            dc.Pop();
        }
        else
        {
            double lineEndX = nodeX - NodeRadius - 4;
            dc.DrawLine(linePen, new Point(labelX + totalWidth, y), new Point(lineEndX, y));
        }
    }
}
