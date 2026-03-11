using System.Windows;
using System.Windows.Controls;
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
        // Walk up to find the MainViewModel and close the PR view
        if (Window.GetWindow(this)?.DataContext is MainViewModel mainVm)
        {
            mainVm.ClosePullRequestViewCommand.Execute(null);
        }
    }
}
