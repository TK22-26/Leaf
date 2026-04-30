using System.Windows;
using System.Windows.Controls;

namespace Leaf.Views;

/// <summary>
/// Picker for the two refs <c>git bisect start</c> needs: the known-bad
/// commit (defaults to HEAD) and the known-good ancestor. The dialog is
/// dumb — it just collects strings and lets the host run the actual
/// <c>git bisect start</c>; if the refs don't resolve or aren't related,
/// git's own error gets surfaced verbatim.
/// </summary>
public partial class StartBisectDialog : Window
{
    public StartBisectDialog(string defaultBadRef = "HEAD", string defaultGoodRef = "")
    {
        InitializeComponent();
        BadRefTextBox.Text = defaultBadRef;
        GoodRefTextBox.Text = defaultGoodRef;
        UpdateOkEnabled();
        // Focus the good-ref box on open since bad-ref is usually
        // already correct (HEAD).
        Loaded += (_, _) => GoodRefTextBox.Focus();
    }

    public string BadRef { get; private set; } = string.Empty;
    public string GoodRef { get; private set; } = string.Empty;

    private void Ref_TextChanged(object sender, TextChangedEventArgs e) => UpdateOkEnabled();

    private void UpdateOkEnabled()
    {
        StartButton.IsEnabled =
            !string.IsNullOrWhiteSpace(BadRefTextBox.Text) &&
            !string.IsNullOrWhiteSpace(GoodRefTextBox.Text);
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        BadRef = BadRefTextBox.Text.Trim();
        GoodRef = GoodRefTextBox.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
