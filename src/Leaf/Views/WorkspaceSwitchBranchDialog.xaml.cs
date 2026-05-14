using System.Windows;

namespace Leaf.Views;

public partial class WorkspaceSwitchBranchDialog : Window
{
    public WorkspaceSwitchBranchDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => BranchInput.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Switch_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
