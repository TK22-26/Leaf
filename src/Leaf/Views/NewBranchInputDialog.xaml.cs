using System.Windows;

namespace Leaf.Views;

/// <summary>
/// Generic one-field input prompt for collecting a branch name. Used
/// by the reflog view's "Create branch here" action; the rest of
/// Leaf's branch-creation flows use the sidebar's inline input, but
/// that isn't available from a modal reflog window.
/// </summary>
public partial class NewBranchInputDialog : Window
{
    public NewBranchInputDialog(string title, string prompt)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        NameTextBox.Focus();
    }

    public string BranchName { get; private set; } = string.Empty;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        BranchName = NameTextBox.Text.Trim();
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ValidationChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        OkButton.IsEnabled = !string.IsNullOrWhiteSpace(NameTextBox.Text);
    }
}
