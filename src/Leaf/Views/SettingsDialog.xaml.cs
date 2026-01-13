using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Leaf.Services;
using Microsoft.Win32;

namespace Leaf.Views;

/// <summary>
/// Settings dialog for configuring PAT and other options.
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly CredentialService _credentialService;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private bool _isPatVisible;
    private bool _suppressPatSync;
    private bool _isGitHubPatVisible;
    private bool _suppressGitHubPatSync;

    public SettingsDialog(CredentialService credentialService, SettingsService settingsService)
    {
        InitializeComponent();

        _credentialService = credentialService;
        _settingsService = settingsService;
        _settings = settingsService.LoadSettings();

        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        // Load default clone path
        ClonePathTextBox.Text = _settings.DefaultClonePath;

        // Load organization
        OrganizationTextBox.Text = _settings.AzureDevOpsOrganization;
        GitHubUsernameTextBox.Text = _settings.GitHubUsername;

        // Check if PAT exists
        var existingPat = _credentialService.GetCredential("AzureDevOps");
        if (!string.IsNullOrEmpty(existingPat))
        {
            PatStatusText.Text = "Connected";
            PatStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 150, 0));
            SavePatButton.IsEnabled = false;
            ClearPatButton.IsEnabled = true;
        }
        else
        {
            PatStatusText.Text = "Not connected";
            PatStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            SavePatButton.IsEnabled = true;
            ClearPatButton.IsEnabled = false;
        }

        var existingGitHubPat = _credentialService.GetCredential("GitHub");
        if (!string.IsNullOrEmpty(existingGitHubPat))
        {
            GitHubStatusText.Text = "Connected";
            GitHubStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 150, 0));
            SaveGitHubPatButton.IsEnabled = false;
            ClearGitHubPatButton.IsEnabled = true;
        }
        else
        {
            GitHubStatusText.Text = "Not connected";
            GitHubStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            SaveGitHubPatButton.IsEnabled = true;
            ClearGitHubPatButton.IsEnabled = false;
        }
    }

    private void PatPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressPatSync) return;

        _suppressPatSync = true;
        PatTextBox.Text = PatPasswordBox.Password;
        _suppressPatSync = false;
    }

    private void PatTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressPatSync) return;

        _suppressPatSync = true;
        PatPasswordBox.Password = PatTextBox.Text;
        _suppressPatSync = false;
    }

    private void TogglePatVisibility_Click(object sender, RoutedEventArgs e)
    {
        _isPatVisible = !_isPatVisible;

        if (_isPatVisible)
        {
            PatTextBox.Text = PatPasswordBox.Password;
            PatPasswordBox.Visibility = Visibility.Collapsed;
            PatTextBox.Visibility = Visibility.Visible;
            TogglePatVisibilityButton.Content = "Hide";
        }
        else
        {
            PatPasswordBox.Password = PatTextBox.Text;
            PatTextBox.Visibility = Visibility.Collapsed;
            PatPasswordBox.Visibility = Visibility.Visible;
            TogglePatVisibilityButton.Content = "Show";
        }
    }

    private void GitHubPatPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressGitHubPatSync) return;

        _suppressGitHubPatSync = true;
        GitHubPatTextBox.Text = GitHubPatPasswordBox.Password;
        _suppressGitHubPatSync = false;
    }

    private void GitHubPatTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressGitHubPatSync) return;

        _suppressGitHubPatSync = true;
        GitHubPatPasswordBox.Password = GitHubPatTextBox.Text;
        _suppressGitHubPatSync = false;
    }

    private void ToggleGitHubPatVisibility_Click(object sender, RoutedEventArgs e)
    {
        _isGitHubPatVisible = !_isGitHubPatVisible;

        if (_isGitHubPatVisible)
        {
            GitHubPatTextBox.Text = GitHubPatPasswordBox.Password;
            GitHubPatPasswordBox.Visibility = Visibility.Collapsed;
            GitHubPatTextBox.Visibility = Visibility.Visible;
            ToggleGitHubPatVisibilityButton.Content = "Hide";
        }
        else
        {
            GitHubPatPasswordBox.Password = GitHubPatTextBox.Text;
            GitHubPatTextBox.Visibility = Visibility.Collapsed;
            GitHubPatPasswordBox.Visibility = Visibility.Visible;
            ToggleGitHubPatVisibilityButton.Content = "Show";
        }
    }

    private void SavePat_Click(object sender, RoutedEventArgs e)
    {
        var pat = _isPatVisible ? PatTextBox.Text : PatPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(pat))
        {
            MessageBox.Show("Please enter a PAT before saving.", "No PAT Entered",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Save to credential manager
        _credentialService.SaveCredential("AzureDevOps", "git", pat);

        PatStatusText.Text = "Connected";
        PatStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 150, 0));
        SavePatButton.IsEnabled = false;
        ClearPatButton.IsEnabled = true;

        // Clear the input
        PatPasswordBox.Password = "";
        PatTextBox.Text = "";
    }

    private void ClearPat_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to clear the saved PAT?",
            "Clear PAT",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _credentialService.DeleteCredential("AzureDevOps");

            PatPasswordBox.Password = "";
            PatTextBox.Text = "";
            PatStatusText.Text = "Not connected";
            PatStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            SavePatButton.IsEnabled = true;
            ClearPatButton.IsEnabled = false;
        }
    }

    private void SaveGitHubPat_Click(object sender, RoutedEventArgs e)
    {
        var pat = _isGitHubPatVisible ? GitHubPatTextBox.Text : GitHubPatPasswordBox.Password;
        var username = GitHubUsernameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(username))
        {
            MessageBox.Show("Please enter a GitHub username or email.", "Missing Username",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(pat))
        {
            MessageBox.Show("Please enter a PAT before saving.", "No PAT Entered",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _credentialService.SaveCredential("GitHub", username, pat);
        _settings.GitHubUsername = username;
        _settingsService.SaveSettings(_settings);

        GitHubStatusText.Text = "Connected";
        GitHubStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 150, 0));
        SaveGitHubPatButton.IsEnabled = false;
        ClearGitHubPatButton.IsEnabled = true;

        GitHubPatPasswordBox.Password = "";
        GitHubPatTextBox.Text = "";
    }

    private void ClearGitHubPat_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to disconnect GitHub?",
            "Disconnect GitHub",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _credentialService.DeleteCredential("GitHub");
            GitHubPatPasswordBox.Password = "";
            GitHubPatTextBox.Text = "";
            GitHubStatusText.Text = "Not connected";
            GitHubStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            SaveGitHubPatButton.IsEnabled = true;
            ClearGitHubPatButton.IsEnabled = false;
        }
    }

    private void BrowseClonePath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Default Clone Directory",
            InitialDirectory = ClonePathTextBox.Text
        };

        if (dialog.ShowDialog() == true)
        {
            ClonePathTextBox.Text = dialog.FolderName;
            _settings.DefaultClonePath = dialog.FolderName;
            _settingsService.SaveSettings(_settings);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // Save settings if changed
        var changed = false;

        if (_settings.DefaultClonePath != ClonePathTextBox.Text)
        {
            _settings.DefaultClonePath = ClonePathTextBox.Text;
            changed = true;
        }

        if (_settings.AzureDevOpsOrganization != OrganizationTextBox.Text.Trim())
        {
            _settings.AzureDevOpsOrganization = OrganizationTextBox.Text.Trim();
            changed = true;
        }

        if (_settings.GitHubUsername != GitHubUsernameTextBox.Text.Trim())
        {
            _settings.GitHubUsername = GitHubUsernameTextBox.Text.Trim();
            changed = true;
        }

        if (changed)
        {
            _settingsService.SaveSettings(_settings);
        }

        DialogResult = true;
        Close();
    }
}
