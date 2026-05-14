using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FluentIcons.Common;
using FluentIcons.Wpf;
using Leaf.Controls.GitGraph;
using Leaf.Models;
using Leaf.Services;
using Leaf.ViewModels;

namespace Leaf.Views;

/// <summary>
/// Interaction logic for GitGraphView.xaml
/// </summary>
public partial class GitGraphView : UserControl
{
    private const double RowHeight = 28.0;
    private readonly DispatcherTimer _tooltipCloseTimer;
    private readonly DispatcherTimer _scrollDebounceTimer;
    private readonly HashSet<ToolTip> _openTooltips = new();
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

        _scrollDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _scrollDebounceTimer.Tick += ScrollDebounceTimer_Tick;

        // Subscribe to expansion changes from the canvas
        if (GraphCanvas != null)
        {
            GraphCanvas.RowExpansionChanged += OnRowExpansionChanged;
            GraphCanvas.BranchCheckoutRequested += OnBranchCheckoutRequested;
        }
    }

    private void OnBranchCheckoutRequested(object? sender, BranchCheckoutRequestedEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is MainViewModel mainViewModel)
        {
            var label = e.Label;
            Log.Info("Checkout", $"OnBranchCheckoutRequested: label.Name={label.Name}, label.TipSha={label.TipSha ?? "NULL"}, e.TipSha={e.TipSha ?? "NULL"}");

            // If this is a remote-only label and we're on the matching local branch, fast-forward
            if (label.IsRemote && !label.IsLocal && label.RemoteName != null)
            {
                if (DataContext is GitGraphViewModel viewModel)
                {
                    var currentBranchName = viewModel.WorkingChanges?.BranchName;
                    if (currentBranchName == label.Name)
                    {
                        mainViewModel.FastForwardBranchLabelAsync(label)
                            .FireAndForget(nameof(mainViewModel.FastForwardBranchLabelAsync), isUserAction: true);
                        return;
                    }
                }
            }

            // Otherwise do regular checkout
            // Use the label's actual TipSha, not the display row's commit SHA
            var branchName = label.IsRemote && !label.IsLocal && label.RemoteName != null
                ? $"{label.RemoteName}/{label.Name}"
                : label.Name;
            var tipShaToUse = label.TipSha ?? e.TipSha ?? string.Empty;
            Log.Info("Checkout", $"Calling CheckoutBranchAsync: branchName={branchName}, tipShaToUse={tipShaToUse}");
            mainViewModel.CheckoutBranchAsync(new BranchInfo
            {
                Name = branchName,
                IsRemote = label.IsRemote,
                RemoteName = label.RemoteName,
                IsCurrent = label.IsCurrent,
                TipSha = tipShaToUse
            }).FireAndForget(nameof(mainViewModel.CheckoutBranchAsync), isUserAction: true);
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

        // Account for working changes offset (stashes are now in Commits)
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
                Log.Info("Checkout", $"GraphCanvas double-click: label.Name={label.Name}, label.TipSha={label.TipSha ?? "NULL"}");

                // If this is a remote-only label (local is at different commit)
                // and we're currently on the matching local branch, fast-forward instead of checkout
                if (label.IsRemote && !label.IsLocal && label.RemoteName != null)
                {
                    var currentBranchName = viewModel.WorkingChanges?.BranchName;
                    if (currentBranchName == label.Name)
                    {
                        // Fast-forward current branch to this remote
                        mainViewModel.FastForwardBranchLabelAsync(label)
                            .FireAndForget(nameof(mainViewModel.FastForwardBranchLabelAsync), isUserAction: true);
                        e.Handled = true;
                        return;
                    }
                }

                // Otherwise do regular checkout
                // Use the label's actual TipSha, not the display row's commit SHA
                var name = label.IsRemote && !label.IsLocal && label.RemoteName != null
                    ? $"{label.RemoteName}/{label.Name}"
                    : label.Name;
                Log.Info("Checkout", $"GraphCanvas calling CheckoutBranchAsync: name={name}, TipSha={label.TipSha ?? "NULL"}");
                mainViewModel.CheckoutBranchAsync(new BranchInfo
                {
                    Name = name,
                    IsRemote = label.IsRemote,
                    RemoteName = label.RemoteName,
                    IsCurrent = label.IsCurrent,
                    TipSha = label.TipSha ?? string.Empty
                }).FireAndForget(nameof(mainViewModel.CheckoutBranchAsync), isUserAction: true);
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

        // Select the commit at this row (stashes are now inline graph nodes)
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

        // If this is a stash pseudo-commit, show stash context menu instead
        if (commit.IsStash)
        {
            var stashMenu = new ContextMenu();

            var popItem = new MenuItem
            {
                Header = "Pop Stash",
                InputGestureText = "Apply and remove",
                Icon = new TextBlock
                {
                    Text = "\uE74C",
                    FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons"),
                    FontSize = 14
                }
            };
            popItem.Click += (_, _) =>
            {
                if (mainViewModel.PopStashCommand.CanExecute(null))
                    mainViewModel.PopStashCommand.Execute(null);
            };
            stashMenu.Items.Add(popItem);

            stashMenu.Items.Add(new Separator());

            var deleteItem = new MenuItem
            {
                Header = "Delete Stash",
                InputGestureText = "Discard",
                Icon = new TextBlock
                {
                    Text = "\uE74D",
                    FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons"),
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(232, 17, 35))
                }
            };
            deleteItem.Click += (_, _) =>
            {
                if (mainViewModel.DeleteStashCommand.CanExecute(null))
                    mainViewModel.DeleteStashCommand.Execute(null);
            };
            stashMenu.Items.Add(deleteItem);

            element.ContextMenu = stashMenu;
            stashMenu.IsOpen = true;
            e.Handled = true;
            return;
        }

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
            Icon = new SymbolIcon { Symbol = Symbol.BranchFork, FontSize = 14 }
        };
        menu.Items.Add(createBranchItem);

        var currentBranchName = mainViewModel.SelectedRepository?.CurrentBranch;
        if (!string.IsNullOrWhiteSpace(currentBranchName)
            && mainViewModel.SelectedRepository?.IsDetachedHead != true)
        {
            var resetMenu = new MenuItem
            {
                Header = $"Reset {currentBranchName} to this commit"
            };
            resetMenu.Items.Add(new MenuItem
            {
                Header = "Soft \u2014 keep all changes staged",
                Command = mainViewModel.ResetCurrentBranchToCommitCommand,
                CommandParameter = new ResetCurrentBranchRequest(commit, GitResetMode.Soft)
            });
            resetMenu.Items.Add(new MenuItem
            {
                Header = "Mixed \u2014 keep changes unstaged",
                Command = mainViewModel.ResetCurrentBranchToCommitCommand,
                CommandParameter = new ResetCurrentBranchRequest(commit, GitResetMode.Mixed)
            });
            resetMenu.Items.Add(new MenuItem
            {
                Header = "Hard \u2014 discard tracked changes",
                Command = mainViewModel.ResetCurrentBranchToCommitCommand,
                CommandParameter = new ResetCurrentBranchRequest(commit, GitResetMode.Hard)
            });
            menu.Items.Add(resetMenu);
        }

        var revertItem = new MenuItem
        {
            Header = "Revert commit",
            Command = mainViewModel.RevertCommitCommand,
            CommandParameter = commit
        };
        menu.Items.Add(revertItem);

        // Cherry-pick commit
        var cherryPickItem = new MenuItem
        {
            Header = "Cherry-pick commit",
            Command = mainViewModel.CherryPickCommitCommand,
            CommandParameter = commit
        };
        menu.Items.Add(cherryPickItem);

        // Interactive rebase from this commit. We don't gate visibility
        // on detached-HEAD here — the command handler routes the user to
        // a clear "rebase already in progress" or initialisation message
        // when the precondition fails, which is more discoverable than
        // a silently missing menu entry.
        var rebaseInteractiveItem = new MenuItem
        {
            Header = "Rebase Interactively from Here…",
            Command = mainViewModel.RebaseInteractivelyFromCommitCommand,
            CommandParameter = commit,
            Icon = new SymbolIcon { Symbol = Symbol.ArrowSwap, FontSize = 14 }
        };
        menu.Items.Add(rebaseInteractiveItem);

        // Patch creation entries. These sit just under the rebase entry
        // because both are "rewrite this commit's worth of work" tools —
        // mentally they group together. Quick-clipboard sits next to the
        // file-write variant for muscle-memory.
        var createPatchItem = new MenuItem
        {
            Header = "Create Patch File…",
            Command = mainViewModel.CreatePatchFromCommitCommand,
            CommandParameter = commit,
            Icon = new SymbolIcon { Symbol = Symbol.Save, FontSize = 14 }
        };
        menu.Items.Add(createPatchItem);

        var copyPatchItem = new MenuItem
        {
            Header = "Copy as Patch",
            Command = mainViewModel.CopyCommitAsPatchCommand,
            CommandParameter = commit,
            Icon = new SymbolIcon { Symbol = Symbol.Copy, FontSize = 14 }
        };
        menu.Items.Add(copyPatchItem);

        // Bisect: pre-fill the right-clicked commit as the known-good
        // ancestor so the user only has to confirm/adjust the bad ref.
        var startBisectItem = new MenuItem
        {
            Header = "Start Bisect (this commit is good)…",
            Command = mainViewModel.StartBisectFromCommitCommand,
            CommandParameter = commit,
            Icon = new SymbolIcon { Symbol = Symbol.Search, FontSize = 14 }
        };
        menu.Items.Add(startBisectItem);

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

        menu.Items.Add(new Separator());

        var findPrItem = new MenuItem
        {
            Header = "Find Pull Request...",
            Command = mainViewModel.FindPullRequestForCommitCommand,
            CommandParameter = commit,
            Icon = new FluentIcons.Wpf.SymbolIcon { Symbol = FluentIcons.Common.Symbol.BranchRequest, FontSize = 14 }
        };
        menu.Items.Add(findPrItem);

        // Merge / rebase branch labels
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
                if (!label.IsCurrent)
                {
                    menu.Items.Add(new MenuItem
                    {
                        Header = $"Rebase current onto {label.FullName}...",
                        Command = mainViewModel.RebaseBranchLabelCommand,
                        CommandParameter = label
                    });
                }
            }
        }

        element.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private async void CommitItem_ToolTipOpening(object sender, ToolTipEventArgs e)
    {
        try
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
        catch (Exception ex)
        {
            // Tooltip opening is a passive user hover — silent log by default.
            AsyncErrorHandler.Handle(ex, nameof(CommitItem_ToolTipOpening), isUserAction: false);
        }
    }

    private async void GraphCanvas_ToolTipOpening(object sender, ToolTipEventArgs e)
    {
        try
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
        catch (Exception ex)
        {
            AsyncErrorHandler.Handle(ex, nameof(GraphCanvas_ToolTipOpening), isUserAction: false);
        }
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

        toolTip.Opened += (_, _) => _openTooltips.Add(toolTip);
        toolTip.Closed += (_, _) => _openTooltips.Remove(toolTip);

        toolTip.MouseEnter += (_, _) =>
        {
            _tooltipCloseTimer.Stop();
        };
        toolTip.MouseLeave += (_, _) =>
        {
            StartTooltipCloseTimer();
        };

        element.ToolTip = toolTip;
        return toolTip;
    }

    private void OnTooltipCloseTimerTick(object? sender, EventArgs e)
    {
        _tooltipCloseTimer.Stop();

        // Close all tooltips where neither tooltip nor target is hovered
        var toClose = _openTooltips
            .Where(tt => !tt.IsMouseOver &&
                         tt.PlacementTarget is FrameworkElement target &&
                         !target.IsMouseOver)
            .ToList();

        foreach (var tooltip in toClose)
        {
            tooltip.IsOpen = false;
        }
    }

    private void ScheduleTooltipClose(FrameworkElement element)
    {
        StartTooltipCloseTimer();
    }

    private void StartTooltipCloseTimer()
    {
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
        try
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
        catch (Exception ex)
        {
            // MouseMove fires continuously — toasting every frame would spam.
            AsyncErrorHandler.Handle(ex, nameof(GraphCanvas_MouseMove), isUserAction: false);
        }
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

        // Don't return commit if hovering over label area (for tooltip purposes)
        if (pos.X < GraphCanvas.LabelAreaWidth)
            return null;

        int row = (int)(pos.Y / RowHeight);
        int rowOffset = viewModel.HasWorkingChanges ? 1 : 0;
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

        var pos = e.GetPosition(GraphCanvas);

        // §5.17 — tag chip right-click takes precedence over a branch
        // label hit-test in the same area (rare overlap, but tag chips
        // are explicit while branch labels share the wider label band).
        var tagName = GraphCanvas.GetTagAt(pos);
        if (tagName is not null
            && Window.GetWindow(this)?.DataContext is MainViewModel mainVm
            && DataContext is GitGraphViewModel graphVm)
        {
            ShowTagContextMenu(tagName, mainVm, graphVm);
            e.Handled = true;
            return;
        }

        var label = GraphCanvas.GetBranchLabelAt(pos);
        if (label == null)
            return;

        if (Window.GetWindow(this)?.DataContext is not MainViewModel mainViewModel)
            return;

        if (DataContext is not GitGraphViewModel)
            return;

        var menu = new ContextMenu();

        // Create BranchInfo for commands that need it
        // Use the label's actual TipSha, not the display row's commit SHA
        var branchName = label.IsRemote && !label.IsLocal && label.RemoteName != null
            ? $"{label.RemoteName}/{label.Name}"
            : label.Name;
        var branchInfo = new BranchInfo
        {
            Name = branchName,
            IsRemote = label.IsRemote,
            RemoteName = label.RemoteName,
            IsCurrent = label.IsCurrent,
            TipSha = label.TipSha ?? string.Empty
        };

        // Checkout (skip if already current)
        if (!label.IsCurrent)
        {
            var checkoutItem = new MenuItem
            {
                Header = $"Checkout {label.Name}",
                Command = mainViewModel.CheckoutBranchCommand,
                CommandParameter = branchInfo
            };
            menu.Items.Add(checkoutItem);
        }

        // Merge into current
        var mergeItem = new MenuItem
        {
            Header = $"Merge {label.FullName} into current",
            Command = mainViewModel.MergeBranchLabelCommand,
            CommandParameter = label
        };
        menu.Items.Add(mergeItem);

        // Rebase current onto this label. Disabled when the label IS the
        // current branch — rebasing onto self is a no-op.
        if (!label.IsCurrent)
        {
            var rebaseItem = new MenuItem
            {
                Header = $"Rebase current onto {label.FullName}...",
                Command = mainViewModel.RebaseBranchLabelCommand,
                CommandParameter = label
            };
            menu.Items.Add(rebaseItem);
        }

        if (label.IsLocal && !label.IsCurrent)
        {
            var createPullRequestItem = new MenuItem
            {
                Header = $"Create PR into {label.Name}...",
                Command = mainViewModel.OpenCreatePullRequestCommand,
                CommandParameter = new CreatePullRequestRequest(
                    SourceBranch: mainViewModel.SelectedRepository?.CurrentBranch,
                    TargetBranch: label.Name)
            };
            menu.Items.Add(createPullRequestItem);
        }

        // Create branch here
        var createBranchItem = new MenuItem
        {
            Header = "Create branch here...",
            Command = mainViewModel.CreateBranchAtBranchCommand,
            CommandParameter = branchInfo
        };
        menu.Items.Add(createBranchItem);

        menu.Items.Add(new Separator());

        // Copy branch name
        var copyItem = new MenuItem
        {
            Header = "Copy branch name",
            Command = mainViewModel.CopyBranchNameCommand,
            CommandParameter = branchInfo
        };
        menu.Items.Add(copyItem);

        menu.Items.Add(new Separator());

        // §5.14 — Branch colour overrides. Resolved against the active
        // GitGraphViewModel's IBranchColorService; the menu items are
        // disabled when no colour service is bound (no repo loaded)
        // rather than hidden, so the existence of the feature is
        // discoverable from any branch right-click.
        var colorService = (DataContext as GitGraphViewModel)?.BranchColorService;
        var normalizedBranchName = label.Name; // service does its own remote-prefix normalisation
        var hasOverride = colorService?.HasOverride(normalizedBranchName) ?? false;
        var hasAnyOverrides = colorService?.HasAnyOverrides ?? false;

        var changeColorItem = new MenuItem
        {
            Header = "Change colour…",
            IsEnabled = colorService != null,
            Icon = new SymbolIcon { Symbol = Symbol.Color, FontSize = 14 },
        };
        changeColorItem.Click += (_, _) => OpenBranchColorPicker(label);
        menu.Items.Add(changeColorItem);

        var resetItem = new MenuItem
        {
            Header = "Reset to auto",
            IsEnabled = hasOverride,
            ToolTip = hasOverride
                ? null
                : "This branch has no override — its colour already comes from the active palette.",
        };
        resetItem.Click += (_, _) => colorService?.ClearOverride(normalizedBranchName);
        menu.Items.Add(resetItem);

        var resetAllItem = new MenuItem
        {
            Header = "Reset all branch colours…",
            IsEnabled = hasAnyOverrides,
            ToolTip = hasAnyOverrides
                ? "Remove every per-branch colour override on this repository."
                : "No colour overrides set on this repository.",
        };
        resetAllItem.Click += (_, _) => ConfirmAndResetAllBranchColors(colorService);
        menu.Items.Add(resetAllItem);

        menu.Items.Add(new Separator());

        // Delete branch
        var deleteItem = new MenuItem
        {
            Header = "Delete branch",
            Command = mainViewModel.DeleteBranchLabelCommand,
            CommandParameter = label,
            Foreground = new SolidColorBrush(Color.FromRgb(232, 89, 89))
        };
        menu.Items.Add(deleteItem);

        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>
    /// Open the §5.14 colour picker for the right-clicked branch label.
    /// Pre-fills the picker with the branch's currently-resolved colour
    /// (override or palette-derived) and routes the result back through
    /// the active <see cref="IBranchColorService"/>.
    /// </summary>
    private void OpenBranchColorPicker(BranchLabel label)
    {
        if (DataContext is not GitGraphViewModel viewModel) return;
        var service = viewModel.BranchColorService;
        if (service is null) return;

        var initial = service.GetColor(label.Name);
        var dialog = new Branch.BranchColorPickerDialog(label.Name, initial, service.ActivePalette)
        {
            Owner = Window.GetWindow(this),
        };

        if (dialog.ShowDialog() != true) return;

        switch (dialog.Result)
        {
            case Branch.BranchColorPickerDialog.PickerResult.OverrideSet:
                service.SetOverride(label.Name, dialog.SelectedColor);
                break;
            case Branch.BranchColorPickerDialog.PickerResult.ResetToAuto:
                service.ClearOverride(label.Name);
                break;
            case Branch.BranchColorPickerDialog.PickerResult.Cancelled:
                // User dismissed — leave existing state alone.
                break;
        }
    }

    /// <summary>
    /// Confirm before wiping every branch override on the repo. The
    /// "Reset all" affordance is destructive enough that an accidental
    /// click in a busy graph context should be recoverable, so a yes/no
    /// dialog gates the call.
    /// </summary>
    private void ConfirmAndResetAllBranchColors(IBranchColorService? service)
    {
        if (service is null) return;
        var owner = Window.GetWindow(this);
        var result = FluentMessageBox.Show(
            owner,
            "Remove every per-branch colour override on this repository?\n\n" +
            "Branches will go back to using the active palette.",
            "Reset all branch colours",
            MessageBoxButton.YesNo,
            FluentMessageBoxIcon.Warning);
        if (result == MessageBoxResult.Yes)
            service.ClearAllOverrides();
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

    private void ScrollDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _scrollDebounceTimer.Stop();
        if (DataContext is GitGraphViewModel viewModel &&
            viewModel.LoadMoreCommitsCommand.CanExecute(null))
        {
            viewModel.LoadMoreCommitsCommand.Execute(null);
        }
    }

    private void MainScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Don't trigger on horizontal scrolls or when viewport is larger than content
        if (e.ExtentHeight <= e.ViewportHeight)
            return;

        double scrollPercent = e.VerticalOffset / (e.ExtentHeight - e.ViewportHeight);

        // When scrolled past 65%, trigger load more (with debounce)
        // Lower threshold prefetches earlier for smoother experience
        if (scrollPercent > 0.65)
        {
            // Debounce: stop and restart timer on each scroll event
            _scrollDebounceTimer.Stop();
            _scrollDebounceTimer.Start();
        }
    }

    /// <summary>
    /// §5.17 — surface the tag context menu for the chip the user
    /// right-clicked. Resolves <see cref="TagInfo"/> via the graph VM's
    /// TagsByName lookup; falls back to a name-only menu when the
    /// lookup hasn't been populated (very brief window after panel open).
    /// </summary>
    private void ShowTagContextMenu(string tagName, MainViewModel mainVm, GitGraphViewModel graphVm)
    {
        TagInfo? tag = null;
        graphVm.TagsByName?.TryGetValue(tagName, out tag);

        var menu = new ContextMenu();

        // Header line: tag name + a "signed/annotated/lightweight" pill so
        // users see what they're acting on without re-reading the chip.
        var header = new MenuItem
        {
            Header = tag is null
                ? tagName
                : $"{tag.Name}  ·  {(tag.IsSigned ? "signed" : tag.IsAnnotated ? "annotated" : "lightweight")}",
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold,
        };
        menu.Items.Add(header);
        menu.Items.Add(new Separator());

        var checkoutItem = new MenuItem
        {
            Header = $"Checkout {tagName} (detached HEAD)",
            Command = mainVm.CheckoutTagCommand,
            CommandParameter = tag,
            IsEnabled = tag is not null,
            Icon = new SymbolIcon { Symbol = Symbol.ArrowDownload, FontSize = 14 },
        };
        menu.Items.Add(checkoutItem);

        var pushItem = new MenuItem
        {
            Header = "Push tag to origin",
            Command = mainVm.PushTagCommand,
            CommandParameter = tag,
            IsEnabled = tag is not null,
            Icon = new SymbolIcon { Symbol = Symbol.ArrowUpload, FontSize = 14 },
        };
        menu.Items.Add(pushItem);

        var copyItem = new MenuItem
        {
            Header = "Copy tag name",
            Command = mainVm.CopyTagNameCommand,
            CommandParameter = tag,
            IsEnabled = tag is not null,
            Icon = new SymbolIcon { Symbol = Symbol.Copy, FontSize = 14 },
        };
        menu.Items.Add(copyItem);

        menu.Items.Add(new Separator());

        var deleteItem = new MenuItem
        {
            Header = "Delete tag…",
            Command = mainVm.DeleteTagCommand,
            CommandParameter = tag,
            IsEnabled = tag is not null,
            Foreground = new SolidColorBrush(Color.FromRgb(232, 89, 89)),
        };
        menu.Items.Add(deleteItem);

        menu.IsOpen = true;
    }
}
