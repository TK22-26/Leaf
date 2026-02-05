using System.Windows;
using System.Windows.Controls;
using Leaf.Services;

namespace Leaf.Views.Settings;

/// <summary>
/// Settings control for GitHub authentication with multi-account PAT support.
/// </summary>
public partial class GitHubSettingsControl : UserControl, ISettingsSectionControl
{
    private AppSettings? _settings;
    private CredentialService? _credentialService;
    private SettingsService? _settingsService;

    private bool _isPatVisible;
    private bool _suppressPatSync;

    /// <summary>
    /// Gets or sets the owner window (unused, kept for interface compatibility).
    /// </summary>
    public Window? OwnerWindow { get; set; }

    public GitHubSettingsControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Sets the settings service for saving settings.
    /// </summary>
    public void SetSettingsService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void LoadSettings(AppSettings settings, CredentialService credentialService)
    {
        _settings = settings;
        _credentialService = credentialService;

        RefreshCredentialsList();
    }

    public void SaveSettings(AppSettings settings, CredentialService credentialService)
    {
        // Credentials are saved immediately when adding, nothing to do here
    }

    private void RefreshCredentialsList()
    {
        if (_credentialService == null) return;

        var orgs = _credentialService.GetOrganizationsForProvider("GitHub").ToList();
        CredentialsList.ItemsSource = orgs;

        // Show/hide empty state
        EmptyStateText.Visibility = orgs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    #region Add Credential

    private void AddCredential_Click(object sender, RoutedEventArgs e)
    {
        AddCredentialForm.Visibility = Visibility.Visible;
        AddCredentialButton.Visibility = Visibility.Collapsed;
        OwnerTextBox.Focus();
    }

    private void CancelAdd_Click(object sender, RoutedEventArgs e)
    {
        ClearAddForm();
        AddCredentialForm.Visibility = Visibility.Collapsed;
        AddCredentialButton.Visibility = Visibility.Visible;
    }

    private void SaveCredential_Click(object sender, RoutedEventArgs e)
    {
        if (_credentialService == null || _settingsService == null || _settings == null)
            return;

        var owner = OwnerTextBox.Text.Trim();
        var pat = _isPatVisible ? PatTextBox.Text : PatPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(owner))
        {
            MessageBox.Show("Please enter an owner or organization name.", "Missing Owner",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(pat))
        {
            MessageBox.Show("Please enter a Personal Access Token.", "Missing PAT",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Check if this org already exists
        if (_credentialService.HasCredential($"GitHub:{owner}"))
        {
            var result = MessageBox.Show(
                $"A credential for '{owner}' already exists. Do you want to replace it?",
                "Credential Exists",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;
        }

        // Store the credential
        _credentialService.StorePat($"GitHub:{owner}", pat);

        // Update settings (keep GitHubUsername for backward compatibility during migration)
        if (string.IsNullOrEmpty(_settings.GitHubUsername))
        {
            _settings.GitHubUsername = owner;
            _settingsService.SaveSettings(_settings);
        }

        // Refresh UI
        ClearAddForm();
        AddCredentialForm.Visibility = Visibility.Collapsed;
        AddCredentialButton.Visibility = Visibility.Visible;
        RefreshCredentialsList();
    }

    private void ClearAddForm()
    {
        OwnerTextBox.Text = "";
        PatPasswordBox.Password = "";
        PatTextBox.Text = "";
        _isPatVisible = false;
        PatPasswordBox.Visibility = Visibility.Visible;
        PatTextBox.Visibility = Visibility.Collapsed;
        TogglePatVisibilityButton.Content = "Show";
    }

    #endregion

    #region Remove Credential

    private void RemoveCredential_Click(object sender, RoutedEventArgs e)
    {
        if (_credentialService == null || sender is not Button button || button.Tag is not string owner)
            return;

        var result = MessageBox.Show(
            $"Remove the credential for '{owner}'?\n\nYou will need to re-enter the PAT to use this account again.",
            "Remove Credential",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _credentialService.RemovePat($"GitHub:{owner}");
            RefreshCredentialsList();
        }
    }

    #endregion

    #region PAT Visibility Toggle

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

    #endregion
}
