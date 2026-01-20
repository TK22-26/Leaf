using System.Windows;

namespace Leaf.Views;

public partial class CreateTagDialog : Window
{
    public CreateTagDialog()
    {
        InitializeComponent();
    }

    public string TagName { get; private set; } = string.Empty;

    public string? TagMessage { get; private set; }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        TagName = TagNameTextBox.Text.Trim();
        TagMessage = string.IsNullOrWhiteSpace(TagMessageTextBox.Text) ? null : TagMessageTextBox.Text.Trim();
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void TagNameTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        OkButton.IsEnabled = !string.IsNullOrWhiteSpace(TagNameTextBox.Text);
    }
}
