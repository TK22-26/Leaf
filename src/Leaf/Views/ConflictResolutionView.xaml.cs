using System.Windows;
using Leaf.ViewModels;

namespace Leaf.Views;

/// <summary>
/// Interaction logic for ConflictResolutionView.xaml
/// </summary>
public partial class ConflictResolutionView : Window
{
    private ConflictResolutionViewModel? _viewModel;

    public ConflictResolutionView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.MergeCompleted -= ViewModel_MergeCompleted;
        }

        _viewModel = e.NewValue as ConflictResolutionViewModel;

        if (_viewModel != null)
        {
            _viewModel.MergeCompleted += ViewModel_MergeCompleted;
        }
    }

    private void ViewModel_MergeCompleted(object? sender, bool success)
    {
        Close();
    }

    private void OnDoneClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
