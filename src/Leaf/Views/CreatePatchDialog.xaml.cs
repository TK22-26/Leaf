using System.IO;
using System.Windows;
using System.Windows.Controls;
using Leaf.Models;
using Microsoft.Win32;

namespace Leaf.Views;

/// <summary>
/// Modal dialog used by <see cref="ViewModels.MainViewModel.CreatePatchFromCommitAsync"/>
/// to collect the output folder and <see cref="CreatePatchOptions"/>. The
/// dialog is dumb: it sets <see cref="OutputDirectory"/> + <see cref="Options"/>
/// on OK and the caller does the actual work.
/// </summary>
public partial class CreatePatchDialog : Window
{
    public CreatePatchDialog(string commitDescription, string defaultOutputDirectory, CreatePatchOptions? defaults = null)
    {
        InitializeComponent();
        CommitText.Text = commitDescription;
        OutputDirTextBox.Text = defaultOutputDirectory;

        // Pre-populate options from the caller (typically read from git
        // config). The submitted CreatePatchOptions is authoritative on
        // submit — we always pass --signoff or --no-signoff explicitly so
        // unchecking an inherited true actually wins over global config.
        if (defaults != null)
        {
            IncludeBinaryCheckBox.IsChecked = defaults.IncludeBinary;
            SignOffCheckBox.IsChecked = defaults.SignOff;
            SubjectPrefixTextBox.Text = defaults.SubjectPrefix ?? string.Empty;
        }

        // CreateButton starts disabled; the TextChanged handler enables it
        // once the path is non-blank. We don't try to validate existence
        // here — PatchService creates the directory if missing.
        UpdateOkEnabled();
    }

    public string OutputDirectory { get; private set; } = string.Empty;

    public CreatePatchOptions Options { get; private set; } = new();

    private void OutputDirTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateOkEnabled();

    private void UpdateOkEnabled()
    {
        CreateButton.IsEnabled = !string.IsNullOrWhiteSpace(OutputDirTextBox.Text);
    }

    private void BrowseOutputDir_Click(object sender, RoutedEventArgs e)
    {
        var current = OutputDirTextBox.Text;
        var dialog = new OpenFolderDialog
        {
            Title = "Select Patch Output Folder",
            InitialDirectory = Directory.Exists(current)
                ? current
                : Path.GetDirectoryName(current) ?? string.Empty,
        };
        if (dialog.ShowDialog(this) == true)
        {
            OutputDirTextBox.Text = dialog.FolderName;
        }
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        OutputDirectory = OutputDirTextBox.Text.Trim();
        Options = new CreatePatchOptions
        {
            IncludeBinary = IncludeBinaryCheckBox.IsChecked == true,
            SignOff = SignOffCheckBox.IsChecked == true,
            SubjectPrefix = string.IsNullOrWhiteSpace(SubjectPrefixTextBox.Text)
                ? null
                : SubjectPrefixTextBox.Text.Trim(),
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
