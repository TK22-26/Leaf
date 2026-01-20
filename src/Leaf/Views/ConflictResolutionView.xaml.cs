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
            _viewModel.RequestScrollToRegion -= ViewModel_RequestScrollToRegion;
        }

        _viewModel = e.NewValue as ConflictResolutionViewModel;

        if (_viewModel != null)
        {
            _viewModel.MergeCompleted += ViewModel_MergeCompleted;
            _viewModel.RequestScrollToRegion += ViewModel_RequestScrollToRegion;
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

    private void ViewModel_RequestScrollToRegion(object? sender, int regionIndex)
    {
        if (_viewModel?.CurrentMergeResult == null)
        {
            return;
        }

        if (regionIndex < 0 || regionIndex >= _viewModel.CurrentMergeResult.Regions.Count)
        {
            return;
        }

        var region = _viewModel.CurrentMergeResult.Regions[regionIndex];
        Dispatcher.BeginInvoke(() =>
        {
            OursRegionList.UpdateLayout();
            TheirsRegionList.UpdateLayout();

            if (OursRegionList.ItemContainerGenerator.ContainerFromItem(region) is FrameworkElement oursContainer)
            {
                oursContainer.BringIntoView();
            }

            if (TheirsRegionList.ItemContainerGenerator.ContainerFromItem(region) is FrameworkElement theirsContainer)
            {
                theirsContainer.BringIntoView();
            }
        });
    }
}
