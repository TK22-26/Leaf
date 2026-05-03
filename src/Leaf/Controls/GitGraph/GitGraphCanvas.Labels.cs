using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Leaf.Graph;
using Leaf.Models;

namespace Leaf.Controls.GitGraph;

public partial class GitGraphCanvas
{
    // §5.17 — per-chip tag hit areas. Registered during DrawTagLabels,
    // queried by hover (signature tooltip) and right-click (context
    // menu) handlers. Cleared at the start of each render. Keyed by
    // display row so a small graph doesn't iterate every row's tags.
    private readonly Dictionary<int, List<(string Name, Rect Rect)>> _tagChipHitAreas = new();

    /// <summary>
    /// Look up a tag chip at <paramref name="position"/> using the
    /// hit-areas registered during <see cref="DrawTagLabels"/>. Returns
    /// the tag name (caller resolves <see cref="TagInfo"/> via
    /// <see cref="TagsByName"/>) or null when the cursor isn't on a
    /// chip.
    /// </summary>
    internal string? GetTagAt(Point position)
    {
        foreach (var (_, chips) in _tagChipHitAreas)
        {
            foreach (var (name, rect) in chips)
            {
                if (rect.Contains(position)) return name;
            }
        }
        return null;
    }

    /// <summary>
    /// Render the §5.17 signature dot — a small filled circle anchored
    /// at the bottom-right of a tag chip, colour-matched to the
    /// commit-side signature badge palette. Skips silently when the
    /// TagsByName lookup is empty (no enrichment yet) or the tag is
    /// unsigned. Annotated-but-unsigned tags get a subtle neutral dot
    /// so they're still distinguishable from lightweight chips.
    /// </summary>
    private void DrawTagSignatureDot(DrawingContext dc, Rect labelRect, string tagName)
    {
        if (TagsByName is null || !TagsByName.TryGetValue(tagName, out var tag) || tag is null)
            return;

        Color? fill = null;
        if (tag.IsSigned)
        {
            fill = tag.SignatureStatus switch
            {
                CommitSignatureStatus.Valid => Color.FromRgb(0x2E, 0xA0, 0x43),
                CommitSignatureStatus.RevokedKey => Color.FromRgb(0xC8, 0x35, 0x35),
                CommitSignatureStatus.Bad => Color.FromRgb(0xC8, 0x35, 0x35),
                _ => Color.FromRgb(0xBF, 0x83, 0x00),
            };
        }
        else if (tag.IsAnnotated)
        {
            // Annotated-only — small amber-tinted dot with low opacity so
            // the chip is visibly different from a lightweight tag without
            // implying signature trust.
            fill = Color.FromArgb(0xC0, 0xBF, 0x83, 0x00);
        }

        if (fill is null) return;

        var brush = new SolidColorBrush(fill.Value);
        brush.Freeze();
        var ringPen = new Pen(Brushes.White, 1) { LineJoin = PenLineJoin.Round };
        ringPen.Freeze();
        const double dotRadius = 3.5;
        var center = new Point(labelRect.Right - dotRadius - 1, labelRect.Bottom - dotRadius - 1);
        dc.DrawEllipse(brush, ringPen, center, dotRadius, dotRadius);
    }

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
            Brush bgBrush = ResolveBranchColor(label.Name);

            // Current branch gets enlarged styling
            double fontSize = label.IsCurrent ? 13 : 11;
            double iconFontSize = label.IsCurrent ? 13 : 11;
            double labelHeight = label.IsCurrent ? 22 : 18;
            double cornerRadius = label.IsCurrent ? 5 : 4;

            // Build icon text for local (computer icon) and generic cloud remotes
            var iconText = "";
            if (label.IsLocal)
                iconText += ComputerIcon;

            // Count how many custom icons we need (GitHub, Azure DevOps)
            // and how many generic cloud icons (Other remotes)
            int customIconCount = 0;
            int genericCloudCount = 0;
            foreach (var remote in label.Remotes)
            {
                if (remote.RemoteType == RemoteType.GitHub || remote.RemoteType == RemoteType.AzureDevOps)
                    customIconCount++;
                else
                    genericCloudCount++;
            }

            // Add spacing and cloud icons for generic remotes
            if (label.IsLocal && (customIconCount > 0 || genericCloudCount > 0))
                iconText += " ";
            for (int i = 0; i < genericCloudCount; i++)
            {
                if (i > 0) iconText += " ";
                iconText += CloudIcon;
            }

            // Measure icon text (computer + generic clouds)
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

            // Calculate custom remote icons size (GitHub/Azure DevOps logos)
            double singleCustomIconSize = iconFontSize;
            double customIconSpacing = 2;
            double totalCustomIconWidth = customIconCount > 0
                ? (customIconCount * singleCustomIconSize) + ((customIconCount - 1) * customIconSpacing)
                : 0;
            double customIconSpace = (customIconCount > 0 && iconFormatted.Width > 0) ? 2 : 0;

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

            double iconBlockWidth = iconWidth + customIconSpace + totalCustomIconWidth;
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

            // Draw custom remote icons (GitHub/Azure DevOps) for each remote
            if (customIconCount > 0)
            {
                double customIconX = iconX + iconWidth + customIconSpace;
                double customIconY = y - singleCustomIconSize / 2;

                foreach (var remote in label.Remotes)
                {
                    if (remote.RemoteType != RemoteType.GitHub && remote.RemoteType != RemoteType.AzureDevOps)
                        continue;

                    Geometry iconGeometry = remote.RemoteType == RemoteType.GitHub
                        ? GitHubLogoGeometry
                        : AzureDevOpsLogoGeometry;

                    double sourceWidth = remote.RemoteType == RemoteType.GitHub ? GitHubLogoWidth : AzureDevOpsLogoWidth;
                    double sourceHeight = remote.RemoteType == RemoteType.GitHub ? GitHubLogoHeight : AzureDevOpsLogoHeight;

                    double scale = singleCustomIconSize / Math.Max(sourceWidth, sourceHeight);

                    var transform = new TransformGroup();
                    transform.Children.Add(new ScaleTransform(scale, scale));
                    transform.Children.Add(new TranslateTransform(customIconX, customIconY));
                    transform.Freeze();

                    dc.PushTransform(transform);
                    dc.DrawGeometry(LabelTextBrush, null, iconGeometry);
                    dc.Pop();

                    customIconX += singleCustomIconSize + customIconSpacing;
                }
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
            var baseBrush = ResolveBranchColor(tagName) as SolidColorBrush ?? Brushes.Gray;
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

            // §5.17 — register a per-chip hit area so hover and right-
            // click can identify which tag the cursor is on. Keyed by
            // displayRow so the lookup loop in GetTagAt skips rows that
            // don't have any tags.
            if (!_tagChipHitAreas.TryGetValue(displayRow, out var chipList))
            {
                chipList = new List<(string, Rect)>(node.TagNames.Count);
                _tagChipHitAreas[displayRow] = chipList;
            }
            chipList.Add((tagName, labelRect));

            // §5.17 — paint a small signature dot at the chip's bottom-
            // right when the tag has signature data. Reuses the colour
            // palette from the commit signature badge so commits + tags
            // speak one visual vocabulary.
            DrawTagSignatureDot(dc, labelRect, tagName);

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

        Brush baseBrush = ResolveBranchColor(labelText);
        Color baseColor = ((SolidColorBrush)baseBrush).Color;
        var ghostBrush = new SolidColorBrush(Color.FromArgb(
            (byte)(baseColor.A * GhostTagOpacity),
            baseColor.R, baseColor.G, baseColor.B));
        ghostBrush.Freeze();

        var ghostTextBrush = new SolidColorBrush(Color.FromArgb(
            (byte)(255 * GhostTagOpacity), 255, 255, 255));
        ghostTextBrush.Freeze();

        bool isLocal = false;
        var remotes = new List<RemoteBranchInfo>();

        var matchingLabel = node.BranchLabels.FirstOrDefault(l =>
            l.Name.Equals(labelText, StringComparison.OrdinalIgnoreCase));
        if (matchingLabel != null)
        {
            isLocal = matchingLabel.IsLocal;
            remotes = matchingLabel.Remotes;
        }
        else if (_branchLabelLookup.TryGetValue(labelText, out var globalLabel))
        {
            isLocal = globalLabel.IsLocal;
            remotes = globalLabel.Remotes;
        }

        // Count custom icons and generic cloud icons
        int customIconCount = 0;
        int genericCloudCount = 0;
        foreach (var remote in remotes)
        {
            if (remote.RemoteType == RemoteType.GitHub || remote.RemoteType == RemoteType.AzureDevOps)
                customIconCount++;
            else
                genericCloudCount++;
        }

        var iconText = "";
        if (isLocal)
            iconText += ComputerIcon;
        if (isLocal && (customIconCount > 0 || genericCloudCount > 0))
            iconText += " ";
        for (int i = 0; i < genericCloudCount; i++)
        {
            if (i > 0) iconText += " ";
            iconText += CloudIcon;
        }

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
        nameFormatted.MaxLineCount = 1;
        nameFormatted.Trimming = TextTrimming.CharacterEllipsis;

        double ghostCustomIconSize = 11;
        double ghostCustomIconSpacing = 2;
        double totalCustomIconWidth = customIconCount > 0
            ? (customIconCount * ghostCustomIconSize) + ((customIconCount - 1) * ghostCustomIconSpacing)
            : 0;
        double ghostCustomIconSpace = (customIconCount > 0 && iconFormatted.Width > 0) ? 2 : 0;

        double iconWidth = iconFormatted.Width;
        double nameWidth = nameFormatted.Width;
        double iconBlockWidth = iconWidth + ghostCustomIconSpace + totalCustomIconWidth;

        // Constrain name width to available label area (same as normal branch labels)
        double availableWidth = LabelAreaWidth - 8 - labelX;
        double fixedWidth = iconBlockWidth > 0 ? 6 + 4 + iconBlockWidth + 6 : 6 + 6;
        double nameMaxWidth = availableWidth - fixedWidth;
        if (nameMaxWidth > 0 && nameWidth > nameMaxWidth)
        {
            nameFormatted.MaxTextWidth = nameMaxWidth;
            nameWidth = nameMaxWidth;
        }

        double totalWidth = iconBlockWidth > 0
            ? 6 + nameWidth + 4 + iconBlockWidth + 6
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

        // Draw custom remote icons (GitHub/Azure DevOps)
        if (customIconCount > 0)
        {
            double customIconXGhost = ghostIconX + iconWidth + ghostCustomIconSpace;
            double customIconYGhost = y - ghostCustomIconSize / 2;

            foreach (var remote in remotes)
            {
                if (remote.RemoteType != RemoteType.GitHub && remote.RemoteType != RemoteType.AzureDevOps)
                    continue;

                Geometry iconGeometry = remote.RemoteType == RemoteType.GitHub
                    ? GitHubLogoGeometry
                    : AzureDevOpsLogoGeometry;

                double sourceWidth = remote.RemoteType == RemoteType.GitHub ? GitHubLogoWidth : AzureDevOpsLogoWidth;
                double sourceHeight = remote.RemoteType == RemoteType.GitHub ? GitHubLogoHeight : AzureDevOpsLogoHeight;

                double scale = ghostCustomIconSize / Math.Max(sourceWidth, sourceHeight);

                var transform = new TransformGroup();
                transform.Children.Add(new ScaleTransform(scale, scale));
                transform.Children.Add(new TranslateTransform(customIconXGhost, customIconYGhost));
                transform.Freeze();

                dc.PushTransform(transform);
                dc.DrawGeometry(ghostTextBrush, null, iconGeometry);
                dc.Pop();

                customIconXGhost += ghostCustomIconSize + ghostCustomIconSpacing;
            }
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
