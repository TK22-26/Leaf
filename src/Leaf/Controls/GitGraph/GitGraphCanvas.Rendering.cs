using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Leaf.Graph;
using Leaf.Models;
using Leaf.Utils;

namespace Leaf.Controls.GitGraph;

public partial class GitGraphCanvas
{
    protected override void OnRender(DrawingContext dc)
    {
        // Clear hit testing areas before re-drawing
        _hitTestService.ClearHitAreas();
        base.OnRender(dc);

        // Row offset: working changes (0 or 1) — stashes are now inline graph nodes
        int workingChangesOffset = HasWorkingChanges ? 1 : 0;
        int rowOffset = workingChangesOffset;

        var nodes = Nodes;

        // Draw working changes row first (at row 0) if present
        if (HasWorkingChanges)
        {
            // Get the branch color for WIP node
            var branchName = CurrentBranchName ?? "main";
            var branchBrush = GraphBuilder.GetBranchColor(branchName) as SolidColorBrush ?? Brushes.Gray;
            var branchColor = branchBrush.Color;

            var headNode = nodes?.FirstOrDefault(n => n.IsHead);
            // Target the topmost node in HEAD's lane (may be a stash above HEAD)
            GitTreeNode? targetNode = null;
            if (headNode != null && nodes != null)
            {
                targetNode = nodes.FirstOrDefault(n => n.ColumnIndex == headNode.ColumnIndex) ?? headNode;
            }
            targetNode ??= nodes != null && nodes.Count > 0 ? nodes[0] : null;
            int wipLane = targetNode?.ColumnIndex ?? 0;

            // Draw connection from WIP directly to current branch head when possible
            double wipX = GetXForColumn(wipLane);
            double wipY = GetYForRow(0);
            double avatarRadius = NodeRadius * 1.875;

            double targetX;
            double targetY;
            if (targetNode != null)
            {
                targetX = GetXForColumn(targetNode.ColumnIndex);
                targetY = GetYForRow(targetNode.RowIndex + rowOffset);
            }
            else
            {
                targetX = wipX;
                targetY = wipY;
            }

            if (targetY > wipY && targetNode != null)
            {
                // Create clip geometry that excludes both the WIP circle and the target node's shape
                var fullArea = _cacheService.GetFullArea(ActualWidth, ActualHeight);
                var wipCircle = new EllipseGeometry(new Point(wipX, wipY), avatarRadius, avatarRadius);

                Geometry targetShape;
                if (targetNode.IsStash)
                {
                    double boxSize = NodeRadius * 1.875;
                    targetShape = new RectangleGeometry(new Rect(targetX - boxSize, targetY - boxSize, boxSize * 2, boxSize * 2), 3, 3);
                }
                else
                {
                    double targetRadius = targetNode.IsMerge ? NodeRadius * 0.875 : NodeRadius * 1.875;
                    targetShape = new EllipseGeometry(new Point(targetX, targetY), targetRadius, targetRadius);
                }

                var clipWithoutWip = new CombinedGeometry(GeometryCombineMode.Exclude, fullArea, wipCircle);
                var clipGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, clipWithoutWip, targetShape);
                clipGeometry.Freeze();

                dc.PushClip(clipGeometry);
                var linePen = _cacheService.GetConnectionPen(branchBrush);
                DrawRailConnection(dc, linePen, wipX, wipY, targetX, targetY, false);
                dc.Pop();
            }

            // Draw WIP node on top of connection line
            DrawWorkingChangesRow(dc, branchColor, wipLane);
        }

        if (nodes == null || nodes.Count == 0)
            return;

        // Calculate visible node range for render culling
        var (minNodeIndex, maxNodeIndex) = GetVisibleNodeRange(nodes, rowOffset);

        // Resolve viewport bounds for pass-through lane drawing
        var scrollViewer = FindParentScrollViewer();
        double viewportTop = scrollViewer?.VerticalOffset ?? 0;
        double viewportBottom = viewportTop + GetEffectiveViewportHeight(scrollViewer);

        // Pass 0: Draw pass-through lane lines for connections beyond the culling range
        DrawPassThroughLanes(dc, rowOffset, minNodeIndex, viewportTop, viewportBottom);

        var nodesBySha = _cacheService.GetNodesBySha(nodes);

        // First pass: Draw gradient trails (behind everything)
        for (int i = minNodeIndex; i <= maxNodeIndex; i++)
        {
            var node = nodes[i];
            DrawTrail(dc, node, rowOffset);
        }

        // Second pass: Draw connection lines (behind nodes)
        for (int i = minNodeIndex; i <= maxNodeIndex; i++)
        {
            var node = nodes[i];
            DrawConnections(dc, node, nodesBySha, rowOffset);
        }

        // Third pass: Draw nodes (on top of lines)
        for (int i = minNodeIndex; i <= maxNodeIndex; i++)
        {
            var node = nodes[i];
            DrawNode(dc, node, rowOffset);
        }

        // Fourth pass: Draw branch labels (leftmost, on top of everything)
        for (int i = minNodeIndex; i <= maxNodeIndex; i++)
        {
            var node = nodes[i];
            double branchLabelRight = 0;
            if (node.BranchLabels.Count > 0)
            {
                branchLabelRight = DrawBranchLabels(dc, node, rowOffset);
            }

            if (node.TagNames.Count > 0 && !_stateService.IsNodeExpanded(node.RowIndex))
            {
                double tagStartX = branchLabelRight > 0 ? branchLabelRight + 4 : 4;
                DrawTagLabels(dc, node, rowOffset, tagStartX);
            }
        }

        // Fifth pass: Draw ghost tags for hovered and selected commits
        var selectedNode = SelectedSha != null ? nodesBySha.GetValueOrDefault(SelectedSha) : null;
        var hoveredNode = HoveredSha != null ? nodesBySha.GetValueOrDefault(HoveredSha) : null;

        // Draw selected ghost tag first (so hovered draws on top if same row)
        if (selectedNode != null && selectedNode.BranchLabels.Count == 0 && selectedNode.TagNames.Count == 0 &&
            selectedNode.RowIndex >= minNodeIndex && selectedNode.RowIndex <= maxNodeIndex)
        {
            DrawGhostTag(dc, selectedNode, rowOffset);
        }

        // Draw hovered ghost tag (even if same as selected, it will just overlap)
        if (hoveredNode != null && hoveredNode.BranchLabels.Count == 0 && hoveredNode.TagNames.Count == 0 &&
            hoveredNode.RowIndex >= minNodeIndex && hoveredNode.RowIndex <= maxNodeIndex &&
            hoveredNode.Sha != SelectedSha)
        {
            DrawGhostTag(dc, hoveredNode, rowOffset);
        }

        // Sixth pass: Draw expanded branch dropdowns on top of everything
        var expandedNodes = _stateService.GetExpandedNodes();
        foreach (var kvp in expandedNodes)
        {
            int nodeIndex = kvp.Key;
            if (nodeIndex >= 0 && nodeIndex < nodes.Count)
            {
                var node = nodes[nodeIndex];
                double y = GetYForRow(node.RowIndex + rowOffset);
                double nodeX = GetXForColumn(node.ColumnIndex);
                DrawExpandedBranchLabels(dc, node, y, nodeX, rowOffset);
            }
        }

        // Seventh pass: Draw expanded tag dropdowns on top of everything
        var expandedTagNodes = _stateService.GetExpandedTagNodes();
        foreach (var nodeIndex in expandedTagNodes)
        {
            if (nodeIndex >= 0 && nodeIndex < nodes.Count)
            {
                var node = nodes[nodeIndex];
                DrawExpandedTagLabels(dc, node, rowOffset);
            }
        }
    }

    private void DrawWorkingChangesRow(DrawingContext dc, Color branchColor, int laneIndex)
    {
        double y = GetYForRow(0);
        double x = GetXForColumn(laneIndex);
        double avatarRadius = NodeRadius * 1.875;

        // Determine if this row is highlighted (same as regular commits)
        bool isHighlighted = IsWorkingChangesSelected || IsWorkingChangesHovered;

        // Draw trail using branch color (same style as regular commits)
        double trailHeight = NodeRadius * 3.75 + 4;
        double trailOpacity = isHighlighted ? 0.5 : 0.15;
        var trailColor = Color.FromArgb((byte)(branchColor.A * trailOpacity),
            branchColor.R, branchColor.G, branchColor.B);
        var trailBrush = new SolidColorBrush(trailColor);
        trailBrush.Freeze();

        var accentBrush = new SolidColorBrush(branchColor);
        accentBrush.Freeze();

        // Create clipped trail geometry (rectangle with circle cut out)
        var trailRect = new Rect(x, y - trailHeight / 2, ActualWidth - x - 2, trailHeight);
        var trailGeometry = new RectangleGeometry(trailRect);
        var circleGeometry = new EllipseGeometry(new Point(x, y), avatarRadius, avatarRadius);
        var clippedTrail = new CombinedGeometry(GeometryCombineMode.Exclude, trailGeometry, circleGeometry);
        clippedTrail.Freeze();

        // Draw clipped trail
        dc.DrawGeometry(trailBrush, null, clippedTrail);

        // Draw accent at edge
        var accentRect = new Rect(ActualWidth - 2, y - trailHeight / 2, 2, trailHeight);
        dc.DrawRectangle(accentBrush, null, accentRect);

        // Draw dashed circle outline
        var dashedPen = new Pen(accentBrush, 2.5)
        {
            DashStyle = new DashStyle(new double[] { 1.4, 1.4 }, 0)
        };
        dashedPen.Freeze();
        dc.DrawEllipse(Brushes.Transparent, dashedPen, new Point(x, y), avatarRadius, avatarRadius);
    }

    private void DrawTrail(DrawingContext dc, GitTreeNode node, int rowOffset = 0)
    {
        double x = GetXForColumn(node.ColumnIndex);
        double y = GetYForRow(node.RowIndex + rowOffset);

        // Get the node's color
        var baseBrush = node.NodeColor as SolidColorBrush ?? Brushes.Gray;
        var baseColor = baseBrush.Color;

        // Trail dimensions - match commit bubble height (diameter = 2 * NodeRadius * 1.875) + 4px for border
        double trailHeight = NodeRadius * 3.75 + 4;
        double trailStartX = x;
        double trailEndX = ActualWidth;
        double accentWidth = 2;

        // Determine if this row is highlighted (selected, hovered, or search match)
        bool isHighlighted = node.Sha == SelectedSha || node.Sha == HoveredSha || (IsSearchActive && node.IsSearchMatch);

        // Trail opacity: 15% normal, 50% when highlighted
        double trailOpacity = isHighlighted ? 0.5 : 0.15;
        var trailColor = Color.FromArgb((byte)(baseColor.A * trailOpacity), baseColor.R, baseColor.G, baseColor.B);
        var trailBrush = new SolidColorBrush(trailColor);
        trailBrush.Freeze();

        // Full opacity brush for the accent (always 100%)
        var accentBrush = new SolidColorBrush(baseColor);
        accentBrush.Freeze();

        // Create clipped trail geometry (rectangle with node shape cut out)
        var trailRect = new Rect(
            trailStartX,
            y - trailHeight / 2,
            trailEndX - trailStartX - accentWidth,
            trailHeight);
        var trailGeometry = new RectangleGeometry(trailRect);

        Geometry nodeGeometry;
        if (node.IsStash)
        {
            // Stash nodes use a rounded rectangle
            double boxSize = NodeRadius * 1.875;
            var boxRect = new Rect(x - boxSize, y - boxSize, boxSize * 2, boxSize * 2);
            nodeGeometry = new RectangleGeometry(boxRect, 3, 3);
        }
        else
        {
            // Node radius depends on commit type (merge = smaller dot, regular = avatar circle)
            double avatarRadius = node.IsMerge ? NodeRadius * 0.875 : NodeRadius * 1.875;
            nodeGeometry = new EllipseGeometry(new Point(x, y), avatarRadius, avatarRadius);
        }

        var clippedTrail = new CombinedGeometry(GeometryCombineMode.Exclude, trailGeometry, nodeGeometry);
        clippedTrail.Freeze();

        // Draw clipped trail
        dc.DrawGeometry(trailBrush, null, clippedTrail);

        // Draw the accent rectangle at the end (100% opacity)
        var accentRect = new Rect(
            trailEndX - accentWidth,
            y - trailHeight / 2,
            accentWidth,
            trailHeight);
        dc.DrawRectangle(accentBrush, null, accentRect);
    }

    private void DrawNode(DrawingContext dc, GitTreeNode node, int rowOffset = 0)
    {
        double x = GetXForColumn(node.ColumnIndex);
        double y = GetYForRow(node.RowIndex + rowOffset);

        var brush = node.NodeColor ?? Brushes.Gray;

        if (node.IsStash)
        {
            // Stash nodes: rounded rectangle with stash icon (same style as old DrawStashRow)
            double boxSize = NodeRadius * 1.875;
            var boxPen = new Pen(brush, 2.5);
            boxPen.Freeze();
            var boxRect = new Rect(x - boxSize, y - boxSize, boxSize * 2, boxSize * 2);
            dc.DrawRoundedRectangle(Brushes.White, boxPen, boxRect, 3, 3);

            // Draw stash icon inside the box
            var iconFormatted = new FormattedText(
                StashIcon,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                IconTypeface,
                13,
                brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(iconFormatted, new Point(x - iconFormatted.Width / 2, y - iconFormatted.Height / 2));
        }
        else if (node.IsMerge)
        {
            // Merge commits: simple dot (0.875x = 0.7 * 1.25)
            double mergeRadius = NodeRadius * 0.875;
            dc.DrawEllipse(brush, null, new Point(x, y), mergeRadius, mergeRadius);
        }
        else
        {
            // Regular commits: circle with identicon fill
            double avatarRadius = NodeRadius * 1.875;
            var outerPen = new Pen(brush, 2.5);
            outerPen.Freeze();
            var backgroundColor = IdenticonGenerator.GetDefaultBackgroundColor();
            var backgroundBrush = Application.Current?.TryFindResource("SolidBackgroundFillColorBaseBrush") as SolidColorBrush
                ?? (backgroundColor.HasValue ? new SolidColorBrush(backgroundColor.Value) : Brushes.White);
            if (!backgroundBrush.IsFrozen) backgroundBrush.Freeze();
            dc.DrawEllipse(backgroundBrush, outerPen, new Point(x, y), avatarRadius, avatarRadius);

            var key = string.IsNullOrWhiteSpace(node.IdenticonKey) ? node.Sha : node.IdenticonKey;
            int iconSize = (int)Math.Round(avatarRadius * 2);
            if (iconSize < 10)
            {
                iconSize = 10;
            }

            var fillBrush = IdenticonGenerator.GetIdenticonBrush(key, iconSize, backgroundColor);
            dc.DrawEllipse(fillBrush, null, new Point(x, y), avatarRadius - 1, avatarRadius - 1);
        }
    }

    private void DrawConnections(DrawingContext dc, GitTreeNode node, IReadOnlyDictionary<string, GitTreeNode> nodesBySha, int rowOffset = 0)
    {
        double nodeX = GetXForColumn(node.ColumnIndex);
        double nodeY = GetYForRow(node.RowIndex + rowOffset);

        for (int i = 0; i < node.ParentShas.Count; i++)
        {
            var parentSha = node.ParentShas[i];
            if (!nodesBySha.TryGetValue(parentSha, out var parentNode))
                continue;

            double parentX = GetXForColumn(parentNode.ColumnIndex);
            double parentY = GetYForRow(parentNode.RowIndex + rowOffset);

            // First parent (i=0): commit-to-commit style (down then horizontal)
            // Second+ parent (i>0): merge style (horizontal then down)
            bool isMergeConnection = i > 0;

            // Rail color:
            // - Commit connections travel down in child's lane, so use child's color
            // - Merge connections travel into parent's lane, so use parent's color
            var lineBrush = isMergeConnection
                ? (parentNode.NodeColor ?? Brushes.Gray)
                : (node.NodeColor ?? Brushes.Gray);
            var linePen = _cacheService.GetConnectionPen(lineBrush);

            // Build clip geometry for node shape (stash = rounded rect, others = ellipse)
            var fullArea = _cacheService.GetFullArea(ActualWidth, ActualHeight);

            Geometry nodeShape;
            if (node.IsStash)
            {
                double boxSize = NodeRadius * 1.875;
                nodeShape = new RectangleGeometry(new Rect(nodeX - boxSize, nodeY - boxSize, boxSize * 2, boxSize * 2), 3, 3);
            }
            else
            {
                double nodeRadius = node.IsMerge ? NodeRadius * 0.875 : NodeRadius * 1.875;
                nodeShape = new EllipseGeometry(new Point(nodeX, nodeY), nodeRadius, nodeRadius);
            }

            Geometry parentShape;
            if (parentNode.IsStash)
            {
                double boxSize = NodeRadius * 1.875;
                parentShape = new RectangleGeometry(new Rect(parentX - boxSize, parentY - boxSize, boxSize * 2, boxSize * 2), 3, 3);
            }
            else
            {
                double parentRadius = parentNode.IsMerge ? NodeRadius * 0.875 : NodeRadius * 1.875;
                parentShape = new EllipseGeometry(new Point(parentX, parentY), parentRadius, parentRadius);
            }

            // Exclude node shape first, then parent shape
            var clipWithoutNode = new CombinedGeometry(GeometryCombineMode.Exclude, fullArea, nodeShape);
            var clipGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, clipWithoutNode, parentShape);
            clipGeometry.Freeze();

            dc.PushClip(clipGeometry);
            DrawRailConnection(dc, linePen, nodeX, nodeY, parentX, parentY, isMergeConnection);
            dc.Pop();
        }
    }

    private void DrawPassThroughLanes(DrawingContext dc, int rowOffset, int minNodeIndex,
        double viewportTop, double viewportBottom)
    {
        if (_laneSegments.Count == 0)
            return;

        // Binary search: find the last segment index where ChildRow < minNodeIndex
        // Segments are sorted by ChildRow ascending, so all qualifying segments are at the front
        int lo = 0, hi = _laneSegments.Count - 1, cutoff = 0;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (_laneSegments[mid].ChildRow < minNodeIndex)
            {
                cutoff = mid + 1;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        for (int i = 0; i < cutoff; i++)
        {
            var seg = _laneSegments[i];

            double segTopY = GetYForRow(seg.ChildRow + rowOffset);
            double segBottomY = GetYForRow(seg.ParentRow + rowOffset);

            // Skip if no viewport overlap
            if (segBottomY < viewportTop || segTopY > viewportBottom)
                continue;

            // Draw vertical line clamped to viewport
            double x = GetXForColumn(seg.Column);
            double drawTop = Math.Max(segTopY, viewportTop);
            double drawBottom = Math.Min(segBottomY, viewportBottom);

            var pen = _cacheService.GetConnectionPen(seg.Color);
            dc.DrawLine(pen, new Point(x, drawTop), new Point(x, drawBottom));
        }
    }

    private void DrawRailConnection(DrawingContext dc, Pen pen, double x1, double y1, double x2, double y2, bool isMergeConnection = false)
    {
        double cornerRadius = Math.Min(LaneWidth * 0.4, RowHeight * 0.4);
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x1, y1), false, false);

            if (Math.Abs(x1 - x2) < 1)
            {
                // Same column - straight vertical line
                ctx.LineTo(new Point(x2, y2), true, false);
            }
            else
            {
                bool goingRight = x2 > x1;

                // Safety check for corner radius
                if (cornerRadius > Math.Abs(y2 - y1))
                {
                    cornerRadius = Math.Abs(y2 - y1) / 2;
                }

                if (isMergeConnection)
                {
                    // MERGE: horizontal first, then down
                    ctx.LineTo(new Point(goingRight ? x2 - cornerRadius : x2 + cornerRadius, y1), true, false);
                    ctx.ArcTo(
                        new Point(x2, y1 + cornerRadius),
                        new Size(cornerRadius, cornerRadius),
                        0, false,
                        goingRight ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                        true, false);
                    ctx.LineTo(new Point(x2, y2), true, false);
                }
                else
                {
                    // COMMIT: down first, then horizontal
                    ctx.LineTo(new Point(x1, y2 - cornerRadius), true, false);
                    ctx.ArcTo(
                        new Point(goingRight ? x1 + cornerRadius : x1 - cornerRadius, y2),
                        new Size(cornerRadius, cornerRadius),
                        0, false,
                        goingRight ? SweepDirection.Counterclockwise : SweepDirection.Clockwise,
                        true, false);
                    ctx.LineTo(new Point(x2, y2), true, false);
                }
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}
