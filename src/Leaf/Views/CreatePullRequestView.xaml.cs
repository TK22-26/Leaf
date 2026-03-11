using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Leaf.Models;
using Leaf.ViewModels;

namespace Leaf.Views;

/// <summary>
/// Code-behind for CreatePullRequestView.
/// </summary>
public partial class CreatePullRequestView : UserControl
{
    public CreatePullRequestView()
    {
        InitializeComponent();
    }

    private void ReviewerSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is CreatePullRequestViewModel vm)
        {
            vm.SearchReviewersCommand.Execute(null);
        }
    }

    private void ReviewerResult_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ReviewerInfo reviewer }
            && DataContext is CreatePullRequestViewModel vm)
        {
            vm.AddReviewerCommand.Execute(reviewer);
        }
    }
}
