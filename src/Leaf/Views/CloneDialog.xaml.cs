using System.IO;
using System.Windows;
using System.Windows.Controls;
using Leaf.Services;
using Microsoft.Win32;

namespace Leaf.Views;

/// <summary>
/// Clone repository dialog with GitHub and Azure DevOps browsing and PAT support.
/// </summary>
public partial class CloneDialog : Window
{
    private readonly IGitService _gitService;
    private readonly CredentialService _credentialService;
    private readonly AzureDevOpsService _azureDevOpsService;
    private readonly GitHubService _gitHubService;
    private readonly SettingsService _settingsService;
    private readonly string _defaultClonePath;
    private bool _isCloning;

    // Azure DevOps repos
    private List<AzureDevOpsRepo> _allRepos = [];
    private List<AzureDevOpsRepo> _filteredRepos = [];

    // GitHub repos
    private List<GitHubRepo> _allGitHubRepos = [];
    private List<GitHubRepo> _filteredGitHubRepos = [];

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
        _gitHubService = new GitHubService(credentialService);
        _defaultClonePath = defaultClonePath;

        DestinationTextBox.Text = defaultClonePath;
        UpdateCloneButtonState();

        // Load repos on startup (GitHub is first tab now)
        Loaded += async (s, e) => await LoadGitHubRepositoriesAsync();
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

    #region GitHub

    private async Task LoadGitHubRepositoriesAsync()
    {
        var pat = _credentialService.GetCredential("GitHub");

        if (string.IsNullOrEmpty(pat))
        {
            GitHubStatusText.Text = "Configure GitHub PAT in Settings";
            GitHubEmptyStateText.Text = "No GitHub PAT configured.\nGo to Settings to add your GitHub Personal Access Token.";
            GitHubEmptyStateText.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            GitHubLoadingOverlay.Visibility = Visibility.Visible;
            GitHubEmptyStateText.Visibility = Visibility.Collapsed;
            GitHubRefreshButton.IsEnabled = false;
            GitHubStatusText.Text = "Loading from GitHub...";

            _allGitHubRepos = await _gitHubService.GetRepositoriesAsync();
            _allGitHubRepos = _allGitHubRepos.OrderBy(r => r.DisplayName).ToList();

            FilterGitHubRepositories();

            GitHubStatusText.Text = $"{_allGitHubRepos.Count} repositories";

            if (_allGitHubRepos.Count == 0)
            {
                GitHubEmptyStateText.Text = "No repositories found.";
                GitHubEmptyStateText.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            GitHubStatusText.Text = "Failed to load repositories";
            GitHubEmptyStateText.Text = $"Error: {ex.Message}\n\nCheck your GitHub PAT in Settings.";
            GitHubEmptyStateText.Visibility = Visibility.Visible;
        }
        finally
        {
            GitHubLoadingOverlay.Visibility = Visibility.Collapsed;
            GitHubRefreshButton.IsEnabled = true;
        }
    }

    private void FilterGitHubRepositories()
    {
        var searchText = GitHubSearchTextBox.Text.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(searchText))
        {
            _filteredGitHubRepos = _allGitHubRepos.ToList();
        }
        else
        {
            _filteredGitHubRepos = _allGitHubRepos
                .Where(r => r.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                           r.RemoteUrl.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        GitHubRepoListBox.ItemsSource = _filteredGitHubRepos;

        if (_filteredGitHubRepos.Count == 0 && _allGitHubRepos.Count > 0)
        {
            GitHubEmptyStateText.Text = "No repositories match your search.";
            GitHubEmptyStateText.Visibility = Visibility.Visible;
        }
        else if (_allGitHubRepos.Count > 0)
        {
            GitHubEmptyStateText.Visibility = Visibility.Collapsed;
        }
    }

    private void GitHubSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Update placeholder visibility
        GitHubSearchPlaceholder.Visibility = string.IsNullOrEmpty(GitHubSearchTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        FilterGitHubRepositories();
    }

    private async void GitHubRefresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadGitHubRepositoriesAsync();
    }

    private void GitHubRepoListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GitHubRepoListBox.SelectedItem is GitHubRepo repo)
        {
            _selectedUrl = repo.RemoteUrl;
            SelectedUrlText.Text = repo.RemoteUrl;
            UpdateRepoName(_selectedUrl);
            UpdateCloneButtonState();
        }
    }

    #endregion

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

    private async void ModeTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;

        // Tab 0: GitHub, Tab 1: Azure DevOps, Tab 2: Enter URL
        switch (ModeTabControl.SelectedIndex)
        {
            case 0: // GitHub
                if (GitHubRepoListBox.SelectedItem is GitHubRepo ghRepo)
                {
                    _selectedUrl = ghRepo.RemoteUrl;
                }
                else
                {
                    _selectedUrl = string.Empty;
                }
                // Load GitHub repos if not already loaded
                if (_allGitHubRepos.Count == 0)
                {
                    await LoadGitHubRepositoriesAsync();
                }
                break;

            case 1: // Azure DevOps
                if (RepoListBox.SelectedItem is AzureDevOpsRepo adoRepo)
                {
                    _selectedUrl = adoRepo.RemoteUrl;
                }
                else
                {
                    _selectedUrl = string.Empty;
                }
                // Load Azure DevOps repos if not already loaded
                if (_allRepos.Count == 0)
                {
                    await LoadRepositoriesAsync();
                }
                break;

            case 2: // Enter URL
                _selectedUrl = UrlTextBox.Text.Trim();
                break;

            default:
                _selectedUrl = string.Empty;
                break;
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
            UrlHintText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
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
        else if (IsGitHubUrl(url))
        {
            var hasPat = !string.IsNullOrEmpty(_credentialService.GetCredential("GitHub"));
            UrlHintText.Text = hasPat
                ? "GitHub URL - will use saved PAT"
                : "GitHub URL - no PAT saved";
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

            // Get credentials based on URL type
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
            else if (IsGitHubUrl(url))
            {
                password = _credentialService.GetCredential("GitHub");
                if (!string.IsNullOrEmpty(password))
                {
                    // GitHub uses PAT as password with any username (or "x-access-token")
                    username = "x-access-token";
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

    private static bool IsGitHubUrl(string url)
    {
        return url.Contains("github.com", StringComparison.OrdinalIgnoreCase);
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
