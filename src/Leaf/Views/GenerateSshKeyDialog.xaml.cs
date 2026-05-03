using System.IO;
using System.Windows;
using System.Windows.Controls;
using Leaf.Services.Ssh;
using Microsoft.Win32;

namespace Leaf.Views;

/// <summary>
/// §5.13 Phase 2 — wizard for <c>ssh-keygen</c>. Builds an
/// <see cref="SshKeyGenerationRequest"/> from the form values; the
/// caller drives the actual generation so this dialog has no direct
/// dependency on the service. Dialog returns true on click, false on
/// cancel; on true, callers read <see cref="BuildRequest"/>.
/// </summary>
public partial class GenerateSshKeyDialog : Window
{
    private static readonly string DefaultSshDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

    public GenerateSshKeyDialog()
    {
        InitializeComponent();
        // Seed the path with a sensible default for the initial algorithm
        // (Ed25519 → id_ed25519) and let AlgorithmCombo_SelectionChanged
        // keep it in sync if the user changes algorithm.
        UpdateDefaultPathForAlgorithm();
        UpdateValidation();
    }

    /// <summary>
    /// Build a request from the current form state. Returns null when
    /// the form is invalid — caller should treat that as "user cancelled
    /// despite Generate click", though normally the Generate button is
    /// disabled in that state.
    /// </summary>
    public SshKeyGenerationRequest? BuildRequest()
    {
        var (algo, bits) = ParseAlgorithmTag();
        var path = PathBox.Text?.Trim();
        if (string.IsNullOrEmpty(path)) return null;
        if (PassphraseBox.Password != ConfirmBox.Password) return null;

        return new SshKeyGenerationRequest(
            Algorithm: algo,
            Bits: bits,
            Comment: CommentBox.Text?.Trim() ?? string.Empty,
            OutputPath: path,
            Passphrase: string.IsNullOrEmpty(PassphraseBox.Password) ? null : PassphraseBox.Password);
    }

    private void AlgorithmCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateDefaultPathForAlgorithm();
        UpdateValidation();
    }

    private void PathBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateValidation();

    private void Passphrase_Changed(object sender, RoutedEventArgs e) => UpdateValidation();

    private void UpdateDefaultPathForAlgorithm()
    {
        if (PathBox is null) return;
        // Don't overwrite a path the user has already customised. The
        // simplest signal: PathBox is empty or matches one of our
        // canonical defaults. Anything else means "user typed it".
        var current = PathBox.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(current) && !LooksLikeCanonicalDefault(current)) return;

        var (algo, _) = ParseAlgorithmTag();
        var filename = algo switch
        {
            SshKeyAlgorithm.Ed25519 => "id_ed25519",
            SshKeyAlgorithm.Rsa => "id_rsa",
            SshKeyAlgorithm.Ecdsa => "id_ecdsa",
            SshKeyAlgorithm.Dsa => "id_dsa",
            _ => "id_ed25519",
        };
        PathBox.Text = Path.Combine(DefaultSshDir, filename);
    }

    private static bool LooksLikeCanonicalDefault(string path)
    {
        var name = Path.GetFileName(path);
        return name is "id_ed25519" or "id_rsa" or "id_ecdsa" or "id_dsa";
    }

    private (SshKeyAlgorithm Algorithm, int? Bits) ParseAlgorithmTag()
    {
        var tag = (AlgorithmCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Ed25519";
        return tag switch
        {
            "Ed25519" => (SshKeyAlgorithm.Ed25519, null),
            "Rsa-4096" => (SshKeyAlgorithm.Rsa, 4096),
            "Rsa-3072" => (SshKeyAlgorithm.Rsa, 3072),
            "Ecdsa-384" => (SshKeyAlgorithm.Ecdsa, 384),
            "Ecdsa-256" => (SshKeyAlgorithm.Ecdsa, 256),
            _ => (SshKeyAlgorithm.Ed25519, null),
        };
    }

    private void UpdateValidation()
    {
        if (PathBox is null || ValidationText is null || GenerateButton is null) return;

        string? error = null;
        var path = PathBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(path))
            error = "Output path is required.";
        else if (File.Exists(path) || File.Exists(path + ".pub"))
            error = "A key already exists at that path. Pick a different filename.";
        else if (PassphraseBox.Password != ConfirmBox.Password)
            error = "Passphrases don't match.";

        if (error is null)
        {
            ValidationText.Visibility = Visibility.Collapsed;
            GenerateButton.IsEnabled = true;
        }
        else
        {
            ValidationText.Text = error;
            ValidationText.Visibility = Visibility.Visible;
            GenerateButton.IsEnabled = false;
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var (algo, _) = ParseAlgorithmTag();
        var defaultName = algo switch
        {
            SshKeyAlgorithm.Ed25519 => "id_ed25519",
            SshKeyAlgorithm.Rsa => "id_rsa",
            SshKeyAlgorithm.Ecdsa => "id_ecdsa",
            _ => "id_ed25519",
        };
        var dialog = new SaveFileDialog
        {
            Title = "Choose private key path",
            InitialDirectory = DefaultSshDir,
            FileName = defaultName,
            Filter = "All files (*.*)|*.*",
            // ssh-keygen creates two files (the key + .pub); we only ask
            // the user for the private path.
            OverwritePrompt = false,
        };
        if (dialog.ShowDialog() == true)
        {
            PathBox.Text = dialog.FileName;
        }
    }

    private void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateValidation();
        if (!GenerateButton.IsEnabled) return;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
