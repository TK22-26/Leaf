using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Leaf.Models;

namespace Leaf.Controls;

/// <summary>
/// A reusable control for displaying file changes (unstaged or staged) with list/tree views.
/// </summary>
public partial class FileChangesSectionControl : UserControl
{
    public static readonly DependencyProperty ContextProperty =
        DependencyProperty.Register(
            nameof(Context),
            typeof(FileChangesSectionContext),
            typeof(FileChangesSectionControl),
            new PropertyMetadata(null, OnContextChanged));

    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(
            nameof(IsExpanded),
            typeof(bool),
            typeof(FileChangesSectionControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty ShowTreeViewProperty =
        DependencyProperty.Register(
            nameof(ShowTreeView),
            typeof(bool),
            typeof(FileChangesSectionControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnShowTreeViewChanged));

    public static readonly DependencyProperty ItemCountProperty =
        DependencyProperty.Register(
            nameof(ItemCount),
            typeof(int),
            typeof(FileChangesSectionControl),
            new PropertyMetadata(0));

    public FileChangesSectionControl()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyTreeVisibility();
    }

    /// <summary>
    /// Context object containing all commands, data sources, and metadata.
    /// </summary>
    public FileChangesSectionContext? Context
    {
        get => (FileChangesSectionContext?)GetValue(ContextProperty);
        set => SetValue(ContextProperty, value);
    }

    /// <summary>
    /// Whether the section is expanded.
    /// </summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>
    /// Whether to show tree view (true) or list view (false).
    /// </summary>
    public bool ShowTreeView
    {
        get => (bool)GetValue(ShowTreeViewProperty);
        set => SetValue(ShowTreeViewProperty, value);
    }

    /// <summary>
    /// Number of items in the files source (for display in header).
    /// </summary>
    public int ItemCount
    {
        get => (int)GetValue(ItemCountProperty);
        set => SetValue(ItemCountProperty, value);
    }

    private static void OnContextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FileChangesSectionControl control)
        {
            control.UpdateItemCount();
        }
    }

    private static void OnShowTreeViewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FileChangesSectionControl control)
        {
            control.ApplyTreeVisibility();
        }
    }

    private void OnTreeToggleChanged(object sender, RoutedEventArgs e)
    {
        ApplyTreeVisibility();
    }

    private void ApplyTreeVisibility()
    {
        if (ListScrollViewer != null)
        {
            ListScrollViewer.Visibility = ShowTreeView ? Visibility.Collapsed : Visibility.Visible;
        }
        if (FileTreeView != null)
        {
            FileTreeView.Visibility = ShowTreeView ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdateItemCount()
    {
        if (Context?.FilesSource is ICollection collection)
        {
            ItemCount = collection.Count;
        }
        else
        {
            ItemCount = 0;
        }
    }

    private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source != null && source is not TreeViewItem)
            source = VisualTreeHelper.GetParent(source);

        if (source is TreeViewItem item)
        {
            item.IsSelected = true;
            item.Focus();
            e.Handled = true;
        }
    }
}
