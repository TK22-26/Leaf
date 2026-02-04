using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.Views.Settings;

/// <summary>
/// Settings control for Azure DevOps authentication (OAuth and PAT).
/// </summary>
public partial class AzureDevOpsSettingsControl : UserControl, ISettingsSectionControl
{
    private AppSettings? _settings;
    private CredentialService? _credentialService;
    private SettingsService? _settingsService;

    private bool _isPatVisible;
    private bool _suppressPatSync;

    // Azure DevOps OAuth
    private AzureDevOpsOAuthService? _azureDevOpsOAuthService;
    private CancellationTokenSource? _azureDevOpsOAuthCancellationTokenSource;

    /// <summary>
    /// Gets or sets the owner window for OAuth popups.
    /// </summary>
    public Window? OwnerWindow { get; set; }

    public AzureDevOpsSettingsControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Sets the settings service for saving settings during OAuth flows.
    /// </summary>
    public void SetSettingsService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void LoadSettings(AppSettings settings, CredentialService credentialService)
    {
        _settings = settings;
        _credentialService = credentialService;

        // Load organization
        OrganizationTextBox.Text = settings.AzureDevOpsOrganization;

        // Check if Azure DevOps credential exists
        var existingAzureDevOpsToken = credentialService.GetCredential("AzureDevOps");
        if (!string.IsNullOrEmpty(existingAzureDevOpsToken))
        {
            // Show connected state based on auth method
            ShowConnectedState(settings.AzureDevOpsUserDisplayName ?? settings.AzureDevOpsOrganization, settings.AzureDevOpsAuthMethod);
        }
        else
        {
            // Show disconnected state
            ShowDisconnectedState();
        }
    }

    public void SaveSettings(AppSettings settings, CredentialService credentialService)
    {
        settings.AzureDevOpsOrganization = OrganizationTextBox.Text.Trim();
    }

    /// <summary>
    /// Gets the organization name entered by the user.
    /// </summary>
    public string Organization => OrganizationTextBox.Text.Trim();

    #region PAT Handling

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

    private void SavePat_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null || _credentialService == null || _settingsService == null)
            return;

        var pat = _isPatVisible ? PatTextBox.Text : PatPasswordBox.Password;
        var organization = OrganizationTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(organization))
        {
            MessageBox.Show("Please enter an organization name.", "Missing Organization",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(pat))
        {
            MessageBox.Show("Please enter a PAT before saving.", "No PAT Entered",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Save to credential manager
        _credentialService.SaveCredential("AzureDevOps", "git", pat);

        // Update settings
        _settings.AzureDevOpsOrganization = organization;
        _settings.AzureDevOpsAuthMethod = AzureDevOpsAuthMethod.PersonalAccessToken;
        _settingsService.SaveSettings(_settings);

        // Clear PAT fields
        PatPasswordBox.Password = "";
        PatTextBox.Text = "";

        // Show connected state
        ShowConnectedState(organization, AzureDevOpsAuthMethod.PersonalAccessToken);
    }

    #endregion

    #region OAuth

    private async void AzureDevOpsOAuth_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null || _credentialService == null || _settingsService == null)
            return;

        var organization = OrganizationTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(organization))
        {
            MessageBox.Show("Please enter an organization name first.", "Missing Organization",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _azureDevOpsOAuthService ??= new AzureDevOpsOAuthService();
        _azureDevOpsOAuthService.DeviceFlowStatusChanged += OnDeviceFlowStatusChanged;

        try
        {
            // Show OAuth flow panel, hide other sections
            AzureDevOpsOAuthSection.Visibility = Visibility.Collapsed;
            AzureDevOpsPatSection.Visibility = Visibility.Collapsed;
            AzureDevOpsOAuthFlowPanel.Visibility = Visibility.Visible;
            AzureDevOpsOAuthButton.IsEnabled = false;

            // Start device flow
            var deviceCode = await _azureDevOpsOAuthService.StartDeviceFlowAsync();

            // Display user code
            AzureDevOpsUserCodeText.Text = deviceCode.UserCode;
            AzureDevOpsVerificationUriText.Text = deviceCode.VerificationUri;

            // Open browser automatically
            Process.Start(new ProcessStartInfo
            {
                FileName = deviceCode.VerificationUri,
                UseShellExecute = true
            });

            // Start polling
            _azureDevOpsOAuthCancellationTokenSource = new CancellationTokenSource();
            var result = await _azureDevOpsOAuthService.PollForAccessTokenAsync(
                deviceCode.DeviceCode,
                deviceCode.Interval,
                deviceCode.ExpiresIn,
                _azureDevOpsOAuthCancellationTokenSource.Token);

            if (result.Success && !string.IsNullOrEmpty(result.AccessToken))
            {
                // Get user info from Azure DevOps
                var connectionData = await _azureDevOpsOAuthService.GetConnectionDataAsync(result.AccessToken, organization);
                var displayName = connectionData?.AuthenticatedUser?.DisplayName ??
                                  connectionData?.AuthenticatedUser?.CustomDisplayName ??
                                  organization;

                // Save tokens
                _credentialService.SaveCredential("AzureDevOps", displayName, result.AccessToken);
                if (!string.IsNullOrEmpty(result.RefreshToken))
                {
                    _credentialService.SaveRefreshToken("AzureDevOps", result.RefreshToken);
                }

                // Update settings
                _settings.AzureDevOpsAuthMethod = AzureDevOpsAuthMethod.OAuth;
                _settings.AzureDevOpsOrganization = organization;
                _settings.AzureDevOpsUserDisplayName = displayName;
                _settings.AzureDevOpsOAuthTokenCreatedAt = DateTime.UtcNow;
                _settings.AzureDevOpsOAuthTokenExpiresAt = result.ExpiresAt;
                _settings.AzureDevOpsOAuthScopes = result.Scope;
                _settingsService.SaveSettings(_settings);

                // Update UI
                ShowConnectedState(displayName, AzureDevOpsAuthMethod.OAuth);
            }
            else
            {
                // Show error
                var message = result.Error switch
                {
                    EntraOAuthError.AuthorizationDeclined => "You denied the authorization request.",
                    EntraOAuthError.ExpiredToken => "The authorization timed out. Please try again.",
                    _ => result.ErrorMessage ?? "An error occurred during authorization."
                };
                MessageBox.Show(message, "Authorization Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                ShowDisconnectedState();
            }
        }
        catch (OperationCanceledException)
        {
            // User cancelled
            ShowDisconnectedState();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"OAuth failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ShowDisconnectedState();
        }
        finally
        {
            _azureDevOpsOAuthService.DeviceFlowStatusChanged -= OnDeviceFlowStatusChanged;
            AzureDevOpsOAuthButton.IsEnabled = true;
        }
    }

    private void AzureDevOpsCancelOAuth_Click(object sender, RoutedEventArgs e)
    {
        _azureDevOpsOAuthCancellationTokenSource?.Cancel();
    }

    private void AzureDevOpsCopyCode_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(AzureDevOpsUserCodeText.Text);
        AzureDevOpsCopyCodeButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new FluentIcons.Wpf.SymbolIcon { Symbol = FluentIcons.Common.Symbol.Checkmark, FontSize = 14, Margin = new Thickness(0, 0, 6, 0) },
                new TextBlock { Text = "Copied!" }
            }
        };

        // Reset after 2 seconds
        Task.Delay(2000).ContinueWith(_ => Dispatcher.Invoke(() =>
        {
            AzureDevOpsCopyCodeButton.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new FluentIcons.Wpf.SymbolIcon { Symbol = FluentIcons.Common.Symbol.Copy, FontSize = 14, Margin = new Thickness(0, 0, 6, 0) },
                    new TextBlock { Text = "Copy Code" }
                }
            };
        }));
    }

    private void AzureDevOpsVerificationUri_Click(object sender, MouseButtonEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = AzureDevOpsVerificationUriText.Text,
            UseShellExecute = true
        });
    }

    private void OnDeviceFlowStatusChanged(object? sender, EntraDeviceFlowEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            AzureDevOpsOAuthStatusMessage.Text = e.Message ?? e.Status.ToString();
        });
    }

    private void AzureDevOpsDisconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null || _credentialService == null || _settingsService == null)
            return;

        var result = MessageBox.Show(
            "Are you sure you want to disconnect Azure DevOps?",
            "Disconnect Azure DevOps",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _credentialService.DeleteCredential("AzureDevOps");
            _credentialService.DeleteRefreshToken("AzureDevOps");
            _settings.AzureDevOpsUserDisplayName = null;
            _settings.AzureDevOpsOAuthTokenCreatedAt = null;
            _settings.AzureDevOpsOAuthTokenExpiresAt = null;
            _settings.AzureDevOpsOAuthScopes = null;
            _settingsService.SaveSettings(_settings);

            ShowDisconnectedState();
        }
    }

    #endregion

    #region UI State Management

    private void ShowConnectedState(string displayName, AzureDevOpsAuthMethod authMethod)
    {
        var name = string.IsNullOrEmpty(displayName) ? "Azure DevOps User" : displayName;
        ImageSource? identiconSource = null;

        // Try to find the IdenticonConverter from the parent window's resources
        if (OwnerWindow != null)
        {
            var identiconConverter = OwnerWindow.TryFindResource("IdenticonConverter") as Converters.IdenticonConverter;
            if (identiconConverter != null)
            {
                identiconSource = identiconConverter.Convert(name, typeof(ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture) as ImageSource;
            }
        }

        // Show both sections, hide OAuth flow panel
        AzureDevOpsOAuthSection.Visibility = Visibility.Visible;
        AzureDevOpsPatSection.Visibility = Visibility.Visible;
        AzureDevOpsOAuthFlowPanel.Visibility = Visibility.Collapsed;

        if (authMethod == AzureDevOpsAuthMethod.OAuth)
        {
            // Show OAuth as connected
            AzureDevOpsOAuthAvatar.Visibility = Visibility.Visible;
            AzureDevOpsOAuthIdenticonImage.Source = identiconSource;
            AzureDevOpsOAuthStatusText.Text = $"Connected as {name}";
            AzureDevOpsOAuthStatusText.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
            AzureDevOpsOAuthButton.Visibility = Visibility.Collapsed;
            AzureDevOpsOAuthDisconnectButton.Visibility = Visibility.Visible;

            // Reset PAT section
            AzureDevOpsPatAvatar.Visibility = Visibility.Collapsed;
            AzureDevOpsPatStatusText.Text = "Use a PAT for authentication";
            AzureDevOpsPatStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            AzureDevOpsPatDisconnectButton.Visibility = Visibility.Collapsed;
            AzureDevOpsPatInputPanel.Visibility = Visibility.Visible;
        }
        else // PAT
        {
            // Show PAT as connected
            AzureDevOpsPatAvatar.Visibility = Visibility.Visible;
            AzureDevOpsPatIdenticonImage.Source = identiconSource;
            AzureDevOpsPatStatusText.Text = $"Connected as {name}";
            AzureDevOpsPatStatusText.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
            AzureDevOpsPatDisconnectButton.Visibility = Visibility.Visible;
            AzureDevOpsPatInputPanel.Visibility = Visibility.Collapsed;

            // Reset OAuth section
            AzureDevOpsOAuthAvatar.Visibility = Visibility.Collapsed;
            AzureDevOpsOAuthStatusText.Text = "Securely authenticate using your browser";
            AzureDevOpsOAuthStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            AzureDevOpsOAuthButton.Visibility = Visibility.Visible;
            AzureDevOpsOAuthDisconnectButton.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowDisconnectedState()
    {
        // Show both sections, hide OAuth flow panel
        AzureDevOpsOAuthSection.Visibility = Visibility.Visible;
        AzureDevOpsPatSection.Visibility = Visibility.Visible;
        AzureDevOpsOAuthFlowPanel.Visibility = Visibility.Collapsed;

        // Reset OAuth section
        AzureDevOpsOAuthAvatar.Visibility = Visibility.Collapsed;
        AzureDevOpsOAuthStatusText.Text = "Securely authenticate using your browser";
        AzureDevOpsOAuthStatusText.Foreground = new SolidColorBrush(Colors.Gray);
        AzureDevOpsOAuthButton.Visibility = Visibility.Visible;
        AzureDevOpsOAuthDisconnectButton.Visibility = Visibility.Collapsed;

        // Reset PAT section
        AzureDevOpsPatAvatar.Visibility = Visibility.Collapsed;
        AzureDevOpsPatStatusText.Text = "Use a PAT for authentication";
        AzureDevOpsPatStatusText.Foreground = new SolidColorBrush(Colors.Gray);
        AzureDevOpsPatDisconnectButton.Visibility = Visibility.Collapsed;
        AzureDevOpsPatInputPanel.Visibility = Visibility.Visible;

        // Clear PAT fields
        PatPasswordBox.Password = "";
        PatTextBox.Text = "";
    }

    #endregion
}
