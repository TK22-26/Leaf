using System.Windows;

namespace Leaf.Views;

public partial class WorkspaceMergeDialog : Window
{
    public WorkspaceMergeDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => TargetInput.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Merge_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
