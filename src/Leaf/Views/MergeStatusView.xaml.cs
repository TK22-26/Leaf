using System.Windows.Controls;
using System.Windows.Input;
using Leaf.Models;
using Leaf.ViewModels;

namespace Leaf.Views;

/// <summary>
/// Interaction logic for MergeStatusView.xaml
/// </summary>
public partial class MergeStatusView : UserControl
{
    public MergeStatusView()
    {
        InitializeComponent();
    }

    private void OnConflictedFileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item
            && item.DataContext is ConflictInfo conflict
            && DataContext is MainViewModel vm
            && vm.OpenConflictInLeafCommand.CanExecute(conflict))
        {
            vm.OpenConflictInLeafCommand.Execute(conflict);
            e.Handled = true;
        }
    }
}
