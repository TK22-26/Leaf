using System.Windows;

namespace Leaf.Views;

/// <summary>
/// Interaction logic for MergeDialog.xaml
/// </summary>
public partial class MergeDialog : Window
{
    public MergeDialog()
    {
        InitializeComponent();
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
