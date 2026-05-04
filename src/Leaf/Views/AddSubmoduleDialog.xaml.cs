using System.Windows;

namespace Leaf.Views;

/// <summary>
/// Collects URL + target path (+ optional tracking branch) for a new
/// submodule. The dialog only validates that both required fields are
/// non-empty — git itself surfaces the real errors (bad URL, path
/// already in the index, etc.) when the caller runs the add.
/// </summary>
public partial class AddSubmoduleDialog : Window
{
    public AddSubmoduleDialog()
    {
        InitializeComponent();
    }

    public string Url { get; private set; } = string.Empty;
    public string Path { get; private set; } = string.Empty;
    public string? Branch { get; private set; }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Url = UrlTextBox.Text.Trim();
        // Normalise to forward slashes so users can paste Windows-style
        // input without git choking on the mixed separators.
        Path = PathTextBox.Text.Trim().Replace('\\', '/').TrimEnd('/');
        Branch = string.IsNullOrWhiteSpace(BranchTextBox.Text)
            ? null
            : BranchTextBox.Text.Trim();
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ValidationChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        OkButton.IsEnabled =
            !string.IsNullOrWhiteSpace(UrlTextBox.Text) &&
            !string.IsNullOrWhiteSpace(PathTextBox.Text);
    }
}
