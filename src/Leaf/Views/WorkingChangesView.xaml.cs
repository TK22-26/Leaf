using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Leaf.Models;
using Leaf.ViewModels;

namespace Leaf.Views;

/// <summary>
/// Interaction logic for WorkingChangesView.xaml
/// </summary>
public partial class WorkingChangesView : UserControl
{
    public WorkingChangesView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ApplyTreeVisibility(UnstagedTreeToggle, UnstagedListScrollViewer, UnstagedTreeView);
            ApplyTreeVisibility(StagedTreeToggle, StagedListScrollViewer, StagedTreeView);
        };
    }

    private void UnstagedFile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is FileStatusInfo file)
        {
            // Get MainViewModel from Window
            if (Window.GetWindow(this)?.DataContext is MainViewModel mainVm)
            {
                _ = mainVm.ShowUnstagedFileDiffAsync(file);
            }
        }
    }

    private void StagedFile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is FileStatusInfo file)
        {
            // Get MainViewModel from Window
            if (Window.GetWindow(this)?.DataContext is MainViewModel mainVm)
            {
                _ = mainVm.ShowStagedFileDiffAsync(file);
            }
        }
    }

    private void FileItem_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Ensure the item is selected when right-clicking to open context menu
        // The context menu will open automatically
        e.Handled = false;
    }

    private void UnstagedTreeToggle_Changed(object sender, RoutedEventArgs e)
    {
        ApplyTreeVisibility(UnstagedTreeToggle, UnstagedListScrollViewer, UnstagedTreeView);
        LogTreeState("Unstaged", UnstagedListScrollViewer, UnstagedTreeView, UnstagedTreeToggle);
    }

    private void StagedTreeToggle_Changed(object sender, RoutedEventArgs e)
    {
        ApplyTreeVisibility(StagedTreeToggle, StagedListScrollViewer, StagedTreeView);
        LogTreeState("Staged", StagedListScrollViewer, StagedTreeView, StagedTreeToggle);
    }

    private static void LogTreeState(string label, ScrollViewer? list, TreeView? tree, ToggleButton? toggle)
    {
        var listVis = list?.Visibility.ToString() ?? "null";
        var treeVis = tree?.Visibility.ToString() ?? "null";
        var toggleState = toggle?.IsChecked?.ToString() ?? "null";
        Debug.WriteLine($"[WorkingChanges] {label} Toggle={toggleState} List={listVis} Tree={treeVis}");
    }

    private static void ApplyTreeVisibility(ToggleButton? toggle, ScrollViewer? list, TreeView? tree)
    {
        bool showTree = toggle?.IsChecked == true;
        if (list != null)
        {
            list.Visibility = showTree ? Visibility.Collapsed : Visibility.Visible;
        }
        if (tree != null)
        {
            tree.Visibility = showTree ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
