using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Leaf.Controls;
using Leaf.Models;
using Leaf.ViewModels;

namespace Leaf.Views;

/// <summary>
/// Interaction logic for GitGraphView.xaml
/// </summary>
public partial class GitGraphView : UserControl
{
    private const double RowHeight = 28.0;

    public GitGraphView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Subscribe to expansion changes from the canvas
        if (GraphCanvas != null)
        {
            GraphCanvas.RowExpansionChanged += OnRowExpansionChanged;
            GraphCanvas.BranchCheckoutRequested += OnBranchCheckoutRequested;
        }
    }

    private void OnBranchCheckoutRequested(object? sender, BranchLabel label)
    {
        if (Window.GetWindow(this)?.DataContext is MainViewModel mainViewModel)
        {
            // If this is a remote-only label and we're on the matching local branch, fast-forward
            if (label.IsRemote && !label.IsLocal && label.RemoteName != null)
            {
                if (DataContext is GitGraphViewModel viewModel)
                {
                    var currentBranchName = viewModel.WorkingChanges?.BranchName;
                    if (currentBranchName == label.Name)
                    {
                        _ = mainViewModel.FastForwardBranchLabelAsync(label);
                        return;
                    }
                }
            }

            // Otherwise do regular checkout
            var branchName = label.IsRemote && !label.IsLocal && label.RemoteName != null
                ? $"{label.RemoteName}/{label.Name}"
                : label.Name;
            _ = mainViewModel.CheckoutBranchAsync(new BranchInfo
            {
                Name = branchName,
                IsRemote = label.IsRemote,
                RemoteName = label.RemoteName,
                IsCurrent = label.IsCurrent
            });
        }
    }

    private void OnRowExpansionChanged(object? sender, RowExpansionChangedEventArgs e)
    {
        // Canvas handles expansion as overlay - nothing to sync
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Unsubscribe from old ViewModel
        if (e.OldValue is GitGraphViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        // Subscribe to new ViewModel
        if (e.NewValue is GitGraphViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When SelectedCommit changes, scroll to it
        if (e.PropertyName == nameof(GitGraphViewModel.SelectedCommit))
        {
            ScrollToSelectedCommit();
        }
        // When working changes selection changes, update highlight
        else if (e.PropertyName == nameof(GitGraphViewModel.IsWorkingChangesSelected))
        {
            UpdateWorkingChangesHighlight();
        }
    }

    private void UpdateWorkingChangesHighlight()
    {
        if (DataContext is GitGraphViewModel viewModel && WorkingChangesHighlight != null)
        {
            WorkingChangesHighlight.Background = viewModel.IsWorkingChangesSelected
                ? (System.Windows.Media.Brush)FindResource("LeafAccentSelectedBrush")
                : System.Windows.Media.Brushes.Transparent;
        }
    }

    private void ScrollToSelectedCommit()
    {
        if (DataContext is not GitGraphViewModel viewModel || viewModel.SelectedCommit == null)
            return;

        // Find the index of the selected commit
        int index = -1;
        for (int i = 0; i < viewModel.Commits.Count; i++)
        {
            if (viewModel.Commits[i].Sha == viewModel.SelectedCommit.Sha)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return;

        // Account for working changes and stash rows offset
        int rowOffset = viewModel.HasWorkingChanges ? 1 : 0;
        rowOffset += viewModel.Stashes.Count;

        // Calculate the Y position of this commit
        double targetY = (index + rowOffset) * RowHeight;

        // Get the current scroll position and viewport size
        double viewportHeight = MainScrollViewer.ViewportHeight;
        double currentOffset = MainScrollViewer.VerticalOffset;

        // Only scroll if the commit is outside the visible area
        // Add some padding so it's not right at the edge
        double padding = RowHeight * 2;

        if (targetY < currentOffset + padding)
        {
            // Commit is above visible area - scroll up
            MainScrollViewer.ScrollToVerticalOffset(Math.Max(0, targetY - padding));
        }
        else if (targetY > currentOffset + viewportHeight - padding - RowHeight)
        {
            // Commit is below visible area - scroll down
            MainScrollViewer.ScrollToVerticalOffset(targetY - viewportHeight + RowHeight + padding);
        }
        // Otherwise commit is already visible, no need to scroll
    }

    private void CommitItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is CommitInfo commit)
        {
            if (DataContext is GitGraphViewModel viewModel)
            {
                viewModel.SelectCommit(commit);
            }
        }
    }

    private void CommitItem_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is CommitInfo commit)
        {
            if (DataContext is GitGraphViewModel viewModel)
            {
                viewModel.HoveredSha = commit.Sha;
            }
        }
    }

    private void CommitItem_MouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is GitGraphViewModel viewModel)
        {
            viewModel.HoveredSha = null;
        }
    }

    private void WorkingChangesRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is GitGraphViewModel viewModel)
        {
            viewModel.SelectWorkingChanges();
        }
    }

    private void WorkingChangesRow_MouseEnter(object sender, MouseEventArgs e)
    {
        // Update visual state for hover (using same green accent as regular commits)
        if (WorkingChangesHighlight != null)
        {
            WorkingChangesHighlight.Background = (System.Windows.Media.Brush)FindResource("LeafAccentHoverBrush");
        }

        // Update canvas hover state
        if (GraphCanvas != null)
        {
            GraphCanvas.IsWorkingChangesHovered = true;
        }
    }

    private void WorkingChangesRow_MouseLeave(object sender, MouseEventArgs e)
    {
        // Reset visual state (using same green accent as regular commits)
        if (WorkingChangesHighlight != null && DataContext is GitGraphViewModel viewModel)
        {
            WorkingChangesHighlight.Background = viewModel.IsWorkingChangesSelected
                ? (System.Windows.Media.Brush)FindResource("LeafAccentSelectedBrush")
                : System.Windows.Media.Brushes.Transparent;
        }

        // Update canvas hover state
        if (GraphCanvas != null)
        {
            GraphCanvas.IsWorkingChangesHovered = false;
        }
    }

    private void StashItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is StashInfo stash)
        {
            if (DataContext is GitGraphViewModel viewModel)
            {
                viewModel.SelectStash(stash);
            }
        }
    }

    private void StashItem_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is StashInfo stash)
        {
            if (DataContext is GitGraphViewModel viewModel)
            {
                viewModel.HoveredSha = stash.Sha;
            }

            // Update canvas hover state
            if (GraphCanvas != null)
            {
                GraphCanvas.HoveredStashIndex = stash.Index;
            }
        }
    }

    private void StashItem_MouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is GitGraphViewModel viewModel)
        {
            viewModel.HoveredSha = null;
        }

        // Update canvas hover state
        if (GraphCanvas != null)
        {
            GraphCanvas.HoveredStashIndex = -1;
        }
    }

    private void PopStashMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // Get the MainViewModel from the Window's DataContext
        if (Window.GetWindow(this)?.DataContext is ViewModels.MainViewModel mainViewModel)
        {
            if (mainViewModel.PopStashCommand.CanExecute(null))
            {
                mainViewModel.PopStashCommand.Execute(null);
            }
        }
    }

    private void DeleteStashMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // Get the MainViewModel from the Window's DataContext
        if (Window.GetWindow(this)?.DataContext is ViewModels.MainViewModel mainViewModel)
        {
            if (mainViewModel.DeleteStashCommand.CanExecute(null))
            {
                mainViewModel.DeleteStashCommand.Execute(null);
            }
        }
    }

    private void GraphCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not GitGraphViewModel viewModel)
            return;

        var pos = e.GetPosition(GraphCanvas);
        if (GraphCanvas != null && e.ClickCount == 2)
        {
            var label = GraphCanvas.GetBranchLabelAt(pos);
            if (label != null && Window.GetWindow(this)?.DataContext is MainViewModel mainViewModel)
            {
                // If this is a remote-only label (local is at different commit)
                // and we're currently on the matching local branch, fast-forward instead of checkout
                if (label.IsRemote && !label.IsLocal && label.RemoteName != null)
                {
                    var currentBranchName = viewModel.WorkingChanges?.BranchName;
                    if (currentBranchName == label.Name)
                    {
                        // Fast-forward current branch to this remote
                        _ = mainViewModel.FastForwardBranchLabelAsync(label);
                        e.Handled = true;
                        return;
                    }
                }

                // Otherwise do regular checkout
                var name = label.IsRemote && !label.IsLocal && label.RemoteName != null
                    ? $"{label.RemoteName}/{label.Name}"
                    : label.Name;
                _ = mainViewModel.CheckoutBranchAsync(new BranchInfo
                {
                    Name = name,
                    IsRemote = label.IsRemote,
                    RemoteName = label.RemoteName,
                    IsCurrent = label.IsCurrent
                });
                e.Handled = true;
                return;
            }
        }
        int row = (int)(pos.Y / RowHeight);
        int currentRow = 0;

        // Handle working changes row click
        if (viewModel.HasWorkingChanges)
        {
            if (row == currentRow)
            {
                viewModel.SelectWorkingChanges();
                return;
            }
            currentRow++;
        }

        // Handle stash row clicks
        if (viewModel.HasStashes)
        {
            int stashIndex = row - currentRow;
            if (stashIndex >= 0 && stashIndex < viewModel.Stashes.Count)
            {
                viewModel.SelectStash(viewModel.Stashes[stashIndex]);
                return;
            }
            currentRow += viewModel.Stashes.Count;
        }

        // Select the commit at this row
        int commitIndex = row - currentRow;
        if (commitIndex >= 0 && commitIndex < viewModel.Commits.Count)
        {
            viewModel.SelectCommit(viewModel.Commits[commitIndex]);
        }
    }

    private void CommitItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not CommitInfo commit)
            return;

        if (Window.GetWindow(this)?.DataContext is not MainViewModel mainViewModel)
            return;

        var menu = new ContextMenu();

        // Checkout commit option
        var checkoutItem = new MenuItem
        {
            Header = $"Checkout {commit.ShortSha}",
            Command = mainViewModel.CheckoutCommitCommand,
            CommandParameter = commit,
            Icon = new TextBlock
            {
                Text = "\uE8AB",
                FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons"),
                FontSize = 14
            }
        };
        menu.Items.Add(checkoutItem);

        // Merge branch labels
        if (commit.BranchLabels.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (var label in commit.BranchLabels)
            {
                menu.Items.Add(new MenuItem
                {
                    Header = $"Merge {label.FullName} into current",
                    Command = mainViewModel.MergeBranchLabelCommand,
                    CommandParameter = label
                });
            }
        }

        element.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void GraphCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (GraphCanvas == null)
            return;

        var label = GraphCanvas.GetBranchLabelAt(e.GetPosition(GraphCanvas));
        if (label == null)
            return;

        if (Window.GetWindow(this)?.DataContext is not MainViewModel mainViewModel)
            return;

        var menuItem = new MenuItem
        {
            Header = $"Merge {label.FullName} into current",
            Command = mainViewModel.MergeBranchLabelCommand,
            CommandParameter = label
        };

        var menu = new ContextMenu();
        menu.Items.Add(menuItem);
        menu.IsOpen = true;
        e.Handled = true;
    }

}
