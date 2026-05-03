using System.Windows;
using System.Windows.Input;

namespace Leaf.Views;

/// <summary>
/// §5.13 Phase 4 — modal passphrase prompt for ssh-add. The empty
/// string is a valid value (key has no passphrase); cancellation
/// returns null so the caller can distinguish "skip" from "load with
/// empty passphrase".
/// </summary>
public partial class PassphrasePromptDialog : Window
{
    public string Passphrase { get; private set; } = string.Empty;

    private PassphrasePromptDialog(string prompt)
    {
        InitializeComponent();
        PromptText.Text = prompt;
        Loaded += (_, _) => PassphraseBox.Focus();
    }

    /// <summary>
    /// Show the prompt modal-to-<paramref name="owner"/>. Returns null
    /// when the user cancels; otherwise the entered passphrase
    /// (possibly empty).
    /// </summary>
    public static string? Prompt(Window? owner, string prompt)
    {
        var dialog = new PassphrasePromptDialog(prompt);
        if (owner is not null) dialog.Owner = owner;
        var result = dialog.ShowDialog();
        return result == true ? dialog.Passphrase : null;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Passphrase = PassphraseBox.Password;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void PassphraseBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OkButton_Click(sender, e);
            e.Handled = true;
        }
    }
}
