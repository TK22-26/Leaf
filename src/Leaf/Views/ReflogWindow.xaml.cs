using System.Windows;
using Leaf.ViewModels;

namespace Leaf.Views;

/// <summary>
/// Host window for the reflog view. Kicks off the initial load in
/// <see cref="Window_Loaded"/> so the grid is already populated by
/// the time WPF finishes rendering.
/// </summary>
public partial class ReflogWindow : Window
{
    private readonly ReflogViewModel _viewModel;

    public ReflogWindow(ReflogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += Window_Loaded;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
