using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Leaf.Models;
using Leaf.ViewModels;

namespace Leaf.Views;

/// <summary>
/// Interaction logic for RepositoryListView.xaml
/// </summary>
public partial class RepositoryListView : UserControl
{
    public RepositoryListView()
    {
        InitializeComponent();
    }

    private void Repository_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is RepositoryInfo repo)
        {
            if (DataContext is MainViewModel viewModel)
            {
                _ = viewModel.SelectRepositoryAsync(repo);
            }
        }
    }
}
