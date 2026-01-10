using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Leaf.Graph;
using Leaf.Models;

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

    #endregion

    #region Rendering Constants

    private static readonly Brush LabelTextBrush = Brushes.White;
    private static readonly Pen LabelBorderPen = new Pen(Brushes.Transparent, 0);

    // Working changes amber color
    private static readonly Color WorkingChangesColor = Color.FromRgb(0xFF, 0xB9, 0x00); // #FFB900
    private static readonly Brush WorkingChangesBrush = new SolidColorBrush(WorkingChangesColor);

    // Icons from Segoe Fluent Icons
    private const string ComputerIcon = "\uE7F4"; // Computer/Desktop
    private const string CloudIcon = "\uE753"; // Cloud
    private const string EditIcon = "\uE70F"; // Edit/Pencil for working changes

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
        WorkingChangesBrush.Freeze();
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

        // Handle working changes row (row 0 when HasWorkingChanges)
        if (HasWorkingChanges)
        {
            if (row == 0)
            {
                // Hovering over working changes row
                IsWorkingChangesHovered = true;
                HoveredSha = null;
                return;
            }
            else
            {
                IsWorkingChangesHovered = false;
                // Adjust for offset
                row -= 1;
            }
        }
        else
        {
            IsWorkingChangesHovered = false;
        }

        if (nodes == null || nodes.Count == 0)
        {
            HoveredSha = null;
            return;
        }

        if (row >= 0 && row < nodes.Count)
        {
            HoveredSha = nodes[row].Sha;
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
    }

    #endregion

    protected override Size MeasureOverride(Size availableSize)
    {
        var nodes = Nodes;
        if (nodes == null || nodes.Count == 0)
        {
            // Even with no nodes, if we have working changes, show that row
            if (HasWorkingChanges)
            {
                double emptyWidth = LabelAreaWidth + 2 * LaneWidth;
                return new Size(emptyWidth, RowHeight);
            }
            return new Size(0, 0);
        }

        // Width: label area + (MaxLane + 2) lanes * LaneWidth
        // Height: node count * RowHeight (+ 1 for working changes if present)
        double width = LabelAreaWidth + (MaxLane + 2) * LaneWidth;
        int rowCount = nodes.Count;
        if (HasWorkingChanges)
        {
            rowCount += 1;
        }
        double height = rowCount * RowHeight;

        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        // Row offset: 1 if working changes are shown, 0 otherwise
        int rowOffset = HasWorkingChanges ? 1 : 0;

        // Draw working changes row first (at row 0) if present
        if (HasWorkingChanges)
        {
            DrawWorkingChangesRow(dc);
        }

        var nodes = Nodes;
        if (nodes == null || nodes.Count == 0)
            return;

        // Render culling: only draw visible rows
        var clip = VisualTreeHelper.GetContentBounds(this);
        if (clip.IsEmpty)
        {
            clip = new Rect(0, 0, ActualWidth, ActualHeight);
        }

        // Calculate visible row range (adjusted for offset)
        int minVisibleRow = Math.Max(0, (int)(clip.Top / RowHeight) - 1);
        int maxVisibleRow = (int)(clip.Bottom / RowHeight) + 1;

        // Adjust for node indices (which start at 0, but are rendered at row offset)
        int minNodeIndex = Math.Max(0, minVisibleRow - rowOffset);
        int maxNodeIndex = Math.Min(nodes.Count - 1, maxVisibleRow - rowOffset);

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

    private void DrawWorkingChangesRow(DrawingContext dc)
    {
        double y = GetYForRow(0);
        double x = GetXForColumn(0); // Always in lane 0

        // Determine if this row is highlighted
        bool isHighlighted = IsWorkingChangesSelected || IsWorkingChangesHovered;

        // Draw trail (amber colored)
        double trailHeight = NodeRadius * 3.75 + 4;
        double trailOpacity = isHighlighted ? 0.5 : 0.15;
        var trailColor = Color.FromArgb((byte)(WorkingChangesColor.A * trailOpacity),
            WorkingChangesColor.R, WorkingChangesColor.G, WorkingChangesColor.B);
        var trailBrush = new SolidColorBrush(trailColor);
        trailBrush.Freeze();

        var accentBrush = new SolidColorBrush(WorkingChangesColor);
        accentBrush.Freeze();

        // Draw trail
        var trailRect = new Rect(x, y - trailHeight / 2, ActualWidth - x - 2, trailHeight);
        dc.DrawRectangle(trailBrush, null, trailRect);

        // Draw accent at edge
        var accentRect = new Rect(ActualWidth - 2, y - trailHeight / 2, 2, trailHeight);
        dc.DrawRectangle(accentBrush, null, accentRect);

        // Draw dotted circle node
        double avatarRadius = NodeRadius * 1.875;
        var dashedPen = new Pen(WorkingChangesBrush, 2.5)
        {
            DashStyle = new DashStyle(new double[] { 2, 2 }, 0)
        };
        dashedPen.Freeze();
        dc.DrawEllipse(Brushes.White, dashedPen, new Point(x, y), avatarRadius, avatarRadius);

        // Draw edit icon inside
        var iconFormatted = new FormattedText(
            EditIcon,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            IconTypeface,
            13,
            WorkingChangesBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(iconFormatted, new Point(x - iconFormatted.Width / 2, y - iconFormatted.Height / 2));

        // Draw "Working Changes" label tag
        DrawWorkingChangesTag(dc, y, x);
    }

    private void DrawWorkingChangesTag(DrawingContext dc, double y, double nodeX)
    {
        double labelX = 4;
        string labelText = "Working Changes";

        bool isHighlighted = IsWorkingChangesSelected || IsWorkingChangesHovered;
        double opacity = isHighlighted ? 1.0 : GhostTagOpacity;

        var bgColor = Color.FromArgb((byte)(WorkingChangesColor.A * opacity),
            WorkingChangesColor.R, WorkingChangesColor.G, WorkingChangesColor.B);
        var bgBrush = new SolidColorBrush(bgColor);
        bgBrush.Freeze();

        var textColor = Color.FromArgb((byte)(255 * opacity), 255, 255, 255);
        var textBrush = new SolidColorBrush(textColor);
        textBrush.Freeze();

        // Measure text
        var textFormatted = new FormattedText(
            labelText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            11,
            textBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        double totalWidth = 6 + textFormatted.Width + 6;
        double labelHeight = 18;

        // Draw rounded rectangle background
        var labelRect = new Rect(labelX, y - labelHeight / 2, totalWidth, labelHeight);
        dc.DrawRoundedRectangle(bgBrush, LabelBorderPen, labelRect, 4, 4);

        // Draw text
        dc.DrawText(textFormatted, new Point(labelX + 6, y - textFormatted.Height / 2));

        // Draw connecting line from label to node
        var lineColor = Color.FromArgb((byte)(WorkingChangesColor.A * opacity),
            WorkingChangesColor.R, WorkingChangesColor.G, WorkingChangesColor.B);
        var lineBrush = new SolidColorBrush(lineColor);
        lineBrush.Freeze();
        var linePen = new Pen(lineBrush, 1.5);
        linePen.Freeze();
        dc.DrawLine(linePen, new Point(labelX + totalWidth, y), new Point(nodeX - NodeRadius - 2, y));
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

        // Draw the main trail rectangle
        var trailRect = new Rect(
            trailStartX,
            y - trailHeight / 2,
            trailEndX - trailStartX - accentWidth,
            trailHeight);
        dc.DrawRectangle(trailBrush, null, trailRect);

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
            // Regular commits: circle with white interior (avatar style) - 1.875x size (1.5 * 1.25)
            double avatarRadius = NodeRadius * 1.875;
            var outerPen = new Pen(brush, 2.5);
            outerPen.Freeze();
            dc.DrawEllipse(Brushes.White, outerPen, new Point(x, y), avatarRadius, avatarRadius);

            // Draw a simple person icon inside
            var personIcon = "\uE77B"; // Person icon from Segoe Fluent Icons
            var iconFormatted = new FormattedText(
                personIcon,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                IconTypeface,
                13,
                brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(iconFormatted, new Point(x - iconFormatted.Width / 2, y - iconFormatted.Height / 2));
        }

        // HEAD indicator removed - now shown via enlarged branch tag instead
    }

    private void DrawConnections(DrawingContext dc, GitTreeNode node, Dictionary<string, GitTreeNode> nodesBySha, int rowOffset = 0)
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
            var linePen = new Pen(lineBrush, 2);
            linePen.Freeze();

            DrawRailConnection(dc, linePen, nodeX, nodeY, parentX, parentY, isMergeConnection);
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

            // Draw horizontal line from label to node
            dc.DrawLine(linePen, new Point(lastLabelRight, y), new Point(nodeX - NodeRadius - 2, y));
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
            // Infer: most branches are local (don't have remote prefix like "origin/")
            // and also exist on remote if they're known branches like main/develop
            isLocal = !labelText.Contains('/');
            isRemote = isLocal; // Assume tracked branches exist on both
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

        // Draw connecting line from label to the commit node
        var linePen = new Pen(ghostBrush, 1.5);
        linePen.Freeze();
        dc.DrawLine(linePen, new Point(labelX + totalWidth, y), new Point(nodeX - NodeRadius - 2, y));
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
}
