using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Leaf.Models;
using Leaf.ViewModels;

namespace Leaf.Views;

/// <summary>
/// Interaction logic for WorkingChangesView.xaml
/// </summary>
public partial class WorkingChangesView : UserControl
{
    public WorkingChangesView()
    {
        InitializeComponent();
    }

    private void UnstagedFile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Select the file for diff viewing (future feature)
        if (sender is FrameworkElement element && element.DataContext is FileStatusInfo file)
        {
            if (DataContext is WorkingChangesViewModel viewModel)
            {
                // Could add SelectedFile property in future for diff viewing
            }
        }
    }

    private void StagedFile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Select the file for diff viewing (future feature)
        if (sender is FrameworkElement element && element.DataContext is FileStatusInfo file)
        {
            if (DataContext is WorkingChangesViewModel viewModel)
            {
                // Could add SelectedFile property in future for diff viewing
            }
        }
    }
}
