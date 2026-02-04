using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Leaf.Controls.GitGraph.Services;
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

    private static void OnNodesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GitGraphCanvas canvas)
        {
            canvas._cacheService.ClearNodeCache();
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

    #region Events

    /// <summary>
    /// Event raised when a row expansion state changes.
    /// </summary>
    public event EventHandler<RowExpansionChangedEventArgs>? RowExpansionChanged;

    /// <summary>
    /// Event raised when user double-clicks a branch to checkout.
    /// </summary>
    public event EventHandler<BranchLabel>? BranchCheckoutRequested;

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

    #endregion

    #region Visual Parent Changed

    protected override void OnVisualParentChanged(DependencyObject? oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        ResetScrollViewerCache();
        if (IsLoaded)
            AttachToScrollViewer();
    }

    #endregion

    #region Layout Helper Methods

    private double GetXForColumn(int column) =>
        _layoutService.GetXForColumn(column, LaneWidth, LabelAreaWidth);

    private double GetYForRow(int row) =>
        _layoutService.GetYForRow(row, RowHeight);

    private (int minIndex, int maxIndex) GetVisibleNodeRange(IReadOnlyList<GitTreeNode> nodes, int rowOffset)
    {
        if (nodes.Count == 0)
            return (0, -1);

        var scrollViewer = FindParentScrollViewer();
        if (scrollViewer == null)
            return (0, nodes.Count - 1); // Fallback to all nodes

        return _layoutService.GetVisibleNodeRange(
            nodes.Count,
            scrollViewer.VerticalOffset,
            scrollViewer.ViewportHeight,
            RowHeight,
            rowOffset);
    }

    #endregion
}
