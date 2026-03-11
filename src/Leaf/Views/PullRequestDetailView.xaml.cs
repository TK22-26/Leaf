using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    private void MergeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PullRequestDetailViewModel vm) return;

        // Show merge method context menu
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

    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PullRequestDetailViewModel vm) return;

        var result = MessageBox.Show(
            Window.GetWindow(this)!,
            "Close this pull request without merging?",
            "Close Pull Request",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            await vm.CloseCommand.ExecuteAsync(null);
        }
    }
}
