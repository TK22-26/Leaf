using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentIcons.Common;
using FluentIcons.Wpf;
using Leaf.Models;
using Leaf.ViewModels;

namespace Leaf.Views;

/// <summary>
/// Code-behind for PullRequestDetailView.
/// </summary>
public partial class PullRequestDetailView : UserControl
{
    public PullRequestDetailView()
    {
        InitializeComponent();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is MainViewModel mainVm)
        {
            mainVm.ClosePullRequestViewCommand.Execute(null);
        }
    }

    private void FileItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PullRequestFileInfo file }
            && DataContext is PullRequestDetailViewModel vm)
        {
            vm.SelectFileCommand.Execute(file);
        }
    }

    private void CheckItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PullRequestStatusCheckInfo check }
            && DataContext is PullRequestDetailViewModel vm)
        {
            vm.OpenCheckUrlCommand.Execute(check);
        }
    }

    private void ApprovePrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PullRequestDetailViewModel vm) return;
        vm.SubmitReviewCommand.Execute(PullRequestReviewState.Approved);
    }

    private void ApproveMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PullRequestDetailViewModel vm) return;

        var menu = new ContextMenu();

        var approveItem = new MenuItem
        {
            Header = "Approve",
            Icon = CreateMenuIcon(Symbol.CheckmarkCircle, "#58C472")
        };
        approveItem.Click += (_, _) => vm.SubmitReviewCommand.Execute(PullRequestReviewState.Approved);
        menu.Items.Add(approveItem);

        var commentItem = new MenuItem
        {
            Header = "Approve with comment",
            Icon = CreateMenuIcon(Symbol.Chat, "#58C472")
        };
        commentItem.Click += (_, _) => vm.SubmitReviewCommand.Execute(PullRequestReviewState.Commented);
        menu.Items.Add(commentItem);

        var waitItem = new MenuItem
        {
            Header = "Wait for author",
            Icon = CreateMenuIcon(Symbol.Clock, "#E3A33B")
        };
        waitItem.Click += (_, _) => vm.SubmitReviewCommand.Execute(PullRequestReviewState.Pending);
        menu.Items.Add(waitItem);

        var rejectItem = new MenuItem
        {
            Header = "Reject",
            Icon = CreateMenuIcon(Symbol.DismissCircle, "#E56767")
        };
        rejectItem.Click += (_, _) => vm.SubmitReviewCommand.Execute(PullRequestReviewState.ChangesRequested);
        menu.Items.Add(rejectItem);

        if (vm.SupportsNeutralReviewFeedback)
        {
            menu.Items.Add(new Separator());

            var resetItem = new MenuItem
            {
                Header = "Reset feedback",
                Icon = CreateMenuIcon(Symbol.Circle, "#C7C9CF")
            };
            resetItem.Click += (_, _) => vm.SubmitReviewCommand.Execute(PullRequestReviewState.Pending);
            menu.Items.Add(resetItem);
        }

        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
    }

    private void AddReviewerButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PullRequestDetailViewModel vm) return;

        if (!vm.SupportsRequiredReviewers)
        {
            vm.OpenOptionalReviewerEditorCommand.Execute(null);
            return;
        }

        var menu = new ContextMenu();

        var addRequiredItem = new MenuItem
        {
            Header = "Add required reviewer",
            Icon = CreateMenuIcon(Symbol.PersonAdd, "#D29922")
        };
        addRequiredItem.Click += (_, _) => vm.OpenRequiredReviewerEditorCommand.Execute(null);
        menu.Items.Add(addRequiredItem);

        var addOptionalItem = new MenuItem
        {
            Header = "Add optional reviewer",
            Icon = CreateMenuIcon(Symbol.PersonAdd, "#58C472")
        };
        addOptionalItem.Click += (_, _) => vm.OpenOptionalReviewerEditorCommand.Execute(null);
        menu.Items.Add(addOptionalItem);

        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
    }

    private void ReviewerResult_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ReviewerInfo reviewer } element
            && DataContext is PullRequestDetailViewModel vm)
        {
            if (string.Equals(element.Tag as string, "Required", StringComparison.OrdinalIgnoreCase))
            {
                vm.AddRequiredReviewerCommand.Execute(reviewer);
            }
            else
            {
                vm.AddOptionalReviewerCommand.Execute(reviewer);
            }
        }
    }

    private void DescriptionDocument_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var parentScrollViewer = FindAncestor<ScrollViewer>(sender as DependencyObject);
        if (parentScrollViewer == null)
            return;

        e.Handled = true;

        var forwardedEvent = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = sender
        };

        parentScrollViewer.RaiseEvent(forwardedEvent);
    }

    private void CompleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PullRequestDetailViewModel vm) return;

        var menu = new ContextMenu();

        var mergeItem = new MenuItem { Header = "Merge commit" };
        mergeItem.Click += (_, _) => vm.MergeCommand.Execute(MergeMethod.Merge);
        menu.Items.Add(mergeItem);

        var squashItem = new MenuItem { Header = "Squash and merge" };
        squashItem.Click += (_, _) => vm.MergeCommand.Execute(MergeMethod.Squash);
        menu.Items.Add(squashItem);

        var rebaseItem = new MenuItem { Header = "Rebase and merge" };
        rebaseItem.Click += (_, _) => vm.MergeCommand.Execute(MergeMethod.Rebase);
        menu.Items.Add(rebaseItem);

        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PullRequestDetailViewModel vm) return;

        var menu = new ContextMenu();

        var refreshItem = new MenuItem { Header = "Refresh" };
        refreshItem.Click += async (_, _) => await vm.RefreshCommand.ExecuteAsync(null);
        menu.Items.Add(refreshItem);

        var openBrowserItem = new MenuItem { Header = "Open in browser" };
        openBrowserItem.Click += (_, _) => vm.OpenInBrowserCommand.Execute(null);
        menu.Items.Add(openBrowserItem);

        if (vm.IsOpen)
        {
            menu.Items.Add(new Separator());

            var closeItem = new MenuItem { Header = "Close pull request" };
            closeItem.Click += async (_, _) =>
            {
                var result = FluentMessageBox.Show(
                    Window.GetWindow(this)!,
                    "Close this pull request without merging?",
                    "Close Pull Request",
                    MessageBoxButton.YesNo,
                    FluentMessageBoxIcon.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await vm.CloseCommand.ExecuteAsync(null);
                }
            };
            menu.Items.Add(closeItem);
        }

        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(start);
        while (current != null)
        {
            if (current is T match)
                return match;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static SymbolIcon CreateMenuIcon(Symbol symbol, string hexColor) =>
        new()
        {
            Symbol = symbol,
            FontSize = 16,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor))
        };
}
