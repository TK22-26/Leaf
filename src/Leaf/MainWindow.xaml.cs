using System.Windows;
using System.Windows.Input;
using Leaf.Services;
using Leaf.ViewModels;

namespace Leaf;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Set up services
        var gitService = new GitService();
        var credentialService = new CredentialService();
        var settingsService = new SettingsService();

        // Create view model with all services
        var viewModel = new MainViewModel(gitService, credentialService, settingsService, this);

        DataContext = viewModel;
    }

    private void RepoPane_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ToggleRepoPaneCommand.Execute(null);
        }
    }

    private void BranchNameInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(viewModel.NewBranchName))
        {
            viewModel.ConfirmCreateBranchCommand.Execute(null);
        }
        else if (e.Key == Key.Escape)
        {
            viewModel.CancelBranchInputCommand.Execute(null);
        }
    }

    private void BranchInputOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.CancelBranchInputCommand.Execute(null);
        }
    }

    private void BranchNameInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Git branch name invalid characters: space ~ ^ : ? * [ \ @ { }
        // Also control characters and DEL
        const string invalidChars = " ~^:?*[\\@{}";

        foreach (char c in e.Text)
        {
            // Reject invalid characters
            if (invalidChars.Contains(c) || char.IsControl(c))
            {
                e.Handled = true;
                return;
            }
        }

        // Check for invalid patterns based on current text
        if (sender is System.Windows.Controls.TextBox textBox)
        {
            string currentText = textBox.Text;
            string newText = currentText.Insert(textBox.CaretIndex, e.Text);

            // Cannot start with dot or dash
            if (newText.StartsWith('.') || newText.StartsWith('-'))
            {
                e.Handled = true;
                return;
            }

            // Cannot contain consecutive dots
            if (newText.Contains(".."))
            {
                e.Handled = true;
                return;
            }

            // Cannot contain @{
            if (newText.Contains("@{"))
            {
                e.Handled = true;
                return;
            }
        }
    }
}
