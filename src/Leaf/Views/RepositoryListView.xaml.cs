using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.RequestRepositorySelection -= OnRequestRepositorySelection;
        }

        if (e.NewValue is MainViewModel newVm)
        {
            newVm.RequestRepositorySelection += OnRequestRepositorySelection;
        }
    }

    private void OnRequestRepositorySelection(object? sender, RepositoryInfo repo)
    {
        // Use Dispatcher to ensure the TreeView has finished loading
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            SelectRepositoryInTreeView(repo);
        });
    }

    private void SelectRepositoryInTreeView(RepositoryInfo targetRepo)
    {
        // Search through root items to find and select the repository
        foreach (var rootItem in RepoTreeView.Items)
        {
            var rootContainer = RepoTreeView.ItemContainerGenerator.ContainerFromItem(rootItem) as TreeViewItem;
            if (rootContainer == null)
                continue;

            // Expand the root item to ensure children are generated
            rootContainer.IsExpanded = true;
            rootContainer.UpdateLayout();

            // Search children
            foreach (var childItem in rootContainer.Items)
            {
                // Check if this child is the target repo (either directly or wrapped)
                RepositoryInfo? childRepo = childItem switch
                {
                    RepositoryInfo r => r,
                    QuickAccessItem qa => qa.Repository,
                    _ => null
                };

                if (childRepo != null && childRepo.Path == targetRepo.Path)
                {
                    var childContainer = rootContainer.ItemContainerGenerator.ContainerFromItem(childItem) as TreeViewItem;
                    if (childContainer != null)
                    {
                        childContainer.IsSelected = true;
                        childContainer.BringIntoView();
                        return;
                    }
                }
            }
        }
    }

    private void RepoTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        // Handle both RepositoryInfo (from folder groups) and QuickAccessItem (from PINNED/RECENT)
        RepositoryInfo? repo = e.NewValue switch
        {
            RepositoryInfo r => r,
            QuickAccessItem qa => qa.Repository,
            _ => null
        };

        if (repo != null)
        {
            _ = viewModel.SelectRepositoryAsync(repo);
        }
    }
}
