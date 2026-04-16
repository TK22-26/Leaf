using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Leaf.Controls.GitGraph.Services;
using Leaf.Graph;
using Leaf.Models;

namespace Leaf.Controls.GitGraph;

/// <summary>
/// Custom WPF control for rendering the Git commit graph.
/// Uses DrawingVisual for efficient rendering with render culling.
/// </summary>
public partial class GitGraphCanvas : FrameworkElement
{
    #region Services

    private readonly IGitGraphLayoutService _layoutService;
    private readonly IGitGraphHitTestService _hitTestService;
    private readonly IGitGraphStateService _stateService;
    private readonly IGitGraphCacheService _cacheService;

    #endregion

    private Dictionary<string, BranchLabel> _branchLabelLookup = new(StringComparer.OrdinalIgnoreCase);

    // Pass-through lane segments for drawing branch lines beyond the culling range
    private readonly record struct LaneSegment(int Column, int ChildRow, int ParentRow, Brush Color);
    private readonly List<LaneSegment> _laneSegments = [];
    private readonly Dictionary<string, GitTreeNode> _segmentNodeLookup = new(StringComparer.OrdinalIgnoreCase);

    #region Dependency Properties

    public static readonly DependencyProperty NodesProperty =
        DependencyProperty.Register(
            nameof(Nodes),
            typeof(IReadOnlyList<GitTreeNode>),
            typeof(GitGraphCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnNodesChanged));

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

    public static readonly DependencyProperty ColorResolverProperty =
        DependencyProperty.Register(
            nameof(ColorResolver),
            typeof(IBranchColorResolver),
            typeof(GitGraphCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));


    private static void OnNodesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GitGraphCanvas canvas)
        {
            canvas._cacheService.ClearNodeCache();

            canvas._branchLabelLookup.Clear();
            canvas._laneSegments.Clear();
            canvas._segmentNodeLookup.Clear();

            var newNodes = e.NewValue as IReadOnlyList<GitTreeNode>;
            if (newNodes != null)
            {
                foreach (var node in newNodes)
                {
                    foreach (var label in node.BranchLabels)
                        canvas._branchLabelLookup.TryAdd(label.Name, label);
                }

                // Build lane segments for pass-through rendering
                canvas._segmentNodeLookup.EnsureCapacity(newNodes.Count);
                foreach (var node in newNodes)
                    canvas._segmentNodeLookup[node.Sha] = node;

                foreach (var node in newNodes)
                {
                    for (int i = 0; i < node.ParentShas.Count; i++)
                    {
                        if (!canvas._segmentNodeLookup.TryGetValue(node.ParentShas[i], out var parent))
                            continue;
                        if (parent.ColumnIndex != node.ColumnIndex)
                            continue;

                        // Match DrawConnections color: child color for first parent, parent color for merges
                        var color = i > 0
                            ? (parent.NodeColor ?? Brushes.Gray)
                            : (node.NodeColor ?? Brushes.Gray);

                        int childRow = Math.Min(node.RowIndex, parent.RowIndex);
                        int parentRow = Math.Max(node.RowIndex, parent.RowIndex);
                        canvas._laneSegments.Add(new LaneSegment(node.ColumnIndex, childRow, parentRow, color));
                    }
                }

                // Sort by ChildRow ascending for binary search during render
                canvas._laneSegments.Sort((a, b) => a.ChildRow.CompareTo(b.ChildRow));
            }

            canvas.BeginViewportTracking();
            canvas.ScheduleViewportRefresh();
        }
    }

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

    /// <summary>
    /// Per-repository resolver for branch / tag label colours. Null when the
    /// canvas is wired up before a repository is selected (or in design-time);
    /// rendering falls back to a neutral grey so nothing crashes in that case.
    /// </summary>
    public IBranchColorResolver? ColorResolver
    {
        get => (IBranchColorResolver?)GetValue(ColorResolverProperty);
        set => SetValue(ColorResolverProperty, value);
    }

    /// <summary>
    /// Resolves a branch colour via the active <see cref="ColorResolver"/>,
    /// falling back to gray when no resolver is set yet. Used by all render
    /// paths (main rendering, labels, tooltips, expanded rows).
    /// </summary>
    internal Brush ResolveBranchColor(string branchName)
    {
        return ColorResolver?.GetBranchColor(branchName) ?? Brushes.Gray;
    }


    #endregion

    #region Events

    /// <summary>
    /// Event raised when a row expansion state changes.
    /// </summary>
    public event EventHandler<RowExpansionChangedEventArgs>? RowExpansionChanged;

    /// <summary>
    /// Event raised when user double-clicks a branch to checkout.
    /// </summary>
    public event EventHandler<BranchCheckoutRequestedEventArgs>? BranchCheckoutRequested;

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the total extra height from all expanded rows.
    /// </summary>
    public double TotalExpansionHeight => _stateService.GetTotalExpansionHeight(RowHeight);

    #endregion

    #region Constructors

    static GitGraphCanvas()
    {
        LabelBorderPen.Freeze();
        GitHubLogoGeometry.Freeze();
        AzureDevOpsLogoGeometry.Freeze();
    }

    public GitGraphCanvas() : this(null, null, null, null) { }

    public GitGraphCanvas(
        IGitGraphLayoutService? layoutService = null,
        IGitGraphHitTestService? hitTestService = null,
        IGitGraphStateService? stateService = null,
        IGitGraphCacheService? cacheService = null)
    {
        _layoutService = layoutService ?? new GitGraphLayoutService();
        _hitTestService = hitTestService ?? new GitGraphHitTestService();
        _stateService = stateService ?? new GitGraphStateService();
        _cacheService = cacheService ?? new GitGraphCacheService();

        // Wire up state service events
        _stateService.RowExpansionChanged += (s, e) => RowExpansionChanged?.Invoke(this, e);

        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    #endregion

    #region Measure Override

    protected override Size MeasureOverride(Size availableSize)
    {
        var nodes = Nodes;
        if (nodes == null || nodes.Count == 0)
        {
            // Even with no nodes, if we have working changes, show that row
            int emptyRowCount = HasWorkingChanges ? 1 : 0;
            if (emptyRowCount > 0)
            {
                double emptyWidth = LabelAreaWidth + 2 * LaneWidth;
                return new Size(emptyWidth, emptyRowCount * RowHeight);
            }
            return new Size(0, 0);
        }

        // Width: label area + (MaxLane + 2) lanes * LaneWidth
        // Height: node count * RowHeight (+ 1 for working changes if present)
        // Stash nodes are included in nodes.Count
        double width = LabelAreaWidth + (MaxLane + 2) * LaneWidth;
        int rowCount = nodes.Count;
        if (HasWorkingChanges)
        {
            rowCount += 1;
        }

        // Expansion is rendered as overlay, doesn't affect layout height
        double height = rowCount * RowHeight;

        return new Size(width, height);
    }

    #endregion

    #region Visual Parent Changed

    protected override void OnVisualParentChanged(DependencyObject? oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        ResetScrollViewerCache();
        if (IsLoaded)
        {
            AttachToScrollViewer();
            BeginViewportTracking(3);
            ScheduleViewportRefresh();
        }
    }

    #endregion

    #region Layout Helper Methods

    private double GetXForColumn(int column) =>
        _layoutService.GetXForColumn(column, LaneWidth, LabelAreaWidth);

    private double GetYForRow(int row) =>
        _layoutService.GetYForRow(row, RowHeight);

    private double GetFallbackViewportHeight()
    {
        double fallbackViewportHeight = Window.GetWindow(this)?.ActualHeight ?? 0;
        if (fallbackViewportHeight <= 0 || double.IsNaN(fallbackViewportHeight))
            fallbackViewportHeight = ActualHeight;
        if (fallbackViewportHeight <= 0 || double.IsNaN(fallbackViewportHeight))
            fallbackViewportHeight = 900;

        return fallbackViewportHeight;
    }

    private double GetEffectiveViewportHeight(ScrollViewer? scrollViewer)
    {
        double viewportHeight = scrollViewer?.ViewportHeight ?? 0;
        double scrollViewerHeight = scrollViewer?.ActualHeight ?? 0;
        double renderHeight = scrollViewer?.RenderSize.Height ?? 0;
        double fallbackViewportHeight = GetFallbackViewportHeight();

        double bestViewportHeight = Math.Max(viewportHeight, Math.Max(scrollViewerHeight, renderHeight));
        if (bestViewportHeight <= 0 || double.IsNaN(bestViewportHeight))
            bestViewportHeight = fallbackViewportHeight;

        // On startup, ViewportHeight can briefly lag at ~5 rows even though the host is already larger.
        if (bestViewportHeight < RowHeight * 8 && fallbackViewportHeight > bestViewportHeight)
            bestViewportHeight = fallbackViewportHeight;

        return bestViewportHeight;
    }

    private (int minIndex, int maxIndex) GetVisibleNodeRange(IReadOnlyList<GitTreeNode> nodes, int rowOffset)
    {
        if (nodes.Count == 0)
            return (0, -1);

        var scrollViewer = FindParentScrollViewer();
        double viewportHeight = GetEffectiveViewportHeight(scrollViewer);
        double scrollOffset = scrollViewer?.VerticalOffset ?? 0;

        return _layoutService.GetVisibleNodeRange(
            nodes.Count,
            scrollOffset,
            viewportHeight,
            RowHeight,
            rowOffset);
    }

    #endregion
}
