using System.Windows;

namespace Leaf.Views;

/// <summary>
/// Interaction logic for RebaseDialog.xaml.
/// Mirrors <see cref="MergeDialog"/> — the dialog is purely a strategy picker;
/// all execution lives on <see cref="ViewModels.MainViewModel"/>.
/// </summary>
public partial class RebaseDialog : Window
{
    public RebaseDialog()
    {
        InitializeComponent();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Rebase_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
