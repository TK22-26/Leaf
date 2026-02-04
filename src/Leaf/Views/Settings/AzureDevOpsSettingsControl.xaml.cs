using System.Windows;
using System.Windows.Controls;
using Leaf.Services;

namespace Leaf.Views.Settings;

/// <summary>
/// Settings control for Azure DevOps authentication with multi-organization PAT support.
/// </summary>
public partial class AzureDevOpsSettingsControl : UserControl, ISettingsSectionControl
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

    public AzureDevOpsSettingsControl()
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

    /// <summary>
    /// Gets the first configured organization name (for backward compatibility).
    /// </summary>
    public string Organization
    {
        get
        {
            if (_credentialService == null) return string.Empty;
            var orgs = _credentialService.GetOrganizationsForProvider("AzureDevOps").ToList();
            return orgs.Count > 0 ? orgs[0] : string.Empty;
        }
    }

    private void RefreshCredentialsList()
    {
        if (_credentialService == null) return;

        var orgs = _credentialService.GetOrganizationsForProvider("AzureDevOps").ToList();
        CredentialsList.ItemsSource = orgs;

        // Show/hide empty state
        EmptyStateText.Visibility = orgs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    #region Add Credential

    private void AddCredential_Click(object sender, RoutedEventArgs e)
    {
        AddCredentialForm.Visibility = Visibility.Visible;
        AddCredentialButton.Visibility = Visibility.Collapsed;
        OrganizationTextBox.Focus();
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

        var organization = OrganizationTextBox.Text.Trim();
        var pat = _isPatVisible ? PatTextBox.Text : PatPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(organization))
        {
            MessageBox.Show("Please enter an organization name.", "Missing Organization",
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
        if (_credentialService.HasCredential($"AzureDevOps:{organization}"))
        {
            var result = MessageBox.Show(
                $"A credential for '{organization}' already exists. Do you want to replace it?",
                "Credential Exists",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;
        }

        // Store the credential
        _credentialService.StorePat($"AzureDevOps:{organization}", pat);

        // Update settings (keep AzureDevOpsOrganization for backward compatibility during migration)
        if (string.IsNullOrEmpty(_settings.AzureDevOpsOrganization))
        {
            _settings.AzureDevOpsOrganization = organization;
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
        OrganizationTextBox.Text = "";
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
        if (_credentialService == null || sender is not Button button || button.Tag is not string organization)
            return;

        var result = MessageBox.Show(
            $"Remove the credential for '{organization}'?\n\nYou will need to re-enter the PAT to use this organization again.",
            "Remove Credential",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _credentialService.RemovePat($"AzureDevOps:{organization}");
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
