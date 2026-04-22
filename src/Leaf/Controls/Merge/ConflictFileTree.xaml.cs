#nullable enable
using System.Windows;
using System.Windows.Controls;
using Leaf.Models;
using Leaf.ViewModels.Merge;

namespace Leaf.Controls.Merge;

/// <summary>
/// Grouped conflict file list (C4). Hosts a <see cref="TreeView"/> of
/// <see cref="ConflictTreeNode"/> items built by
/// <see cref="ConflictTreeBuilder"/>; surfaces the user's file selection
/// through the two-way <see cref="SelectedFile"/> dependency property
/// so existing bindings to <c>MergeEditorViewModel.SelectedConflict</c>
/// keep working unchanged.
/// </summary>
/// <remarks>
/// <para>
/// Selection contract: when the user clicks a file row, the TreeView fires
/// <c>SelectedItemChanged</c>, we pick out the <see cref="ConflictTreeNode.Conflict"/>,
/// and push it to <see cref="SelectedFile"/>. Clicking a folder leaves
/// <see cref="SelectedFile"/> unchanged — folders are organizational only,
/// not selectable file targets. The reverse binding (VM → tree) re-expands
/// and highlights the matching file when the VM changes the selection
/// through another path (keyboard nav, command palette, etc.).
/// </para>
/// </remarks>
public partial class ConflictFileTree : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable<ConflictTreeNode>), typeof(ConflictFileTree),
        new FrameworkPropertyMetadata(default(IEnumerable<ConflictTreeNode>),
            OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedFileProperty = DependencyProperty.Register(
        nameof(SelectedFile), typeof(ConflictInfo), typeof(ConflictFileTree),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectedFileChanged));

    public IEnumerable<ConflictTreeNode>? ItemsSource
    {
        get => (IEnumerable<ConflictTreeNode>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ConflictInfo? SelectedFile
    {
        get => (ConflictInfo?)GetValue(SelectedFileProperty);
        set => SetValue(SelectedFileProperty, value);
    }

    private bool _suppressSelectionSync;
    private bool _suppressExpandCollapseCapture;

    // Folder paths the user has explicitly collapsed — survives tree
    // rebuilds (which create fresh TreeViewItems and would otherwise reset
    // every folder back to expanded). Default state is "not collapsed", so
    // a folder whose path isn't in here is auto-expanded on rebuild.
    private readonly HashSet<string> _collapsedFolders = new(StringComparer.Ordinal);

    public ConflictFileTree()
    {
        InitializeComponent();
        AddHandler(TreeViewItem.ExpandedEvent,
            new RoutedEventHandler(OnTreeViewItemExpanded));
        AddHandler(TreeViewItem.CollapsedEvent,
            new RoutedEventHandler(OnTreeViewItemCollapsed));
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // When the tree is rebuilt (e.g. after a RefreshConflictBuckets call),
        // the TreeView resets SelectedItem to null and every new
        // TreeViewItem starts at its default IsExpanded=false. Re-apply both
        // expansion state and selection AFTER layout — the DP callback fires
        // before WPF realizes containers for the new ItemsSource, so
        // ContainerFromItem returns null for every node until the dispatcher
        // reaches Loaded priority.
        if (d is ConflictFileTree tree)
        {
            tree.PruneStaleCollapsedPaths();
            tree.Dispatcher.BeginInvoke(() =>
            {
                tree.ApplyExpansionState();
                tree.SyncSelectionToTree();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// Drop entries from <see cref="_collapsedFolders"/> that no longer
    /// correspond to any folder in the current tree. Keeps the set bounded
    /// across long sessions where files get resolved and disappear, without
    /// needing an explicit "clear" hook on the control.
    /// </summary>
    private void PruneStaleCollapsedPaths()
    {
        if (_collapsedFolders.Count == 0 || ItemsSource is null) return;
        var alive = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in ItemsSource)
        {
            CollectFolderPaths(root, alive);
        }
        _collapsedFolders.RemoveWhere(path => !alive.Contains(path));
    }

    private static void CollectFolderPaths(ConflictTreeNode node, HashSet<string> sink)
    {
        if (!node.IsFolder) return;
        sink.Add(node.FullPath);
        foreach (var child in node.Children) CollectFolderPaths(child, sink);
    }

    private void OnTreeViewItemExpanded(object sender, RoutedEventArgs e)
    {
        if (_suppressExpandCollapseCapture) return;
        if (e.OriginalSource is TreeViewItem tvi &&
            tvi.DataContext is ConflictTreeNode node &&
            node.IsFolder)
        {
            _collapsedFolders.Remove(node.FullPath);
        }
    }

    private void OnTreeViewItemCollapsed(object sender, RoutedEventArgs e)
    {
        if (_suppressExpandCollapseCapture) return;
        if (e.OriginalSource is TreeViewItem tvi &&
            tvi.DataContext is ConflictTreeNode node &&
            node.IsFolder)
        {
            _collapsedFolders.Add(node.FullPath);
        }
    }

    /// <summary>
    /// After an <see cref="ItemsSource"/> swap, walk the new containers and
    /// apply <see cref="_collapsedFolders"/> so folders the user collapsed
    /// stay collapsed while every other folder opens by default. Runs under
    /// <see cref="_suppressExpandCollapseCapture"/> so the resulting
    /// TreeViewItem.Expanded/Collapsed events don't re-enter the capture path.
    /// </summary>
    private void ApplyExpansionState()
    {
        if (ItemsSource is null) return;
        _suppressExpandCollapseCapture = true;
        try
        {
            foreach (var root in ItemsSource)
            {
                ApplyExpansionToContainer(Tree, root);
            }
        }
        finally { _suppressExpandCollapseCapture = false; }
    }

    private void ApplyExpansionToContainer(ItemsControl parent, ConflictTreeNode node)
    {
        parent.UpdateLayout();
        if (parent.ItemContainerGenerator.ContainerFromItem(node) is not TreeViewItem container) return;
        if (!node.IsFolder) return;
        container.IsExpanded = !_collapsedFolders.Contains(node.FullPath);
        // Recurse for descendant folders so nested folders restore state too.
        container.UpdateLayout();
        foreach (var child in node.Children)
        {
            ApplyExpansionToContainer(container, child);
        }
    }

    private static void OnSelectedFileChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ConflictFileTree tree)
        {
            tree.SyncSelectionToTree();
        }
    }

    private void OnTreeSelectedItemChanged(object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressSelectionSync) return;
        // Only file leaves push into SelectedFile. Folders are organizational.
        if (e.NewValue is ConflictTreeNode node && node.Conflict is not null)
        {
            SelectedFile = node.Conflict;
        }
    }

    /// <summary>
    /// After an <see cref="ItemsSource"/> swap or external
    /// <see cref="SelectedFile"/> change, walk the new tree and select the
    /// matching file leaf (expanding its ancestors so it's visible).
    /// </summary>
    private void SyncSelectionToTree()
    {
        if (ItemsSource is null) return;
        if (SelectedFile is null)
        {
            // Clear any stale highlighting when the selection is reset.
            _suppressSelectionSync = true;
            try { ClearTreeSelection(Tree); }
            finally { _suppressSelectionSync = false; }
            return;
        }

        var target = SelectedFile;
        _suppressSelectionSync = true;
        try
        {
            foreach (var root in ItemsSource)
            {
                if (TryExpandAndSelect(Tree, root, target)) return;
            }
        }
        finally { _suppressSelectionSync = false; }
    }

    private static bool TryExpandAndSelect(ItemsControl parent, ConflictTreeNode node, ConflictInfo target)
    {
        parent.UpdateLayout();
        if (parent.ItemContainerGenerator.ContainerFromItem(node) is not TreeViewItem container)
        {
            return false;
        }
        if (node.Conflict == target)
        {
            container.IsSelected = true;
            container.BringIntoView();
            return true;
        }
        if (node.Children.Count == 0) return false;
        container.IsExpanded = true;
        container.UpdateLayout();
        foreach (var child in node.Children)
        {
            if (TryExpandAndSelect(container, child, target)) return true;
        }
        return false;
    }

    private static void ClearTreeSelection(ItemsControl parent)
    {
        // WPF TreeView has no "clear selection" API; walking and setting
        // IsSelected=false on the currently-selected container is the
        // documented workaround.
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container) continue;
            if (container.IsSelected) container.IsSelected = false;
            ClearTreeSelection(container);
        }
    }
}
