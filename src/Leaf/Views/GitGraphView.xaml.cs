using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
    private readonly DispatcherTimer _tooltipCloseTimer;
    private ToolTip? _pendingTooltipClose;
    private FrameworkElement? _pendingTooltipTarget;
    private string? _graphTooltipSha;

    public GitGraphView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        _tooltipCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _tooltipCloseTimer.Tick += OnTooltipCloseTimerTick;

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

        if (sender is FrameworkElement element)
        {
            ScheduleTooltipClose(element);
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

        // Create branch here
        var createBranchItem = new MenuItem
        {
            Header = "Create branch here...",
            Command = mainViewModel.CreateBranchAtCommitCommand,
            CommandParameter = commit,
            Icon = new TextBlock
            {
                Text = "\uE8B7",
                FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons"),
                FontSize = 14
            }
        };
        menu.Items.Add(createBranchItem);

        // Cherry-pick commit
        var cherryPickItem = new MenuItem
        {
            Header = "Cherry-pick commit",
            Command = mainViewModel.CherryPickCommitCommand,
            CommandParameter = commit
        };
        menu.Items.Add(cherryPickItem);

        menu.Items.Add(new Separator());

        var copyShaItem = new MenuItem
        {
            Header = "Copy SHA",
            Command = mainViewModel.CopyCommitShaCommand,
            CommandParameter = commit,
            Icon = new TextBlock
            {
                Text = "\uE8C8",
                FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons"),
                FontSize = 14
            }
        };
        menu.Items.Add(copyShaItem);

        var compareItem = new MenuItem
        {
            Header = "Compare with working directory",
            Command = mainViewModel.CompareCommitToWorkingDirectoryCommand,
            CommandParameter = commit
        };
        menu.Items.Add(compareItem);

        var createTagItem = new MenuItem
        {
            Header = "Create tag here...",
            Command = mainViewModel.CreateTagAtCommitCommand,
            CommandParameter = commit
        };
        menu.Items.Add(createTagItem);

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

    private async void CommitItem_ToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not CommitInfo commit)
            return;

        var toolTip = GetOrCreateTooltip(element);
        if (!commit.IsMerge)
        {
            toolTip.Content = null;
            e.Handled = true;
            return;
        }

        await ShowMergeTooltipAsync(element, commit);
    }

    private async void GraphCanvas_ToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        if (DataContext is not GitGraphViewModel viewModel)
            return;

        var toolTip = GetOrCreateTooltip(element);
        var hoveredCommit = GetCommitAtMousePosition(viewModel);
        if (hoveredCommit == null)
        {
            toolTip.Content = null;
            e.Handled = true;
            return;
        }

        if (!hoveredCommit.IsMerge)
        {
            toolTip.Content = null;
            e.Handled = true;
            return;
        }

        _graphTooltipSha = hoveredCommit.Sha;
        await ShowMergeTooltipAsync(element, hoveredCommit);
    }

    private async Task ShowMergeTooltipAsync(FrameworkElement element, CommitInfo commit)
    {
        if (DataContext is not GitGraphViewModel viewModel)
            return;

        if (viewModel.TryGetMergeTooltip(commit.Sha, out var cachedTooltip) && cachedTooltip != null)
        {
            element.ToolTip = BuildMergeTooltip(cachedTooltip);
            return;
        }

        var toolTip = GetOrCreateTooltip(element);
        toolTip.Content = BuildTooltipLoading();

        var tooltipViewModel = await viewModel.GetMergeTooltipAsync(commit);
        if (tooltipViewModel == null)
        {
            toolTip.Content = null;
            return;
        }

        toolTip.Content = new MergeCommitTooltipView
        {
            DataContext = tooltipViewModel
        };
        toolTip.IsOpen = true;
    }

    private static MergeCommitTooltipView BuildMergeTooltip(MergeCommitTooltipViewModel tooltipViewModel)
    {
        return new MergeCommitTooltipView
        {
            DataContext = tooltipViewModel
        };
    }

    private static TextBlock BuildTooltipLoading()
    {
        var brush = Application.Current?.TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Brushes.Gray;
        return new TextBlock
        {
            Text = "Loading merged commits...",
            Margin = new Thickness(8, 4, 8, 4),
            Foreground = brush
        };
    }

    private ToolTip GetOrCreateTooltip(FrameworkElement element)
    {
        if (element.ToolTip is ToolTip existing)
            return existing;

        var toolTip = new ToolTip
        {
            PlacementTarget = element,
            StaysOpen = true
        };
        toolTip.MouseEnter += (_, _) =>
        {
            if (_tooltipCloseTimer.IsEnabled)
            {
                _tooltipCloseTimer.Stop();
            }
            _pendingTooltipClose = null;
            _pendingTooltipTarget = null;
        };
        toolTip.MouseLeave += (_, _) =>
        {
            if (toolTip.PlacementTarget is FrameworkElement target)
            {
                ScheduleTooltipClose(target);
            }
        };
        element.ToolTip = toolTip;
        return toolTip;
    }

    private void OnTooltipCloseTimerTick(object? sender, EventArgs e)
    {
        if (_pendingTooltipClose == null)
        {
            _tooltipCloseTimer.Stop();
            return;
        }

        bool targetHovered = _pendingTooltipTarget?.IsMouseOver ?? false;
        if (!_pendingTooltipClose.IsMouseOver && !targetHovered)
        {
            _pendingTooltipClose.IsOpen = false;
            _pendingTooltipClose = null;
            _pendingTooltipTarget = null;
            _tooltipCloseTimer.Stop();
        }
    }

    private void ScheduleTooltipClose(FrameworkElement element)
    {
        if (element.ToolTip is not ToolTip toolTip)
            return;

        _pendingTooltipClose = toolTip;
        _pendingTooltipTarget = element;
        if (!_tooltipCloseTimer.IsEnabled)
        {
            _tooltipCloseTimer.Start();
        }
    }

    private void GraphCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            ScheduleTooltipClose(element);
        }
        _graphTooltipSha = null;
    }

    private async void GraphCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        if (DataContext is not GitGraphViewModel viewModel)
            return;

        var commit = GetCommitAtMousePosition(viewModel);
        if (commit == null || !commit.IsMerge)
        {
            CloseTooltip(element);
            _graphTooltipSha = null;
            return;
        }

        if (string.Equals(_graphTooltipSha, commit.Sha, StringComparison.OrdinalIgnoreCase))
            return;

        _graphTooltipSha = commit.Sha;
        await ShowMergeTooltipAsync(element, commit);
    }

    private void CloseTooltip(FrameworkElement element)
    {
        if (element.ToolTip is not ToolTip toolTip)
            return;

        toolTip.IsOpen = false;
        toolTip.Content = null;
    }

    private CommitInfo? GetCommitAtMousePosition(GitGraphViewModel viewModel)
    {
        if (GraphCanvas?.Nodes == null)
            return null;

        var pos = Mouse.GetPosition(GraphCanvas);
        int row = (int)(pos.Y / RowHeight);
        int rowOffset = (viewModel.HasWorkingChanges ? 1 : 0) + viewModel.Stashes.Count;
        int nodeIndex = row - rowOffset;

        if (nodeIndex < 0 || nodeIndex >= GraphCanvas.Nodes.Count)
            return null;

        var sha = GraphCanvas.Nodes[nodeIndex].Sha;
        return viewModel.Commits.FirstOrDefault(c => c.Sha == sha);
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

    private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (MainScrollViewer == null)
            return;

        int lines = SystemParameters.WheelScrollLines;
        if (lines <= 0)
            lines = 3;

        double multiplier = lines * 1.5;
        double delta = -e.Delta / 120.0 * RowHeight * multiplier;
        MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset + delta);
        e.Handled = true;
    }

}
