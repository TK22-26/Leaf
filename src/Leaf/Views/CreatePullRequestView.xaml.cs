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

    private void ReviewerResult_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ReviewerInfo reviewer } element
            && DataContext is CreatePullRequestViewModel vm)
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
}
