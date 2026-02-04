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
/// Settings control for GitHub authentication (OAuth and PAT).
/// </summary>
public partial class GitHubSettingsControl : UserControl, ISettingsSectionControl
{
    private AppSettings? _settings;
    private CredentialService? _credentialService;
    private SettingsService? _settingsService;

    private bool _isGitHubPatVisible;
    private bool _suppressGitHubPatSync;

    // GitHub OAuth
    private GitHubOAuthService? _gitHubOAuthService;
    private CancellationTokenSource? _gitHubOAuthCancellationTokenSource;

    /// <summary>
    /// Gets or sets the owner window for OAuth popups.
    /// </summary>
    public Window? OwnerWindow { get; set; }

    public GitHubSettingsControl()
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

        var existingGitHubToken = credentialService.GetCredential("GitHub");
        if (!string.IsNullOrEmpty(existingGitHubToken))
        {
            // Show connected state
            ShowConnectedState(settings.GitHubUsername, settings.GitHubAuthMethod);
        }
        else
        {
            // Show auth method panel
            ShowDisconnectedState();
        }
    }

    public void SaveSettings(AppSettings settings, CredentialService credentialService)
    {
        // GitHub settings are saved immediately when connecting, nothing to do here
    }

    #region PAT Handling

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

    private void SaveGitHubPat_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null || _credentialService == null || _settingsService == null)
            return;

        var pat = _isGitHubPatVisible ? GitHubPatTextBox.Text : GitHubPatPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(pat))
        {
            MessageBox.Show("Please enter a PAT before saving.", "No PAT Entered",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // GitHub PAT uses "x-access-token" as username (the actual username doesn't matter)
        _credentialService.SaveCredential("GitHub", "x-access-token", pat);
        _settings.GitHubAuthMethod = GitHubAuthMethod.PersonalAccessToken;
        _settingsService.SaveSettings(_settings);

        // Clear PAT fields
        GitHubPatPasswordBox.Password = "";
        GitHubPatTextBox.Text = "";

        // Show connected state
        ShowConnectedState(null, GitHubAuthMethod.PersonalAccessToken);
    }

    #endregion

    #region OAuth

    private async void GitHubOAuth_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null || _credentialService == null || _settingsService == null)
            return;

        _gitHubOAuthService ??= new GitHubOAuthService();
        _gitHubOAuthService.DeviceFlowStatusChanged += OnDeviceFlowStatusChanged;

        try
        {
            // Show OAuth flow panel, hide other sections
            GitHubOAuthSection.Visibility = Visibility.Collapsed;
            GitHubPatSection.Visibility = Visibility.Collapsed;
            GitHubOAuthFlowPanel.Visibility = Visibility.Visible;
            GitHubOAuthButton.IsEnabled = false;

            // Start device flow
            var deviceCode = await _gitHubOAuthService.StartDeviceFlowAsync();

            // Display user code
            GitHubUserCodeText.Text = deviceCode.UserCode;
            GitHubVerificationUriText.Text = deviceCode.VerificationUri;

            // Open browser automatically
            Process.Start(new ProcessStartInfo
            {
                FileName = deviceCode.VerificationUri,
                UseShellExecute = true
            });

            // Start polling
            _gitHubOAuthCancellationTokenSource = new CancellationTokenSource();
            var result = await _gitHubOAuthService.PollForAccessTokenAsync(
                deviceCode.DeviceCode,
                deviceCode.Interval,
                deviceCode.ExpiresIn,
                _gitHubOAuthCancellationTokenSource.Token);

            if (result.Success && !string.IsNullOrEmpty(result.AccessToken))
            {
                // Get user info
                var userInfo = await _gitHubOAuthService.GetUserInfoAsync(result.AccessToken);
                var username = userInfo?.Login ?? "oauth-user";

                // Save token
                _credentialService.SaveCredential("GitHub", username, result.AccessToken);

                // Update settings
                _settings.GitHubAuthMethod = GitHubAuthMethod.OAuth;
                _settings.GitHubUsername = username;
                _settings.GitHubOAuthTokenCreatedAt = DateTime.UtcNow;
                _settings.GitHubOAuthScopes = result.Scope;
                _settingsService.SaveSettings(_settings);

                // Update UI
                ShowConnectedState(username, GitHubAuthMethod.OAuth, userInfo?.AvatarUrl);
            }
            else
            {
                // Show error
                var message = result.Error switch
                {
                    OAuthError.AccessDenied => "You denied the authorization request.",
                    OAuthError.ExpiredToken => "The authorization timed out. Please try again.",
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
            _gitHubOAuthService.DeviceFlowStatusChanged -= OnDeviceFlowStatusChanged;
            GitHubOAuthButton.IsEnabled = true;
        }
    }

    private void GitHubCancelOAuth_Click(object sender, RoutedEventArgs e)
    {
        _gitHubOAuthCancellationTokenSource?.Cancel();
    }

    private void GitHubCopyCode_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(GitHubUserCodeText.Text);
        GitHubCopyCodeButton.Content = new StackPanel
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
            GitHubCopyCodeButton.Content = new StackPanel
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

    private void GitHubVerificationUri_Click(object sender, MouseButtonEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = GitHubVerificationUriText.Text,
            UseShellExecute = true
        });
    }

    private void OnDeviceFlowStatusChanged(object? sender, DeviceFlowEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            GitHubOAuthStatusMessage.Text = e.Message ?? e.Status.ToString();
        });
    }

    private void GitHubDisconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null || _credentialService == null || _settingsService == null)
            return;

        var result = MessageBox.Show(
            "Are you sure you want to disconnect GitHub?",
            "Disconnect GitHub",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _credentialService.DeleteCredential("GitHub");
            _settings.GitHubUsername = string.Empty;
            _settings.GitHubOAuthTokenCreatedAt = null;
            _settings.GitHubOAuthScopes = null;
            _settingsService.SaveSettings(_settings);

            ShowDisconnectedState();
        }
    }

    #endregion

    #region UI State Management

    private void ShowConnectedState(string? username, GitHubAuthMethod authMethod, string? avatarUrl = null)
    {
        // For PAT, we don't have a username - just show "Connected"
        var displayName = string.IsNullOrEmpty(username) ? null : username;
        ImageSource? identiconSource = null;

        if (displayName != null && OwnerWindow != null)
        {
            var identiconConverter = OwnerWindow.TryFindResource("IdenticonConverter") as Converters.IdenticonConverter;
            if (identiconConverter != null)
            {
                identiconSource = identiconConverter.Convert(displayName, typeof(ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture) as ImageSource;
            }
        }

        // Show both sections, hide OAuth flow panel
        GitHubOAuthSection.Visibility = Visibility.Visible;
        GitHubPatSection.Visibility = Visibility.Visible;
        GitHubOAuthFlowPanel.Visibility = Visibility.Collapsed;

        if (authMethod == GitHubAuthMethod.OAuth)
        {
            // Show OAuth as connected
            GitHubOAuthAvatar.Visibility = Visibility.Visible;
            GitHubOAuthIdenticonImage.Source = identiconSource;
            GitHubOAuthStatusText.Text = $"Connected as {displayName}";
            GitHubOAuthStatusText.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
            GitHubOAuthButton.Visibility = Visibility.Collapsed;
            GitHubOAuthDisconnectButton.Visibility = Visibility.Visible;

            // Load avatar if available
            if (!string.IsNullOrEmpty(avatarUrl))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(avatarUrl);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    GitHubOAuthAvatarImage.Source = bitmap;
                }
                catch
                {
                    GitHubOAuthAvatarImage.Source = null;
                }
            }

            // Reset PAT section
            GitHubPatAvatar.Visibility = Visibility.Collapsed;
            GitHubPatStatusText.Text = "Use a PAT for authentication";
            GitHubPatStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            GitHubPatDisconnectButton.Visibility = Visibility.Collapsed;
            GitHubPatInputPanel.Visibility = Visibility.Visible;
        }
        else // PAT
        {
            // Show PAT as connected (no username for PAT)
            GitHubPatAvatar.Visibility = Visibility.Collapsed; // No avatar for PAT
            GitHubPatStatusText.Text = "Connected";
            GitHubPatStatusText.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
            GitHubPatDisconnectButton.Visibility = Visibility.Visible;
            GitHubPatInputPanel.Visibility = Visibility.Collapsed;

            // Reset OAuth section
            GitHubOAuthAvatar.Visibility = Visibility.Collapsed;
            GitHubOAuthAvatarImage.Source = null;
            GitHubOAuthStatusText.Text = "Securely authenticate using your browser";
            GitHubOAuthStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            GitHubOAuthButton.Visibility = Visibility.Visible;
            GitHubOAuthDisconnectButton.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowDisconnectedState()
    {
        // Show both sections, hide OAuth flow panel
        GitHubOAuthSection.Visibility = Visibility.Visible;
        GitHubPatSection.Visibility = Visibility.Visible;
        GitHubOAuthFlowPanel.Visibility = Visibility.Collapsed;

        // Reset OAuth section
        GitHubOAuthAvatar.Visibility = Visibility.Collapsed;
        GitHubOAuthAvatarImage.Source = null;
        GitHubOAuthStatusText.Text = "Securely authenticate using your browser";
        GitHubOAuthStatusText.Foreground = new SolidColorBrush(Colors.Gray);
        GitHubOAuthButton.Visibility = Visibility.Visible;
        GitHubOAuthDisconnectButton.Visibility = Visibility.Collapsed;

        // Reset PAT section
        GitHubPatAvatar.Visibility = Visibility.Collapsed;
        GitHubPatStatusText.Text = "Use a PAT for authentication";
        GitHubPatStatusText.Foreground = new SolidColorBrush(Colors.Gray);
        GitHubPatDisconnectButton.Visibility = Visibility.Collapsed;
        GitHubPatInputPanel.Visibility = Visibility.Visible;

        // Clear PAT fields
        GitHubPatPasswordBox.Password = "";
        GitHubPatTextBox.Text = "";
    }

    #endregion
}
