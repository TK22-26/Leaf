using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    // Track overflow labels by row for hit testing
    private readonly Dictionary<int, (List<BranchLabel> Labels, Rect HitArea)> _overflowByRow = new();
    private readonly Dictionary<int, (List<string> Tags, Rect HitArea, double StartX)> _tagOverflowByRow = new();
    private int _hoveredOverflowRow = -1;

    // Track which rows are expanded (showing all branches vertically)
    // Key: node index (not display row), Value: number of extra rows needed
    private readonly Dictionary<int, int> _expandedNodes = new();
    private readonly HashSet<int> _expandedTagNodes = new();

    // Track animation progress for each expanded node (0.0 to 1.0)
    private readonly Dictionary<int, double> _expansionProgress = new();
    private System.Windows.Threading.DispatcherTimer? _animationTimer;
    private const double AnimationDuration = 100; // milliseconds - snappy expand/collapse
    private const double AnimationStep = 16.67; // ~60fps - standard refresh rate

    // Track hovered item in expanded dropdown: (nodeIndex, branchIndex)
    private (int NodeIndex, int BranchIndex) _hoveredExpandedItem = (-1, -1);

    // Track expanded item hit areas for hover/click detection
    private readonly Dictionary<int, List<(BranchLabel Label, Rect HitArea)>> _expandedItemHitAreas = new();
    private readonly Dictionary<int, List<Rect>> _expandedTagHitAreas = new();

    // Popup for showing branch names tooltip
    private System.Windows.Controls.Primitives.Popup? _branchTooltipPopup;
    private System.Windows.Controls.StackPanel? _branchTooltipPanel;

    #region Performance Caches

    // Cache dictionary lookup - rebuilt only when Nodes collection changes
    private Dictionary<string, GitTreeNode>? _nodesByShaCache;
    private IReadOnlyList<GitTreeNode>? _cachedNodesForDict;

    // Cache ScrollViewer reference - found once, reused
    private ScrollViewer? _parentScrollViewer;
    private bool _scrollViewerSearched;
    private bool _scrollViewerHooked;

    // Geometry cache - stores geometries at origin, keyed by radius only
    private sealed class GeometryCache
    {
        private readonly Dictionary<double, EllipseGeometry> _circlesByRadius = new();
        private RectangleGeometry? _fullAreaGeometry;
        private double _lastWidth, _lastHeight;

        public EllipseGeometry GetCircleAtOrigin(double radius)
        {
            if (!_circlesByRadius.TryGetValue(radius, out var geom))
            {
                geom = new EllipseGeometry(new Point(0, 0), radius, radius);
                geom.Freeze();
                _circlesByRadius[radius] = geom;
            }
            return geom;
        }

        public RectangleGeometry GetFullArea(double width, double height)
        {
            if (_fullAreaGeometry == null || _lastWidth != width || _lastHeight != height)
            {
                _fullAreaGeometry = new RectangleGeometry(new Rect(0, 0, width, height));
                _fullAreaGeometry.Freeze();
                _lastWidth = width;
                _lastHeight = height;
            }
            return _fullAreaGeometry;
        }

        public void Clear()
        {
            _circlesByRadius.Clear();
            _fullAreaGeometry = null;
        }
    }

    private readonly GeometryCache _geometryCache = new();
    private readonly Dictionary<(double r, double w, double h), CombinedGeometry> _clipGeometryCache = new();

    // FormattedText cache - limit size to avoid memory issues
    private readonly Dictionary<string, FormattedText> _formattedTextCache = new();
    private const int MaxFormattedTextCacheSize = 200;

    // Pen cache - keyed by brush reference
    private readonly Dictionary<Brush, Pen> _connectionPenCache = new();
    private const double ConnectionPenWidth = 2.0;

    #endregion

    /// <summary>
    /// Event raised when a row expansion state changes.
    /// </summary>
    public event EventHandler<RowExpansionChangedEventArgs>? RowExpansionChanged;

    /// <summary>
    /// Event raised when user double-clicks a branch to checkout.
    /// </summary>
    public event EventHandler<BranchLabel>? BranchCheckoutRequested;

    /// <summary>
    /// Gets the total extra height from all expanded rows.
    /// </summary>
    public double TotalExpansionHeight => _expandedNodes.Values.Sum() * RowHeight;

    static GitGraphCanvas()
    {
        LabelBorderPen.Freeze();
        GitHubLogoGeometry.Freeze();
        AzureDevOpsLogoGeometry.Freeze();
    }

    public GitGraphCanvas()
    {
        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override void OnVisualParentChanged(DependencyObject? oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        ResetScrollViewerCache();
        if (IsLoaded)
            AttachToScrollViewer();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Handle double-click on expanded branch item
        if (e.ClickCount == 2)
        {
            var pos = e.GetPosition(this);
            foreach (var kvp in _expandedItemHitAreas)
            {
                for (int i = 0; i < kvp.Value.Count; i++)
                {
                    var item = kvp.Value[i];
                    if (item.HitArea.Contains(pos))
                    {
                        // Double-clicked on a branch - request checkout
                        BranchCheckoutRequested?.Invoke(this, item.Label);
                        e.Handled = true;
                        return;
                    }
                }
            }
        }
    }

    private void StartExpansionAnimation(int nodeIndex, bool expanding)
    {
        if (_animationTimer == null)
        {
            _animationTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AnimationStep)
            };
            _animationTimer.Tick += OnAnimationTick;
        }

        // Set initial progress
        if (expanding)
        {
            _expansionProgress[nodeIndex] = 0.0;
        }
        // For collapsing, progress will decrease from current value

        if (!_animationTimer.IsEnabled)
        {
            _animationTimer.Start();
        }
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        bool anyAnimating = false;
        double step = AnimationStep / AnimationDuration;

        var nodesToUpdate = _expansionProgress.Keys.ToList();
        foreach (var nodeIndex in nodesToUpdate)
        {
            bool isExpanded = _expandedNodes.ContainsKey(nodeIndex);
            double current = _expansionProgress[nodeIndex];

            if (isExpanded)
            {
                // Expanding - increase progress
                current = Math.Min(1.0, current + step);
                _expansionProgress[nodeIndex] = current;
                if (current < 1.0) anyAnimating = true;
            }
            else
            {
                // Collapsing - decrease progress
                current = Math.Max(0.0, current - step);
                if (current > 0.0)
                {
                    _expansionProgress[nodeIndex] = current;
                    anyAnimating = true;
                }
                else
                {
                    _expansionProgress.Remove(nodeIndex);
                }
            }
        }

        InvalidateVisual();

        if (!anyAnimating)
        {
            _animationTimer?.Stop();
        }
    }

    /// <summary>
    /// Ease-out function for smoother animation (fast start, slow end).
    /// </summary>
    private static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);

    private void ShowBranchTooltip(List<BranchLabel> branches, Rect tagRect)
    {
        if (_branchTooltipPopup == null)
        {
            _branchTooltipPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical
            };

            var border = new System.Windows.Controls.Border
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
            var row = new System.Windows.Controls.Grid
            {
                Margin = new Thickness(4, 3, 4, 3),
                Width = rowWidth
            };
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });

            // Colored circle
            var circle = new System.Windows.Shapes.Ellipse
            {
                Width = circleSize,
                Height = circleSize,
                Fill = branchBrush,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Controls.Grid.SetColumn(circle, 0);
            row.Children.Add(circle);

            // Branch name
            var nameText = new System.Windows.Controls.TextBlock
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
            System.Windows.Controls.Grid.SetColumn(nameText, 1);
            row.Children.Add(nameText);

            // Icons (local/remote)
            var iconText = "";
            if (branch.IsLocal) iconText += ComputerIcon;
            if (branch.IsLocal && branch.IsRemote) iconText += " ";
            if (branch.IsRemote) iconText += CloudIcon;

            if (!string.IsNullOrEmpty(iconText))
            {
                var icons = new System.Windows.Controls.TextBlock
                {
                    Text = iconText,
                    Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                    FontSize = 11,
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                System.Windows.Controls.Grid.SetColumn(icons, 2);
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

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);

        // Check if clicking on any overflow indicator (iterate through all, checking hit areas)
        if (pos.X < LabelAreaWidth)
        {
            foreach (var kvp in _overflowByRow)
            {
                int displayRow = kvp.Key;
                var overflow = kvp.Value;

                if (overflow.HitArea.Contains(pos))
                {
                    // Calculate node index from display row
                    int rowOffset = (HasWorkingChanges ? 1 : 0) + StashCount;
                    int nodeIndex = displayRow - rowOffset;

                    if (nodeIndex >= 0)
                    {
                        // Toggle expansion
                        bool wasExpanded = _expandedNodes.ContainsKey(nodeIndex);
                        if (wasExpanded)
                        {
                            _expandedNodes.Remove(nodeIndex);
                            _expandedItemHitAreas.Remove(nodeIndex);
                            // Release mouse capture when collapsing
                            if (_expandedNodes.Count == 0)
                                Mouse.Capture(null);
                        }
                        else
                        {
                            _expandedNodes[nodeIndex] = overflow.Labels.Count;
                            // Capture mouse to detect clicks outside
                            Mouse.Capture(this, CaptureMode.SubTree);
                        }

                        // Start animation
                        StartExpansionAnimation(nodeIndex, !wasExpanded);

                        // Redraw
                        InvalidateVisual();
                        InvalidateMeasure();

                        // Notify listeners
                        RowExpansionChanged?.Invoke(this, new RowExpansionChangedEventArgs(
                            nodeIndex,
                            !wasExpanded,
                            !wasExpanded ? overflow.Labels.Count : 0,
                            TotalExpansionHeight));
                    }
                    e.Handled = true;
                    return;
                }
            }

            foreach (var kvp in _tagOverflowByRow)
            {
                int displayRow = kvp.Key;
                var overflow = kvp.Value;

                if (overflow.HitArea.Contains(pos))
                {
                    int rowOffset = (HasWorkingChanges ? 1 : 0) + StashCount;
                    int nodeIndex = displayRow - rowOffset;

                    if (nodeIndex >= 0)
                    {
                        bool wasExpanded = _expandedTagNodes.Contains(nodeIndex);
                        if (wasExpanded)
                        {
                            _expandedTagNodes.Remove(nodeIndex);
                            _expandedTagHitAreas.Remove(nodeIndex);
                            if (_expandedNodes.Count == 0 && _expandedTagNodes.Count == 0)
                                Mouse.Capture(null);
                        }
                        else
                        {
                            _expandedTagNodes.Add(nodeIndex);
                            Mouse.Capture(this, CaptureMode.SubTree);
                        }

                        InvalidateVisual();
                        InvalidateMeasure();
                    }
                    e.Handled = true;
                    return;
                }
            }
        }

        // If clicking outside any expanded tag, collapse all expanded tags with animation
        if (_expandedNodes.Count > 0 || _expandedTagNodes.Count > 0)
        {
            // Check if click is inside any expanded tag hit area
            bool clickedInsideExpanded = false;
            foreach (var kvp in _expandedItemHitAreas)
            {
                foreach (var item in kvp.Value)
                {
                    if (item.HitArea.Contains(pos))
                    {
                        clickedInsideExpanded = true;
                        break;
                    }
                }
                if (clickedInsideExpanded) break;
            }

            foreach (var kvp in _expandedTagHitAreas)
            {
                foreach (var rect in kvp.Value)
                {
                    if (rect.Contains(pos))
                    {
                        clickedInsideExpanded = true;
                        break;
                    }
                }
                if (clickedInsideExpanded) break;
            }

            // Also check if clicked on the overflow tag itself
            foreach (var kvp in _overflowByRow)
            {
                if (kvp.Value.HitArea.Contains(pos))
                {
                    clickedInsideExpanded = true;
                    break;
                }
            }

            foreach (var kvp in _tagOverflowByRow)
            {
                if (kvp.Value.HitArea.Contains(pos))
                {
                    clickedInsideExpanded = true;
                    break;
                }
            }

            if (!clickedInsideExpanded)
            {
                // Collapse all expanded nodes with animation
                CollapseAllExpandedTags();
                CollapseAllExpandedTagLabels();
            }
        }
    }

    /// <summary>
    /// Collapse all expanded branch tags with animation.
    /// </summary>
    private void CollapseAllExpandedTags()
    {
        if (_expandedNodes.Count == 0)
            return;

        var nodesToCollapse = _expandedNodes.Keys.ToList();
        foreach (var nodeIndex in nodesToCollapse)
        {
            _expandedNodes.Remove(nodeIndex);
            _expandedItemHitAreas.Remove(nodeIndex);
            StartExpansionAnimation(nodeIndex, false); // Animate collapse
        }

        // Release mouse capture
        Mouse.Capture(null);

        InvalidateVisual();
        InvalidateMeasure();

        // Notify listeners
        RowExpansionChanged?.Invoke(this, new RowExpansionChangedEventArgs(
            -1, false, 0, TotalExpansionHeight));
    }

    private void CollapseAllExpandedTagLabels()
    {
        if (_expandedTagNodes.Count == 0)
            return;

        _expandedTagNodes.Clear();
        _expandedTagHitAreas.Clear();

        if (_expandedNodes.Count == 0)
        {
            Mouse.Capture(null);
        }

        InvalidateVisual();
        InvalidateMeasure();
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        var nodes = Nodes;

        // Check for hover over expanded dropdown items first
        bool foundExpandedHover = false;
        foreach (var kvp in _expandedItemHitAreas)
        {
            for (int i = 0; i < kvp.Value.Count; i++)
            {
                if (kvp.Value[i].HitArea.Contains(pos))
                {
                    if (_hoveredExpandedItem != (kvp.Key, i))
                    {
                        _hoveredExpandedItem = (kvp.Key, i);
                        Cursor = Cursors.Hand;
                        InvalidateVisual();
                    }
                    foundExpandedHover = true;
                    break;
                }
            }
            if (foundExpandedHover) break;
        }

        if (!foundExpandedHover && _hoveredExpandedItem.NodeIndex >= 0)
        {
            _hoveredExpandedItem = (-1, -1);
            Cursor = Cursors.Arrow;
            InvalidateVisual();
        }

        // Check for overflow indicator hover first (within label area) - show tooltip
        if (pos.X < LabelAreaWidth)
        {
            foreach (var kvp in _overflowByRow)
            {
                int displayRow = kvp.Key;
                var overflow = kvp.Value;

                if (overflow.HitArea.Contains(pos))
                {
                    if (_hoveredOverflowRow != displayRow)
                    {
                        _hoveredOverflowRow = displayRow;
                        // Show popup with all branch names (including visible first one)
                        int tooltipRowOffset = (HasWorkingChanges ? 1 : 0) + StashCount;
                        int tooltipNodeIndex = displayRow - tooltipRowOffset;
                        if (Nodes != null && tooltipNodeIndex >= 0 && tooltipNodeIndex < Nodes.Count)
                        {
                            var tooltipNode = Nodes[tooltipNodeIndex];
                            ShowBranchTooltip(tooltipNode.BranchLabels, overflow.HitArea);
                        }
                    }
                    Cursor = Cursors.Hand;
                    return;
                }
            }

            foreach (var kvp in _tagOverflowByRow)
            {
                int displayRow = kvp.Key;
                var overflow = kvp.Value;

                if (overflow.HitArea.Contains(pos))
                {
                    Cursor = Cursors.Hand;
                    return;
                }
            }
        }

        // If we were hovering over overflow but now left, hide tooltip
        if (_hoveredOverflowRow >= 0)
        {
            _hoveredOverflowRow = -1;
            HideBranchTooltip();
            Cursor = Cursors.Arrow;
        }

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

        if (_hoveredOverflowRow >= 0)
        {
            _hoveredOverflowRow = -1;
            HideBranchTooltip();
        }
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

        // Expansion is rendered as overlay, doesn't affect layout height
        double height = rowCount * RowHeight;

        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Clear overflow tracking before re-drawing
        _overflowByRow.Clear();
        _tagOverflowByRow.Clear();
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
                var fullArea = _geometryCache.GetFullArea(ActualWidth, ActualHeight);
                var wipCircle = new EllipseGeometry(new Point(wipX, wipY), avatarRadius, avatarRadius);
                var targetCircle = new EllipseGeometry(new Point(targetX, targetY), targetRadius, targetRadius);

                var clipWithoutWip = new CombinedGeometry(GeometryCombineMode.Exclude, fullArea, wipCircle);
                var clipGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, clipWithoutWip, targetCircle);
                clipGeometry.Freeze();

                dc.PushClip(clipGeometry);
                var linePen = GetConnectionPen(branchBrush);
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

        // Calculate visible node range for render culling
        // Note: rowOffset accounts for working changes and stash rows
        var (minNodeIndex, maxNodeIndex) = GetVisibleNodeRange(nodes, rowOffset);

        // Use cached dictionary for efficient parent finding
        var nodesBySha = GetNodesBySha(nodes);

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

            if (node.TagNames.Count > 0 && !_expandedNodes.ContainsKey(node.RowIndex))
            {
                double tagStartX = branchLabelRight > 0 ? branchLabelRight + 4 : 4;
                DrawTagLabels(dc, node, rowOffset, tagStartX);
            }
        }

        // Fifth pass: Draw ghost tags for hovered and selected commits
        // (only if they don't already have branch labels)
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
            hoveredNode.Sha != SelectedSha) // Don't draw twice if same commit
        {
            DrawGhostTag(dc, hoveredNode, rowOffset);
        }

        // Sixth pass: Draw expanded branch dropdowns on top of everything
        foreach (var kvp in _expandedNodes)
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
        foreach (var nodeIndex in _expandedTagNodes)
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

                  var fillBrush = IdenticonGenerator.GetIdenticonBrush(key, iconSize, backgroundColor);
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
            var linePen = GetConnectionPen(lineBrush);

            // Draw connection line
            // Note: Nodes are drawn AFTER connections, so they naturally cover line endpoints
            // We use clip geometry only to avoid visual artifacts at the overlap
            var fullArea = _geometryCache.GetFullArea(ActualWidth, ActualHeight);
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
        if (_expandedNodes.ContainsKey(nodeIndex))
        {
            return 0;
        }

        foreach (var label in node.BranchLabels)
        {
            // Each branch gets a unique color generated from its name (same as lane colors)
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
                iconText += " "; // Space between icons when both are shown
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

            // Calculate custom remote icon size (same as icon font size)
            double customIconSize = useCustomRemoteIcon ? iconFontSize : 0;
            double customIconSpace = useCustomRemoteIcon ? (iconFormatted.Width > 0 ? 2 : 0) : 0; // spacing if other icons exist

            // Calculate label dimensions (name on left, icons/suffix right-aligned)
            double iconWidth = iconFormatted.Width;
            double nameWidth = nameFormatted.Width;
            double hPadding = label.IsCurrent ? 8 : 6;

            // Check if this is the last label that will fit - if more remain, add "+N" suffix
            // Only count remaining branches (tags have their own separate overflow indicator)
            int remainingAfterThis = node.BranchLabels.Count - drawnCount - 1;
            string overflowSuffix = "";
            double suffixWidth = 0;
            FormattedText? suffixFormatted = null;

            // Always show overflow suffix when there are remaining branches or tags (force stacking)
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
                break; // Nothing fits

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

                // Select the appropriate geometry
                Geometry iconGeometry = label.RemoteType == RemoteType.GitHub
                    ? GitHubLogoGeometry
                    : AzureDevOpsLogoGeometry;

                // Get the bounds to calculate scale
                double sourceWidth = label.RemoteType == RemoteType.GitHub ? GitHubLogoWidth : AzureDevOpsLogoWidth;
                double sourceHeight = label.RemoteType == RemoteType.GitHub ? GitHubLogoHeight : AzureDevOpsLogoHeight;

                // Calculate scale to fit the icon in the target size
                double scale = customIconSize / Math.Max(sourceWidth, sourceHeight);

                // Create transform to scale and position the icon
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

                // Store overflow info - hit area is the entire tag, dropdown at left edge
                var overflowLabels = node.BranchLabels.Skip(drawnCount + 1).ToList();
                _overflowByRow[displayRow] = (overflowLabels, labelRect);
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

        // Clear overflow tracking if no overflow
        if (drawnCount == node.BranchLabels.Count)
        {
            _overflowByRow.Remove(displayRow);
        }

        // Draw connecting line from last label to the commit node (same color as tag)
        // Thicker line for current branch
        if (node.BranchLabels.Count > 0 && lastLabelRight > 0 && lastLabelBrush != null)
        {
            double lineThickness = lastLabelIsCurrent ? 2.5 : 1.5;
            var linePen = new Pen(lastLabelBrush, lineThickness);
            linePen.Freeze();

            // Draw horizontal line from label to node
            // For merge commits, clip out the dot so line renders behind it
            // For regular commits, stop before the avatar edge
            if (node.IsMerge)
            {
                double mergeRadius = NodeRadius * 0.875;
                var fullArea = _geometryCache.GetFullArea(ActualWidth, ActualHeight);
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

        if (_expandedTagNodes.Contains(node.RowIndex))
        {
            _tagOverflowByRow[displayRow] = (node.TagNames, Rect.Empty, labelX);
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
                _tagOverflowByRow[displayRow] = (overflowTags, labelRect, labelX);
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
                var fullArea = _geometryCache.GetFullArea(ActualWidth, ActualHeight);
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

    private void DrawExpandedTagLabels(DrawingContext dc, GitTreeNode node, int rowOffset)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        int displayRow = node.RowIndex + rowOffset;
        double y = GetYForRow(node.RowIndex + rowOffset);
        double nodeX = GetXForColumn(node.ColumnIndex);

        if (!_tagOverflowByRow.TryGetValue(displayRow, out var overflow))
            return;

        var tags = node.TagNames;
        if (tags.Count == 0)
            return;

        double labelX = overflow.StartX;
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

        _expandedTagHitAreas[node.RowIndex] = hitAreas;

        var linePen = new Pen(tagBgBrush, 1.5);
        linePen.Freeze();

        if (node.IsMerge)
        {
            double mergeRadius = NodeRadius * 0.875;
            var fullArea = _geometryCache.GetFullArea(ActualWidth, ActualHeight);
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
        double rawProgress = _expansionProgress.GetValueOrDefault(nodeIndex, 1.0);
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

        // Tag width matches collapsed state (name + icons + custom icon + suffix)
        double tagWidth = hPadding + firstNameFormatted.Width + 4 + firstIconFormatted.Width + firstCustomIconSpace + firstCustomIconSize + suffixFormatted.Width + hPadding;

        // Calculate heights - first item uses itemHeight, others have good vertical padding
        double otherItemHeight = 22; // Good vertical spacing between items
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
        // First item centered at baseY (same as collapsed), others below
        double currentY = baseY;
        int branchIndex = 0;

        foreach (var label in node.BranchLabels)
        {
            // Current branch gets larger font, others stay small
            double labelFontSize = label.IsCurrent ? 13 : 11;
            double currentItemHeight = branchIndex == 0 ? itemHeight : otherItemHeight;
            // Use appropriate padding for each item
            double itemHPadding = label.IsCurrent ? 8 : 6;

            // Only draw if within animated bounds
            double itemTop = currentY - currentItemHeight / 2;
            if (itemTop < tagTop + tagHeight)
            {
                // Check if this item is hovered
                bool isHovered = _hoveredExpandedItem == (nodeIndex, branchIndex);

                // Get this branch's color for border and background
                Brush branchColorBrush = GraphBuilder.GetBranchColor(label.Name);
                var branchColor = ((SolidColorBrush)branchColorBrush).Color;

                // Draw border and background around non-first items using their branch color
                // Fills edge-to-edge to completely cover the underlying tag color
                // Opacity increases on hover (like WIP row behavior)
                if (branchIndex > 0)
                {
                    byte alpha = isHovered ? (byte)255 : (byte)200; // More solid on hover
                    var bgBrush = new SolidColorBrush(Color.FromArgb(alpha, branchColor.R, branchColor.G, branchColor.B));
                    bgBrush.Freeze();
                    // Full width/height to cover underlying tag, no border
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

                // Check if we need to draw a custom remote icon
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

                // Calculate custom icon size for expanded labels
                double customIconSizeExp = useCustomRemoteIconExp ? labelFontSize : 0;
                double customIconSpaceExp = useCustomRemoteIconExp ? (iconFormatted.Width > 0 ? 2 : 0) : 0;
                double iconBlockWidthExp = iconFormatted.Width + customIconSpaceExp + customIconSizeExp;
                double nameMaxWidthExp = tagWidth - (itemHPadding * 2) - (iconBlockWidthExp > 0 ? iconBlockWidthExp + 4 : 0);
                if (nameMaxWidthExp > 0)
                {
                    nameFormatted.MaxTextWidth = nameMaxWidthExp;
                }

                // Calculate opacity based on how much of the item is visible during animation
                double itemBottom = itemTop + currentItemHeight;
                double visibleRatio = Math.Min(1.0, Math.Max(0.0, (tagTop + tagHeight - itemTop) / currentItemHeight));

                if (visibleRatio > 0.3) // Only draw if more than 30% visible
                {
                    // Draw name with proper padding for this item
                    dc.DrawText(nameFormatted, new Point(labelX + itemHPadding, currentY - nameFormatted.Height / 2));

                    // Draw icons
                    double expIconX = labelX + itemHPadding + nameFormatted.Width + 4;
                    dc.DrawText(iconFormatted, new Point(expIconX, currentY - iconFormatted.Height / 2));

                    // Draw custom remote icon if needed
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

                // Store hit area for this item
                var itemHitRect = new Rect(labelX, itemTop, tagWidth, currentItemHeight);
                hitAreas.Add((label, itemHitRect));
            }

            // Move Y down - first item uses full height, others use smaller height
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

        // Store hit areas for hover/click detection
        _expandedItemHitAreas[nodeIndex] = hitAreas;

        // Draw connecting line from tag to node
        // For merge commits, clip out the dot so line renders behind it
        // For regular commits, stop before the avatar edge
        var linePen = new Pen(tagBgBrush, 1.5);
        linePen.Freeze();

        if (node.IsMerge)
        {
            double expandedMergeRadius = NodeRadius * 0.875;
            var fullArea = _geometryCache.GetFullArea(ActualWidth, ActualHeight);
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
        _overflowByRow[displayRow] = (node.BranchLabels.Skip(1).ToList(), tagRect);
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
            // Infer from branch name format
            // Local branches don't have "/" prefix, remote branches have "origin/" etc.
            isLocal = !labelText.Contains('/');
            isRemote = labelText.Contains('/'); // Only show cloud if it's actually a remote ref
        }

        // Check if we need custom remote icon
        bool useCustomRemoteIconGhost = isRemote && (remoteType == RemoteType.GitHub || remoteType == RemoteType.AzureDevOps);

        // Build icon text
        var iconText = "";
        if (isLocal)
            iconText += ComputerIcon;
        if (isLocal && isRemote)
            iconText += " ";
        if (isRemote && !useCustomRemoteIconGhost)
            iconText += CloudIcon;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Measure icon text
        var iconFormatted = new FormattedText(
            iconText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            IconTypeface,
            11,
            ghostTextBrush,
            dpi);

        // Measure branch name text
        var nameFormatted = new FormattedText(
            labelText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            11,
            ghostTextBrush,
            dpi);

        // Calculate custom icon size for ghost tag
        double ghostCustomIconSize = useCustomRemoteIconGhost ? 11 : 0;
        double ghostCustomIconSpace = useCustomRemoteIconGhost ? (iconFormatted.Width > 0 ? 2 : 0) : 0;

        // Calculate label dimensions (name + icons, matching regular label style)
        double iconWidth = iconFormatted.Width;
        double nameWidth = nameFormatted.Width;
        double totalWidth = iconWidth > 0 || ghostCustomIconSize > 0
            ? 6 + nameWidth + 4 + iconWidth + ghostCustomIconSpace + ghostCustomIconSize + 6  // padding + name + gap + icons + custom + padding
            : 6 + nameWidth + 6;                  // padding + name + padding
        double labelHeight = 18;

        // Draw rounded rectangle background
        var labelRect = new Rect(labelX, y - labelHeight / 2, totalWidth, labelHeight);
        dc.DrawRoundedRectangle(ghostBrush, LabelBorderPen, labelRect, 4, 4);

        // Draw branch name first
        dc.DrawText(nameFormatted, new Point(labelX + 6, y - nameFormatted.Height / 2));

        // Draw icons after name (if any)
        double ghostIconX = labelX + 6 + nameWidth + 4;
        if (iconWidth > 0)
        {
            dc.DrawText(iconFormatted, new Point(ghostIconX, y - iconFormatted.Height / 2));
        }

        // Draw custom remote icon if needed
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

        // Draw connecting line from label to the commit node
        // For merge commits, clip out the dot so line renders behind it
        // For regular commits, stop before the avatar edge
        var linePen = new Pen(ghostBrush, 1.5);
        linePen.Freeze();

        if (node.IsMerge)
        {
            double ghostMergeRadius = NodeRadius * 0.875;
            var fullArea = _geometryCache.GetFullArea(ActualWidth, ActualHeight);
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

    #region Performance Helper Methods

    /// <summary>
    /// Gets cached dictionary for node lookup by SHA.
    /// Only rebuilt when Nodes collection reference changes.
    /// </summary>
    private Dictionary<string, GitTreeNode> GetNodesBySha(IReadOnlyList<GitTreeNode> nodes)
    {
        if (_cachedNodesForDict != nodes || _nodesByShaCache == null)
        {
            _cachedNodesForDict = nodes;
            _nodesByShaCache = nodes.ToDictionary(n => n.Sha);
        }
        return _nodesByShaCache;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ResetScrollViewerCache();
        AttachToScrollViewer();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachFromScrollViewer();
    }

    private void ResetScrollViewerCache()
    {
        DetachFromScrollViewer();
        _parentScrollViewer = null;
        _scrollViewerSearched = false;
    }

    private void AttachToScrollViewer()
    {
        var scrollViewer = FindParentScrollViewer();
        if (scrollViewer == null)
            return;

        if (!ReferenceEquals(_parentScrollViewer, scrollViewer))
            _parentScrollViewer = scrollViewer;

        if (_scrollViewerHooked)
            return;

        _parentScrollViewer.ScrollChanged += ParentScrollViewer_ScrollChanged;
        _parentScrollViewer.SizeChanged += ParentScrollViewer_SizeChanged;
        _scrollViewerHooked = true;
    }

    private void DetachFromScrollViewer()
    {
        if (_parentScrollViewer != null && _scrollViewerHooked)
        {
            _parentScrollViewer.ScrollChanged -= ParentScrollViewer_ScrollChanged;
            _parentScrollViewer.SizeChanged -= ParentScrollViewer_SizeChanged;
        }
        _scrollViewerHooked = false;
    }

    private void ParentScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Re-render visible range when scrolling to keep culling accurate.
        InvalidateVisual();
    }

    private void ParentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Re-render when viewport size changes (window resize/maximize).
        InvalidateVisual();
    }

    /// <summary>
    /// Finds and caches the parent ScrollViewer for viewport calculations.
    /// </summary>
    private ScrollViewer? FindParentScrollViewer()
    {
        if (_scrollViewerSearched)
            return _parentScrollViewer;

        _scrollViewerSearched = true;
        DependencyObject? parent = VisualTreeHelper.GetParent(this);
        while (parent != null)
        {
            if (parent is ScrollViewer sv)
            {
                _parentScrollViewer = sv;
                return sv;
            }
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    /// <summary>
    /// Calculates the visible node index range based on scroll position.
    /// Uses fixed row height assumption for O(1) calculation.
    /// </summary>
    private (int minIndex, int maxIndex) GetVisibleNodeRange(IReadOnlyList<GitTreeNode> nodes, int rowOffset)
    {
        if (nodes.Count == 0)
            return (0, -1);

        var scrollViewer = FindParentScrollViewer();
        if (scrollViewer == null)
            return (0, nodes.Count - 1); // Fallback to all nodes

        double viewportTop = scrollViewer.VerticalOffset;
        double viewportBottom = viewportTop + scrollViewer.ViewportHeight;

        // CRITICAL: Large padding ABOVE for children that draw to visible parents
        // A commit above viewport draws DOWN to its parent which may be visible
        // Merge commits can have parents many rows apart (long-running feature branches)
        double paddingAbove = RowHeight * 100;  // ~100 rows lookback for merge branches
        double paddingBelow = RowHeight * 5;    // Small padding below

        viewportTop = Math.Max(0, viewportTop - paddingAbove);
        viewportBottom += paddingBelow;

        // Convert viewport coordinates to node indices
        // Account for rowOffset (working changes row + stash rows)
        int minRow = Math.Max(0, (int)(viewportTop / RowHeight) - rowOffset);
        int maxRow = (int)(viewportBottom / RowHeight) - rowOffset;

        int minIndex = Math.Max(0, minRow);
        int maxIndex = Math.Min(nodes.Count - 1, maxRow);

        return (minIndex, maxIndex);
    }

    /// <summary>
    /// Clears all performance caches. Call when Nodes collection changes.
    /// </summary>
    private void ClearCaches()
    {
        _nodesByShaCache = null;
        _cachedNodesForDict = null;
        _formattedTextCache.Clear();
        _connectionPenCache.Clear();
        // Note: Geometry cache uses radius-only keys, so doesn't need clearing on node change
    }

    /// <summary>
    /// Gets a cached Pen for the given brush. Creates and freezes if not cached.
    /// </summary>
    private Pen GetConnectionPen(Brush brush)
    {
        if (!_connectionPenCache.TryGetValue(brush, out var pen))
        {
            pen = new Pen(brush, ConnectionPenWidth);
            pen.Freeze();
            _connectionPenCache[brush] = pen;
        }
        return pen;
    }

    #endregion

    private double GetXForColumn(int column)
    {
        // Shift graph right by label area width, then center of the lane
        return LabelAreaWidth + (column + 0.5) * LaneWidth;
    }

    private double GetYForRow(int row)
    {
        // Simple calculation - expansion is rendered as overlay
        return row * RowHeight + RowHeight / 2;
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

/// <summary>
/// Event args for row expansion changes.
/// </summary>
public class RowExpansionChangedEventArgs : EventArgs
{
    public int NodeIndex { get; }
    public bool IsExpanded { get; }
    public int ExtraRows { get; }
    public double TotalExpansionHeight { get; }

    public RowExpansionChangedEventArgs(int nodeIndex, bool isExpanded, int extraRows, double totalExpansionHeight)
    {
        NodeIndex = nodeIndex;
        IsExpanded = isExpanded;
        ExtraRows = extraRows;
        TotalExpansionHeight = totalExpansionHeight;
    }
}
