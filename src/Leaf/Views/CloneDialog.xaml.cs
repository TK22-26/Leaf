using System.IO;
using System.Windows;
using System.Windows.Controls;
using Leaf.Services;
using Microsoft.Win32;

namespace Leaf.Views;

/// <summary>
/// Clone repository dialog with Azure DevOps browsing and PAT support.
/// </summary>
public partial class CloneDialog : Window
{
    private readonly IGitService _gitService;
    private readonly CredentialService _credentialService;
    private readonly AzureDevOpsService _azureDevOpsService;
    private readonly SettingsService _settingsService;
    private readonly string _defaultClonePath;
    private bool _isCloning;

    private List<AzureDevOpsRepo> _allRepos = [];
    private List<AzureDevOpsRepo> _filteredRepos = [];
    private string _selectedUrl = string.Empty;

    /// <summary>
    /// The path to the cloned repository (set after successful clone).
    /// </summary>
    public string? ClonedRepositoryPath { get; private set; }

    public CloneDialog(IGitService gitService, CredentialService credentialService, SettingsService settingsService, string defaultClonePath)
    {
        InitializeComponent();

        _gitService = gitService;
        _credentialService = credentialService;
        _settingsService = settingsService;
        _azureDevOpsService = new AzureDevOpsService(credentialService);
        _defaultClonePath = defaultClonePath;

        DestinationTextBox.Text = defaultClonePath;
        UpdateCloneButtonState();

        // Load repos on startup
        Loaded += async (s, e) => await LoadRepositoriesAsync();
    }

    private async Task LoadRepositoriesAsync()
    {
        var settings = _settingsService.LoadSettings();
        var organization = settings.AzureDevOpsOrganization;
        var pat = _credentialService.GetCredential("AzureDevOps");

        if (string.IsNullOrEmpty(organization) || string.IsNullOrEmpty(pat))
        {
            AzureStatusText.Text = "Configure organization and PAT in Settings";
            EmptyStateText.Text = "No organization or PAT configured.\nGo to Settings to add your Azure DevOps organization and PAT.";
            EmptyStateText.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            EmptyStateText.Visibility = Visibility.Collapsed;
            RefreshButton.IsEnabled = false;
            AzureStatusText.Text = $"Loading from {organization}...";

            _allRepos = await _azureDevOpsService.GetRepositoriesAsync(organization);
            _allRepos = _allRepos.OrderBy(r => r.DisplayName).ToList();

            FilterRepositories();

            AzureStatusText.Text = $"{_allRepos.Count} repositories in {organization}";

            if (_allRepos.Count == 0)
            {
                EmptyStateText.Text = "No repositories found in this organization.";
                EmptyStateText.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            AzureStatusText.Text = "Failed to load repositories";
            EmptyStateText.Text = $"Error: {ex.Message}\n\nCheck your organization name and PAT in Settings.";
            EmptyStateText.Visibility = Visibility.Visible;
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            RefreshButton.IsEnabled = true;
        }
    }

    private void FilterRepositories()
    {
        var searchText = SearchTextBox.Text.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(searchText))
        {
            _filteredRepos = _allRepos.ToList();
        }
        else
        {
            _filteredRepos = _allRepos
                .Where(r => r.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                           r.RemoteUrl.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        RepoListBox.ItemsSource = _filteredRepos;

        if (_filteredRepos.Count == 0 && _allRepos.Count > 0)
        {
            EmptyStateText.Text = "No repositories match your search.";
            EmptyStateText.Visibility = Visibility.Visible;
        }
        else if (_allRepos.Count > 0)
        {
            EmptyStateText.Visibility = Visibility.Collapsed;
        }
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Update placeholder visibility
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        FilterRepositories();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadRepositoriesAsync();
    }

    private void RepoListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RepoListBox.SelectedItem is AzureDevOpsRepo repo)
        {
            _selectedUrl = repo.RemoteUrl;
            SelectedUrlText.Text = repo.RemoteUrl;
            UpdateRepoName(_selectedUrl);
            UpdateCloneButtonState();
        }
    }

    private void ModeTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;

        // When switching to URL tab, use the URL from text box
        if (ModeTabControl.SelectedIndex == 1)
        {
            _selectedUrl = UrlTextBox.Text.Trim();
        }
        // When switching to Browse tab, use the selected repo
        else if (RepoListBox.SelectedItem is AzureDevOpsRepo repo)
        {
            _selectedUrl = repo.RemoteUrl;
        }
        else
        {
            _selectedUrl = string.Empty;
        }

        SelectedUrlText.Text = _selectedUrl;
        UpdateRepoName(_selectedUrl);
        UpdateCloneButtonState();
    }

    private void UrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var url = UrlTextBox.Text.Trim();
        _selectedUrl = url;
        SelectedUrlText.Text = url;

        // Update hints based on URL
        if (string.IsNullOrEmpty(url))
        {
            UrlHintText.Text = "Enter a Git repository URL (HTTPS)";
        }
        else if (IsAzureDevOpsUrl(url))
        {
            var hasPat = !string.IsNullOrEmpty(_credentialService.GetCredential("AzureDevOps"));
            UrlHintText.Text = hasPat
                ? "Azure DevOps URL - will use saved PAT"
                : "Azure DevOps URL - no PAT saved";
            UrlHintText.Foreground = hasPat
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 150, 0))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 100, 0));
        }
        else
        {
            UrlHintText.Text = "Git repository URL";
            UrlHintText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
        }

        UpdateRepoName(url);
        UpdateCloneButtonState();
    }

    private void DestinationTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateRepoName(_selectedUrl);
        UpdateCloneButtonState();
    }

    private void UpdateRepoName(string url)
    {
        var repoName = ExtractRepoName(url);
        if (!string.IsNullOrEmpty(repoName) && !string.IsNullOrEmpty(DestinationTextBox.Text))
        {
            var fullPath = Path.Combine(DestinationTextBox.Text, repoName);
            RepoNameText.Text = $"Will clone to: {fullPath}";
        }
        else
        {
            RepoNameText.Text = "";
        }
    }

    private void BrowseDestination_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Clone Destination",
            InitialDirectory = DestinationTextBox.Text
        };

        if (dialog.ShowDialog() == true)
        {
            DestinationTextBox.Text = dialog.FolderName;
        }
    }

    private async void Clone_Click(object sender, RoutedEventArgs e)
    {
        if (_isCloning) return;

        var url = _selectedUrl;
        var destination = DestinationTextBox.Text.Trim();
        var repoName = ExtractRepoName(url);

        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(destination) || string.IsNullOrEmpty(repoName))
        {
            MessageBox.Show("Please select a repository and destination.", "Invalid Input",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var localPath = Path.Combine(destination, repoName);

        if (Directory.Exists(localPath))
        {
            MessageBox.Show($"The folder '{localPath}' already exists.", "Folder Exists",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _isCloning = true;
            CloneButton.IsEnabled = false;
            CloneProgressBar.Visibility = Visibility.Visible;
            ProgressText.Text = "Cloning repository...";

            // Get credentials if Azure DevOps
            string? username = null;
            string? password = null;

            if (IsAzureDevOpsUrl(url))
            {
                password = _credentialService.GetCredential("AzureDevOps");
                if (!string.IsNullOrEmpty(password))
                {
                    username = "git";
                }
            }

            var progress = new Progress<string>(msg =>
            {
                ProgressText.Text = msg;
            });

            await _gitService.CloneAsync(url, localPath, username, password, progress);

            ClonedRepositoryPath = localPath;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ProgressText.Text = "";
            CloneProgressBar.Visibility = Visibility.Collapsed;

            var message = ex.Message;
            if (message.Contains("401") || message.Contains("403") || message.Contains("Authentication"))
            {
                message = "Authentication failed. Check your PAT in Settings.\n\n" + message;
            }

            MessageBox.Show($"Clone failed:\n\n{message}", "Clone Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isCloning = false;
            CloneButton.IsEnabled = true;
            CloneProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void UpdateCloneButtonState()
    {
        var destination = DestinationTextBox.Text.Trim();
        var repoName = ExtractRepoName(_selectedUrl);

        CloneButton.IsEnabled = !string.IsNullOrEmpty(_selectedUrl) &&
                                !string.IsNullOrEmpty(destination) &&
                                !string.IsNullOrEmpty(repoName) &&
                                !_isCloning;

        // Update URL display visibility
        SelectedUrlBorder.Visibility = string.IsNullOrEmpty(_selectedUrl)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static bool IsAzureDevOpsUrl(string url)
    {
        return url.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractRepoName(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            url = url.TrimEnd('/');
            if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                url = url[..^4];

            var lastSlash = url.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash < url.Length - 1)
            {
                return url[(lastSlash + 1)..];
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return null;
    }
}
