using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Leaf.Graph;
using Leaf.Models;
using Leaf.Utils;

namespace Leaf.Controls;

/// <summary>
/// Custom WPF control for rendering the Git commit graph.
/// Uses DrawingVisual for efficient rendering with render culling.
/// </summary>
public class GitGraphCanvas : FrameworkElement
{
    #region Dependency Properties

    public static readonly DependencyProperty NodesProperty =
        DependencyProperty.Register(
            nameof(Nodes),
            typeof(IReadOnlyList<GitTreeNode>),
            typeof(GitGraphCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RowHeightProperty =
        DependencyProperty.Register(
            nameof(RowHeight),
            typeof(double),
            typeof(GitGraphCanvas),
            new FrameworkPropertyMetadata(28.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty LaneWidthProperty =
        DependencyProperty.Register(
            nameof(LaneWidth),
            typeof(double),
            typeof(GitGraphCanvas),
            new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty NodeRadiusProperty =
        DependencyProperty.Register(
            nameof(NodeRadius),
            typeof(double),
            typeof(GitGraphCanvas),
            new FrameworkPropertyMetadata(5.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedShaProperty =
        DependencyProperty.Register(
            nameof(SelectedSha),
            typeof(string),
            typeof(GitGraphCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxLaneProperty =
        DependencyProperty.Register(
            nameof(MaxLane),
            typeof(int),
            typeof(GitGraphCanvas),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty LabelAreaWidthProperty =
        DependencyProperty.Register(
            nameof(LabelAreaWidth),
            typeof(double),
            typeof(GitGraphCanvas),
            new FrameworkPropertyMetadata(150.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty HoveredShaProperty =
        DependencyProperty.Register(
            nameof(HoveredSha),
            typeof(string),
            typeof(GitGraphCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsSearchActiveProperty =
        DependencyProperty.Register(
            nameof(IsSearchActive),
            typeof(bool),
            typeof(GitGraphCanvas),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HasWorkingChangesProperty =
        DependencyProperty.Register(
            nameof(HasWorkingChanges),
            typeof(bool),
            typeof(GitGraphCanvas),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty IsWorkingChangesSelectedProperty =
        DependencyProperty.Register(
            nameof(IsWorkingChangesSelected),
            typeof(bool),
            typeof(GitGraphCanvas),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsWorkingChangesHoveredProperty =
        DependencyProperty.Register(
            nameof(IsWorkingChangesHovered),
            typeof(bool),
            typeof(GitGraphCanvas),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentBranchNameProperty =
        DependencyProperty.Register(
            nameof(CurrentBranchName),
            typeof(string),
            typeof(GitGraphCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StashCountProperty =
        DependencyProperty.Register(
            nameof(StashCount),
            typeof(int),
            typeof(GitGraphCanvas),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty HoveredStashIndexProperty =
        DependencyProperty.Register(
            nameof(HoveredStashIndex),
            typeof(int),
            typeof(GitGraphCanvas),
            new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedStashIndexProperty =
        DependencyProperty.Register(
            nameof(SelectedStashIndex),
            typeof(int),
            typeof(GitGraphCanvas),
            new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StashesProperty =
        DependencyProperty.Register(
            nameof(Stashes),
            typeof(IReadOnlyList<StashInfo>),
            typeof(GitGraphCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    #endregion

    #region Properties

    public IReadOnlyList<GitTreeNode>? Nodes
    {
        get => (IReadOnlyList<GitTreeNode>?)GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    public double RowHeight
    {
        get => (double)GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    public double LaneWidth
    {
        get => (double)GetValue(LaneWidthProperty);
        set => SetValue(LaneWidthProperty, value);
    }

    public double NodeRadius
    {
        get => (double)GetValue(NodeRadiusProperty);
        set => SetValue(NodeRadiusProperty, value);
    }

    public string? SelectedSha
    {
        get => (string?)GetValue(SelectedShaProperty);
        set => SetValue(SelectedShaProperty, value);
    }

    public int MaxLane
    {
        get => (int)GetValue(MaxLaneProperty);
        set => SetValue(MaxLaneProperty, value);
    }

    public double LabelAreaWidth
    {
        get => (double)GetValue(LabelAreaWidthProperty);
        set => SetValue(LabelAreaWidthProperty, value);
    }

    public string? HoveredSha
    {
        get => (string?)GetValue(HoveredShaProperty);
        set => SetValue(HoveredShaProperty, value);
    }

    public bool IsSearchActive
    {
        get => (bool)GetValue(IsSearchActiveProperty);
        set => SetValue(IsSearchActiveProperty, value);
    }

    public bool HasWorkingChanges
    {
        get => (bool)GetValue(HasWorkingChangesProperty);
        set => SetValue(HasWorkingChangesProperty, value);
    }

    public bool IsWorkingChangesSelected
    {
        get => (bool)GetValue(IsWorkingChangesSelectedProperty);
        set => SetValue(IsWorkingChangesSelectedProperty, value);
    }

    public bool IsWorkingChangesHovered
    {
        get => (bool)GetValue(IsWorkingChangesHoveredProperty);
        set => SetValue(IsWorkingChangesHoveredProperty, value);
    }

    public string? CurrentBranchName
    {
        get => (string?)GetValue(CurrentBranchNameProperty);
        set => SetValue(CurrentBranchNameProperty, value);
    }

    public int StashCount
    {
        get => (int)GetValue(StashCountProperty);
        set => SetValue(StashCountProperty, value);
    }

    public int HoveredStashIndex
    {
        get => (int)GetValue(HoveredStashIndexProperty);
        set => SetValue(HoveredStashIndexProperty, value);
    }

    public int SelectedStashIndex
    {
        get => (int)GetValue(SelectedStashIndexProperty);
        set => SetValue(SelectedStashIndexProperty, value);
    }

    public IReadOnlyList<StashInfo>? Stashes
    {
        get => (IReadOnlyList<StashInfo>?)GetValue(StashesProperty);
        set => SetValue(StashesProperty, value);
    }

    #endregion

    #region Rendering Constants

    private static readonly Brush LabelTextBrush = Brushes.White;
    private static readonly Pen LabelBorderPen = new Pen(Brushes.Transparent, 0);

    // Icons from Segoe Fluent Icons
    private const string ComputerIcon = "\uE7F4"; // Computer/Desktop
    private const string CloudIcon = "\uE753"; // Cloud
    private const string EditIcon = "\uE70F"; // Edit/Pencil for working changes
    private const string StashIcon = "\uE7B8"; // Box/Package icon for stashes

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

    static GitGraphCanvas()
    {
        LabelBorderPen.Freeze();
    }

    public GitGraphCanvas()
    {
        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        var nodes = Nodes;

        // Calculate which row the mouse is over
        int row = (int)(pos.Y / RowHeight);
        int currentRow = 0;

        // Handle working changes row (row 0 when HasWorkingChanges)
        if (HasWorkingChanges)
        {
            if (row == currentRow)
            {
                // Hovering over working changes row
                IsWorkingChangesHovered = true;
                HoveredSha = null;
                HoveredStashIndex = -1;
                return;
            }
            currentRow++;
            IsWorkingChangesHovered = false;
        }
        else
        {
            IsWorkingChangesHovered = false;
        }

        // Handle stash rows
        if (StashCount > 0)
        {
            int stashIndex = row - currentRow;
            if (stashIndex >= 0 && stashIndex < StashCount)
            {
                // Hovering over a stash row
                HoveredStashIndex = stashIndex;
                HoveredSha = null;
                return;
            }
            HoveredStashIndex = -1;
            currentRow += StashCount;
        }
        else
        {
            HoveredStashIndex = -1;
        }

        if (nodes == null || nodes.Count == 0)
        {
            HoveredSha = null;
            return;
        }

        int nodeIndex = row - currentRow;
        if (nodeIndex >= 0 && nodeIndex < nodes.Count)
        {
            HoveredSha = nodes[nodeIndex].Sha;
        }
        else
        {
            HoveredSha = null;
        }
    }

    private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        HoveredSha = null;
        IsWorkingChangesHovered = false;
        HoveredStashIndex = -1;
    }

    #endregion

    protected override Size MeasureOverride(Size availableSize)
    {
        var nodes = Nodes;
        if (nodes == null || nodes.Count == 0)
        {
            // Even with no nodes, if we have working changes or stashes, show those rows
            int emptyRowCount = (HasWorkingChanges ? 1 : 0) + StashCount;
            if (emptyRowCount > 0)
            {
                // Extra lane for stashes if present
                int stashLaneCount = StashCount > 0 ? 1 : 0;
                double emptyWidth = LabelAreaWidth + (2 + stashLaneCount) * LaneWidth;
                return new Size(emptyWidth, emptyRowCount * RowHeight);
            }
            return new Size(0, 0);
        }

        // Width: label area + (MaxLane + 2) lanes * LaneWidth + stash lane if present
        // Height: node count * RowHeight (+ 1 for working changes if present, + stash count)
        int stashLane = StashCount > 0 ? 1 : 0;
        double width = LabelAreaWidth + (MaxLane + 2 + stashLane) * LaneWidth;
        int rowCount = nodes.Count;
        if (HasWorkingChanges)
        {
            rowCount += 1;
        }
        rowCount += StashCount;
        double height = rowCount * RowHeight;

        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        // Row offset: working changes (0 or 1) + stash count
        int workingChangesOffset = HasWorkingChanges ? 1 : 0;
        int rowOffset = workingChangesOffset + StashCount;

        var nodes = Nodes;

        // Draw working changes row first (at row 0) if present
        if (HasWorkingChanges)
        {
            // Get the branch color for WIP node
            var branchName = CurrentBranchName ?? "main";
            var branchBrush = GraphBuilder.GetBranchColor(branchName) as SolidColorBrush ?? Brushes.Gray;
            var branchColor = branchBrush.Color;

            var headNode = nodes?.FirstOrDefault(n => n.IsHead);
            var targetNode = headNode ?? (nodes != null && nodes.Count > 0 ? nodes[0] : null);
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
                double targetRadius = targetNode.IsMerge ? NodeRadius * 0.875 : NodeRadius * 1.875;

                // Create clip geometry that excludes both the WIP circle and the target commit's circle
                var fullArea = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
                var wipCircle = new EllipseGeometry(new Point(wipX, wipY), avatarRadius, avatarRadius);
                var targetCircle = new EllipseGeometry(new Point(targetX, targetY), targetRadius, targetRadius);

                var clipWithoutWip = new CombinedGeometry(GeometryCombineMode.Exclude, fullArea, wipCircle);
                var clipGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, clipWithoutWip, targetCircle);
                clipGeometry.Freeze();

                dc.PushClip(clipGeometry);
                var linePen = new Pen(branchBrush, 2);
                linePen.Freeze();
                DrawRailConnection(dc, linePen, wipX, wipY, targetX, targetY, false);
                dc.Pop();
            }

            // Draw WIP node on top of connection line
            DrawWorkingChangesRow(dc, branchColor, wipLane);
        }

        // Draw stash rows
        for (int i = 0; i < StashCount; i++)
        {
            int stashRow = workingChangesOffset + i;
            bool isHovered = HoveredStashIndex == i;
            bool isSelected = SelectedStashIndex == i;
            DrawStashRow(dc, stashRow, i, isHovered, isSelected, nodes, rowOffset);
        }

        if (nodes == null || nodes.Count == 0)
            return;

        // Draw all nodes (render culling was causing issues with commits not appearing)
        int minNodeIndex = 0;
        int maxNodeIndex = nodes.Count - 1;

        // Create a lookup for efficient parent finding
        var nodesBySha = nodes.ToDictionary(n => n.Sha);

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
            if (node.BranchLabels.Count > 0)
            {
                DrawBranchLabels(dc, node, rowOffset);
            }
        }

        // Fifth pass: Draw ghost tags for hovered and selected commits
        // (only if they don't already have branch labels)
        var selectedNode = SelectedSha != null ? nodesBySha.GetValueOrDefault(SelectedSha) : null;
        var hoveredNode = HoveredSha != null ? nodesBySha.GetValueOrDefault(HoveredSha) : null;

        // Draw selected ghost tag first (so hovered draws on top if same row)
        if (selectedNode != null && selectedNode.BranchLabels.Count == 0 &&
            selectedNode.RowIndex >= minNodeIndex && selectedNode.RowIndex <= maxNodeIndex)
        {
            DrawGhostTag(dc, selectedNode, rowOffset);
        }

        // Draw hovered ghost tag (even if same as selected, it will just overlap)
        if (hoveredNode != null && hoveredNode.BranchLabels.Count == 0 &&
            hoveredNode.RowIndex >= minNodeIndex && hoveredNode.RowIndex <= maxNodeIndex &&
            hoveredNode.Sha != SelectedSha) // Don't draw twice if same commit
        {
            DrawGhostTag(dc, hoveredNode, rowOffset);
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

    private void DrawStashRow(DrawingContext dc, int row, int stashIndex, bool isHovered, bool isSelected, IReadOnlyList<GitTreeNode>? nodes, int rowOffset)
    {
        double y = GetYForRow(row);
        // Stashes in separate rightmost lane (MaxLane + 1)
        int stashLane = MaxLane + 1;
        double x = GetXForColumn(stashLane);

        // Get stash info and use branch color instead of hardcoded purple
        var stashInfo = Stashes != null && stashIndex < Stashes.Count ? Stashes[stashIndex] : null;
        var branchName = stashInfo?.BranchName ?? CurrentBranchName ?? "main";
        var branchBrush = GraphBuilder.GetBranchColor(branchName) as SolidColorBrush ?? Brushes.Gray;
        var stashColor = branchBrush.Color;
        var stashBrush = new SolidColorBrush(stashColor);
        stashBrush.Freeze();

        // Determine if this row is highlighted
        bool isHighlighted = isSelected || isHovered;

        // Draw trail using stash color
        double trailHeight = NodeRadius * 3.75 + 4;
        double trailOpacity = isHighlighted ? 0.5 : 0.15;
        var trailColor = Color.FromArgb((byte)(stashColor.A * trailOpacity),
            stashColor.R, stashColor.G, stashColor.B);
        var trailBrush = new SolidColorBrush(trailColor);
        trailBrush.Freeze();

        // Draw the main trail rectangle
        double boxSize = NodeRadius * 1.875;
        var trailRect = new Rect(x, y - trailHeight / 2, ActualWidth - x - 2, trailHeight);
        dc.DrawRectangle(trailBrush, null, trailRect);

        // Draw accent at edge
        var accentRect = new Rect(ActualWidth - 2, y - trailHeight / 2, 2, trailHeight);
        dc.DrawRectangle(stashBrush, null, accentRect);

        // Stashes are isolated - no connections to commits or other stashes

        // Draw stash box (rounded rectangle instead of circle)
        var boxPen = new Pen(stashBrush, 2.5);
        boxPen.Freeze();
        var boxRect = new Rect(x - boxSize, y - boxSize, boxSize * 2, boxSize * 2);
        dc.DrawRoundedRectangle(Brushes.White, boxPen, boxRect, 3, 3);

        // Draw stash icon inside the box
        var iconFormatted = new FormattedText(
            StashIcon,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            IconTypeface,
            13,
            stashBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(iconFormatted, new Point(x - iconFormatted.Width / 2, y - iconFormatted.Height / 2));
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
        double trailStartX = x; // Start at center (node drawn on top will clip)
        double trailEndX = ActualWidth;
        double accentWidth = 2;

        // Node radius depends on commit type (merge = smaller dot, regular = avatar circle)
        double avatarRadius = node.IsMerge ? NodeRadius * 0.875 : NodeRadius * 1.875;

        // Determine if this row is highlighted (selected or search match)
        bool isHighlighted = node.Sha == SelectedSha || (IsSearchActive && node.IsSearchMatch);

        // Trail opacity: 15% normal, 50% when highlighted
        double trailOpacity = isHighlighted ? 0.5 : 0.15;
        var trailColor = Color.FromArgb((byte)(baseColor.A * trailOpacity), baseColor.R, baseColor.G, baseColor.B);
        var trailBrush = new SolidColorBrush(trailColor);
        trailBrush.Freeze();

        // Full opacity brush for the accent (always 100%)
        var accentBrush = new SolidColorBrush(baseColor);
        accentBrush.Freeze();

        // Create clipped trail geometry (rectangle with circle cut out)
        var trailRect = new Rect(
            trailStartX,
            y - trailHeight / 2,
            trailEndX - trailStartX - accentWidth,
            trailHeight);
        var trailGeometry = new RectangleGeometry(trailRect);
        var circleGeometry = new EllipseGeometry(new Point(x, y), avatarRadius, avatarRadius);
        var clippedTrail = new CombinedGeometry(GeometryCombineMode.Exclude, trailGeometry, circleGeometry);
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

        if (node.IsMerge)
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
                var backgroundBrush = backgroundColor.HasValue
                    ? new SolidColorBrush(backgroundColor.Value)
                    : Brushes.Transparent;
                backgroundBrush.Freeze();
                dc.DrawEllipse(backgroundBrush, outerPen, new Point(x, y), avatarRadius, avatarRadius);

                var key = string.IsNullOrWhiteSpace(node.IdenticonKey) ? node.Sha : node.IdenticonKey;
                int iconSize = (int)Math.Round(avatarRadius * 2);
                if (iconSize < 10)
                {
                    iconSize = 10;
                }

                var identicon = IdenticonGenerator.GetIdenticon(key, iconSize, backgroundColor);
                var fillBrush = new ImageBrush(identicon)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
                fillBrush.Freeze();
                dc.DrawEllipse(fillBrush, null, new Point(x, y), avatarRadius - 1, avatarRadius - 1);
            }

        // HEAD indicator removed - now shown via enlarged branch tag instead
    }

    private void DrawConnections(DrawingContext dc, GitTreeNode node, Dictionary<string, GitTreeNode> nodesBySha, int rowOffset = 0)
    {
        double nodeX = GetXForColumn(node.ColumnIndex);
        double nodeY = GetYForRow(node.RowIndex + rowOffset);

        // Node radius for clipping (merge = smaller, regular = avatar)
        double nodeRadius = node.IsMerge ? NodeRadius * 0.875 : NodeRadius * 1.875;

        for (int i = 0; i < node.ParentShas.Count; i++)
        {
            var parentSha = node.ParentShas[i];
            if (!nodesBySha.TryGetValue(parentSha, out var parentNode))
                continue;

            double parentX = GetXForColumn(parentNode.ColumnIndex);
            double parentY = GetYForRow(parentNode.RowIndex + rowOffset);
            double parentRadius = parentNode.IsMerge ? NodeRadius * 0.875 : NodeRadius * 1.875;

            // First parent (i=0): commit-to-commit style (down then horizontal)
            // Second+ parent (i>0): merge style (horizontal then down)
            bool isMergeConnection = i > 0;

            // Rail color:
            // - Commit connections travel down in child's lane, so use child's color
            // - Merge connections travel into parent's lane, so use parent's color
            var lineBrush = isMergeConnection
                ? (parentNode.NodeColor ?? Brushes.Gray)
                : (node.NodeColor ?? Brushes.Gray);
            var linePen = new Pen(lineBrush, 2);
            linePen.Freeze();

            // Create clip geometry that excludes both node circles
            var fullArea = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
            var nodeCircle = new EllipseGeometry(new Point(nodeX, nodeY), nodeRadius, nodeRadius);
            var parentCircle = new EllipseGeometry(new Point(parentX, parentY), parentRadius, parentRadius);

            // Exclude node circle first, then parent circle
            var clipWithoutNode = new CombinedGeometry(GeometryCombineMode.Exclude, fullArea, nodeCircle);
            var clipGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, clipWithoutNode, parentCircle);
            clipGeometry.Freeze();

            dc.PushClip(clipGeometry);
            DrawRailConnection(dc, linePen, nodeX, nodeY, parentX, parentY, isMergeConnection);
            dc.Pop();
        }
    }

    private void DrawRailConnection(DrawingContext dc, Pen pen, double x1, double y1, double x2, double y2, bool isMergeConnection = false)
    {
        // Rail-style connection:
        // Child at (x1, y1) - top, Parent at (x2, y2) - bottom
        //
        // COMMIT style (first parent): down then horizontal
        //     O (child)
        //     │
        // O───┘ (parent)
        //
        // MERGE style (second+ parent): horizontal then down
        // O───────┐ (child)
        //         │
        //         O (parent)

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
                    // 1. Horizontal from child toward parent's lane
                    ctx.LineTo(
                        new Point(goingRight ? x2 - cornerRadius : x2 + cornerRadius, y1),
                        true, false);

                    // 2. Curve down
                    ctx.ArcTo(
                        new Point(x2, y1 + cornerRadius),
                        new Size(cornerRadius, cornerRadius),
                        0, false,
                        goingRight ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                        true, false);

                    // 3. Vertical down to parent
                    ctx.LineTo(new Point(x2, y2), true, false);
                }
                else
                {
                    // COMMIT: down first, then horizontal
                    // 1. Vertical down toward parent's Y
                    ctx.LineTo(new Point(x1, y2 - cornerRadius), true, false);

                    // 2. Curve horizontal
                    ctx.ArcTo(
                        new Point(goingRight ? x1 + cornerRadius : x1 - cornerRadius, y2),
                        new Size(cornerRadius, cornerRadius),
                        0, false,
                        goingRight ? SweepDirection.Counterclockwise : SweepDirection.Clockwise,
                        true, false);

                    // 3. Horizontal to parent
                    ctx.LineTo(new Point(x2, y2), true, false);
                }
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private void DrawBranchLabels(DrawingContext dc, GitTreeNode node, int rowOffset = 0)
    {
        double y = GetYForRow(node.RowIndex + rowOffset);
        double nodeX = GetXForColumn(node.ColumnIndex);
        double labelX = 4; // Start from left edge with small padding
        double lastLabelRight = 0;
        Brush? lastLabelBrush = null;
        bool lastLabelIsCurrent = false;

        foreach (var label in node.BranchLabels)
        {
            // Each branch gets a unique color generated from its name (same as lane colors)
            Brush bgBrush = GraphBuilder.GetBranchColor(label.Name);

            // Current branch gets enlarged styling
            double fontSize = label.IsCurrent ? 13 : 11;
            double iconFontSize = label.IsCurrent ? 13 : 11;
            double labelHeight = label.IsCurrent ? 22 : 18;
            double cornerRadius = label.IsCurrent ? 5 : 4;

            // Build the label text with icons (icons AFTER name now)
            var iconText = "";
            if (label.IsLocal)
                iconText += ComputerIcon;
            if (label.IsLocal && label.IsRemote)
                iconText += " "; // Space between icons when both are shown
            if (label.IsRemote)
                iconText += CloudIcon;

            // Measure icon text
            var iconFormatted = new FormattedText(
                iconText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                IconTypeface,
                iconFontSize,
                LabelTextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            // Measure branch name text
            var nameFormatted = new FormattedText(
                label.Name,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                fontSize,
                LabelTextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            // Calculate label dimensions (name first, then icons)
            double iconWidth = iconFormatted.Width;
            double nameWidth = nameFormatted.Width;
            double hPadding = label.IsCurrent ? 8 : 6;
            double totalWidth = hPadding + nameWidth + 4 + iconWidth + hPadding;

            // Check if label would overflow the label area
            if (labelX + totalWidth > LabelAreaWidth - 8)
                break; // Don't draw more labels if they won't fit

            // Draw rounded rectangle background
            var labelRect = new Rect(labelX, y - labelHeight / 2, totalWidth, labelHeight);
            dc.DrawRoundedRectangle(bgBrush, LabelBorderPen, labelRect, cornerRadius, cornerRadius);

            // Draw branch name first
            dc.DrawText(nameFormatted, new Point(labelX + hPadding, y - nameFormatted.Height / 2));

            // Draw icons after name
            dc.DrawText(iconFormatted, new Point(labelX + hPadding + nameWidth + 4, y - iconFormatted.Height / 2));

            // Track the rightmost edge of labels and the last brush color
            lastLabelRight = labelX + totalWidth;
            lastLabelBrush = bgBrush;
            lastLabelIsCurrent = label.IsCurrent;

            // Move X for next label
            labelX += totalWidth + 4;
        }

            // Draw connecting line from last label to the commit node (same color as tag)
            // Thicker line for current branch
            if (node.BranchLabels.Count > 0 && lastLabelRight > 0 && lastLabelBrush != null)
            {
                double lineThickness = lastLabelIsCurrent ? 2.5 : 1.5;
                var linePen = new Pen(lastLabelBrush, lineThickness);
                linePen.Freeze();

                // Draw horizontal line from label to node (stop before node edge)
                double lineEndX = node.IsMerge ? nodeX : nodeX - NodeRadius - 4;
                dc.DrawLine(linePen, new Point(lastLabelRight, y), new Point(lineEndX, y));
            }
    }

    private void DrawGhostTag(DrawingContext dc, GitTreeNode node, int rowOffset = 0)
    {
        // Get the branch name to display (use PrimaryBranch or fallback to short SHA)
        string labelText = node.PrimaryBranch ?? node.Sha[..7];

        double y = GetYForRow(node.RowIndex + rowOffset);
        double nodeX = GetXForColumn(node.ColumnIndex);
        double labelX = 4; // Start from left edge with small padding

        // Get branch color and make it semi-transparent
        Brush baseBrush = GraphBuilder.GetBranchColor(labelText);
        Color baseColor = ((SolidColorBrush)baseBrush).Color;
        var ghostBrush = new SolidColorBrush(Color.FromArgb(
            (byte)(baseColor.A * GhostTagOpacity),
            baseColor.R, baseColor.G, baseColor.B));
        ghostBrush.Freeze();

        // Ghost text brush (white with opacity)
        var ghostTextBrush = new SolidColorBrush(Color.FromArgb(
            (byte)(255 * GhostTagOpacity), 255, 255, 255));
        ghostTextBrush.Freeze();

        // Determine local/remote status for icons
        // Try to find matching branch label info, otherwise infer from branch name
        bool isLocal = false;
        bool isRemote = false;

        var matchingLabel = node.BranchLabels.FirstOrDefault(l =>
            l.Name.Equals(labelText, StringComparison.OrdinalIgnoreCase));
        if (matchingLabel != null)
        {
            isLocal = matchingLabel.IsLocal;
            isRemote = matchingLabel.IsRemote;
        }
        else if (node.PrimaryBranch != null)
        {
            // Infer from branch name format
            // Local branches don't have "/" prefix, remote branches have "origin/" etc.
            isLocal = !labelText.Contains('/');
            isRemote = labelText.Contains('/'); // Only show cloud if it's actually a remote ref
        }

        // Build icon text
        var iconText = "";
        if (isLocal)
            iconText += ComputerIcon;
        if (isLocal && isRemote)
            iconText += " ";
        if (isRemote)
            iconText += CloudIcon;

        // Measure icon text
        var iconFormatted = new FormattedText(
            iconText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            IconTypeface,
            11,
            ghostTextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        // Measure branch name text
        var nameFormatted = new FormattedText(
            labelText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            11,
            ghostTextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        // Calculate label dimensions (name + icons, matching regular label style)
        double iconWidth = iconFormatted.Width;
        double nameWidth = nameFormatted.Width;
        double totalWidth = iconWidth > 0
            ? 6 + nameWidth + 4 + iconWidth + 6  // padding + name + gap + icons + padding
            : 6 + nameWidth + 6;                  // padding + name + padding
        double labelHeight = 18;

        // Draw rounded rectangle background
        var labelRect = new Rect(labelX, y - labelHeight / 2, totalWidth, labelHeight);
        dc.DrawRoundedRectangle(ghostBrush, LabelBorderPen, labelRect, 4, 4);

        // Draw branch name first
        dc.DrawText(nameFormatted, new Point(labelX + 6, y - nameFormatted.Height / 2));

        // Draw icons after name (if any)
        if (iconWidth > 0)
        {
            dc.DrawText(iconFormatted, new Point(labelX + 6 + nameWidth + 4, y - iconFormatted.Height / 2));
        }

        // Draw connecting line from label to the commit node (stop before node edge)
        var linePen = new Pen(ghostBrush, 1.5);
        linePen.Freeze();
        double lineEndX = node.IsMerge ? nodeX : nodeX - NodeRadius - 4;
        dc.DrawLine(linePen, new Point(labelX + totalWidth, y), new Point(lineEndX, y));
    }

    private double GetXForColumn(int column)
    {
        // Shift graph right by label area width, then center of the lane
        return LabelAreaWidth + (column + 0.5) * LaneWidth;
    }

    private double GetYForRow(int row)
    {
        return (row + 0.5) * RowHeight;
    }

    public BranchLabel? GetBranchLabelAt(Point position)
    {
        if (Nodes == null || position.X > LabelAreaWidth)
            return null;

        int row = (int)(position.Y / RowHeight);
        int rowOffset = (HasWorkingChanges ? 1 : 0) + StashCount;
        int nodeIndex = row - rowOffset;

        if (nodeIndex < 0 || nodeIndex >= Nodes.Count)
            return null;

        var node = Nodes[nodeIndex];
        if (node.BranchLabels.Count == 0)
            return null;

        double y = GetYForRow(node.RowIndex + rowOffset);
        double labelX = 4;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        foreach (var label in node.BranchLabels)
        {
            double fontSize = label.IsCurrent ? 13 : 11;
            double iconFontSize = label.IsCurrent ? 13 : 11;
            double labelHeight = label.IsCurrent ? 22 : 18;

            var iconText = "";
            if (label.IsLocal)
                iconText += ComputerIcon;
            if (label.IsLocal && label.IsRemote)
                iconText += " ";
            if (label.IsRemote)
                iconText += CloudIcon;

            var iconFormatted = new FormattedText(
                iconText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                iconFontSize,
                LabelTextBrush,
                dpi);

            var nameFormatted = new FormattedText(
                label.Name,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                fontSize,
                LabelTextBrush,
                dpi);

            double iconWidth = iconFormatted.Width;
            double nameWidth = nameFormatted.Width;
            double hPadding = label.IsCurrent ? 8 : 6;
            double totalWidth = hPadding + nameWidth + 4 + iconWidth + hPadding;

            if (labelX + totalWidth > LabelAreaWidth - 8)
                break;

            var labelRect = new Rect(labelX, y - labelHeight / 2, totalWidth, labelHeight);
            if (labelRect.Contains(position))
                return label;

            labelX += totalWidth + 4;
        }

        return null;
    }
}
