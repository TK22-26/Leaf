using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Leaf.ViewModels;

namespace Leaf.Views;

/// <summary>
/// Interaction logic for CommitDetailView.xaml
/// </summary>
public partial class CommitDetailView : UserControl
{
    public CommitDetailView()
    {
        InitializeComponent();
    }

    private void WorkingChangesBanner_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is CommitDetailViewModel vm)
        {
            vm.SelectWorkingChanges();
        }
    }

    private void CommitSha_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CommitDetailViewModel vm)
        {
            vm.CopySha();
        }
    }

    private void ParentSha_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CommitDetailViewModel vm)
        {
            vm.NavigateToParent();
        }
    }
}
