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
        if (sender is FrameworkElement element && element.DataContext is FileStatusInfo file)
        {
            // Get MainViewModel from Window
            if (Window.GetWindow(this)?.DataContext is MainViewModel mainVm)
            {
                _ = mainVm.ShowUnstagedFileDiffAsync(file);
            }
        }
    }

    private void StagedFile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is FileStatusInfo file)
        {
            // Get MainViewModel from Window
            if (Window.GetWindow(this)?.DataContext is MainViewModel mainVm)
            {
                _ = mainVm.ShowStagedFileDiffAsync(file);
            }
        }
    }

    private void FileItem_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Ensure the item is selected when right-clicking to open context menu
        // The context menu will open automatically
        e.Handled = false;
    }
}
