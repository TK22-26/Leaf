using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Leaf.ViewModels;

namespace Leaf.Views;

/// <summary>
/// Host control for the workspace tile grid. Recomputes the
/// <see cref="UniformGrid"/>'s <c>Rows</c> / <c>Columns</c> on every
/// tile-collection mutation so the layout always picks a shape that
/// minimises wasted area for the current tile count, and so the parent
/// tile (always position 0) keeps its top-left slot.
/// </summary>
public partial class WorkspaceGridView : UserControl
{
    private UniformGrid? _tileGrid;
    private INotifyCollectionChanged? _observedTiles;

    public WorkspaceGridView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Layout shape per tile count. Each entry is (rows, columns)
    /// chosen to match what a user would draw on paper — singles fill
    /// the surface, pairs split side-by-side, triples become 1 × 3,
    /// fours become 2 × 2 (parent top-left, three submodules around
    /// it), 5 - 6 are 2 × 3, 7 - 8 are 2 × 4. Beyond 8 we settle on
    /// 3 columns and let rows grow so a workspace with 12 submodules
    /// reads as 3 × 4 — small but legible without horizontal scrolling.
    /// </summary>
    private static (int Rows, int Cols) PickShape(int count)
    {
        if (count <= 1) return (1, 1);
        if (count == 2) return (1, 2);
        if (count == 3) return (1, 3);
        if (count == 4) return (2, 2);
        if (count <= 6) return (2, 3);
        if (count <= 8) return (2, 4);
        // 9+ — three-column grid, rows grow as needed. Past about 12
        // the user will be scrolling within tiles anyway; the priority
        // is keeping each tile wide enough that the title bar's quick-
        // action icons fit.
        var rows = (count + 2) / 3;
        return (rows, 3);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_observedTiles != null)
        {
            _observedTiles.CollectionChanged -= OnTilesChanged;
            _observedTiles = null;
        }

        if (e.NewValue is WorkspaceViewModel vm)
        {
            _observedTiles = vm.Tiles;
            _observedTiles.CollectionChanged += OnTilesChanged;
            ApplyShape();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _tileGrid = FindUniformGrid();
        ApplyShape();
    }

    private void OnTilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // ItemsControl creates the panel lazily; the very first
        // CollectionChanged after Loaded is when it materialises. Re-
        // find the UniformGrid each time in case the visual tree
        // rebuilt under us (rare, but happens when the host swaps the
        // ItemsPanel template).
        if (_tileGrid is null)
        {
            _tileGrid = FindUniformGrid();
        }
        ApplyShape();
    }

    private void ApplyShape()
    {
        if (_tileGrid is null || DataContext is not WorkspaceViewModel vm) return;
        var (rows, cols) = PickShape(vm.Tiles.Count);
        _tileGrid.Rows = rows;
        _tileGrid.Columns = cols;
    }

    /// <summary>
    /// Walks the visual tree to find the UniformGrid the ItemsControl
    /// generated from its ItemsPanelTemplate. ItemsControl doesn't
    /// expose the panel via x:Name across templates, so we walk for
    /// it on first need.
    /// </summary>
    private UniformGrid? FindUniformGrid()
    {
        return FindDescendant<UniformGrid>(this);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) return t;
            var nested = FindDescendant<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }
}
