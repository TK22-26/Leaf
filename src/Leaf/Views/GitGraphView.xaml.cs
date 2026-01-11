using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        // Account for working changes row offset
        int rowOffset = viewModel.HasWorkingChanges ? 1 : 0;

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

    private void GraphCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not GitGraphViewModel viewModel)
            return;

        var pos = e.GetPosition(GraphCanvas);
        int row = (int)(pos.Y / RowHeight);

        // Handle working changes row click
        if (viewModel.HasWorkingChanges && row == 0)
        {
            viewModel.SelectWorkingChanges();
            return;
        }

        // Adjust for working changes offset
        if (viewModel.HasWorkingChanges)
        {
            row -= 1;
        }

        // Select the commit at this row
        if (row >= 0 && row < viewModel.Commits.Count)
        {
            viewModel.SelectCommit(viewModel.Commits[row]);
        }
    }
}
