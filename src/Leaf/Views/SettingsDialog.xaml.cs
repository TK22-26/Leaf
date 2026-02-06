using System.Windows;
using System.Windows.Controls;
using FluentIcons.Common;
using Leaf.Models;
using Leaf.Services;
using Microsoft.Win32;

namespace Leaf.Views;

/// <summary>
/// Settings dialog for configuring application options.
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly CredentialService _credentialService;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private bool _suppressNavSelection;

    // Search items for settings
    private readonly List<SettingsSearchItem> _allSearchItems;

    public SettingsDialog(CredentialService credentialService, SettingsService settingsService)
    {
        InitializeComponent();

        _credentialService = credentialService;
        _settingsService = settingsService;
        _settings = settingsService.LoadSettings();

        // Initialize search items
        _allSearchItems = new List<SettingsSearchItem>
        {
            new("Clone Path", "Default directory for cloning repositories", "ClonePath", Symbol.Folder),
            new("Default Clone Directory", "Set where new repositories are cloned", "ClonePath", Symbol.Folder),
            new("Watched Folders", "Auto-discover new Git repositories in monitored folders", "WatchedFolders", Symbol.Eye),
            new("Repository Discovery", "Automatically detect new repositories", "WatchedFolders", Symbol.Eye),
            new("Terminal", "Configure the built-in command terminal", "Terminal", Symbol.Code),
            new("Terminal Shell", "Select which shell to run commands with", "Terminal", Symbol.Code),
            new("Terminal Output", "Control terminal output behavior", "Terminal", Symbol.Code),
            new("Clear Credentials", "Remove all stored PATs and tokens", "AuthGeneral", Symbol.Key),
            new("Azure DevOps", "Connect to Azure DevOps for private repositories", "AzureDevOps", Symbol.Cloud),
            new("Azure DevOps PAT", "Personal Access Token for Azure DevOps", "AzureDevOps", Symbol.Key),
            new("Azure DevOps Organization", "Your Azure DevOps organization name", "AzureDevOps", Symbol.Cloud),
            new("GitHub", "Connect to GitHub for private repositories", "GitHub", Symbol.Code),
            new("GitHub PAT", "Personal Access Token for GitHub", "GitHub", Symbol.Key),
            new("GitHub Username", "Your GitHub username or email", "GitHub", Symbol.Code),
            new("AI Settings", "Configure AI integration settings", "AIGeneral", Symbol.Bot),
            new("Default AI Provider", "Select which AI to use by default", "AIGeneral", Symbol.Bot),
            new("CLI Timeout", "Maximum time to wait for AI CLI responses", "AIGeneral", Symbol.Options),
            new("Claude", "Connect to Claude CLI for AI features", "Claude", Symbol.Bot),
            new("Gemini", "Connect to Gemini CLI for AI features", "Gemini", Symbol.Bot),
            new("Codex", "Connect to Codex CLI for AI features", "Codex", Symbol.Bot),
            new("Ollama", "Connect to Ollama for local AI features", "Ollama", Symbol.Bot),
            new("Local LLM", "Run AI locally with Ollama", "Ollama", Symbol.Bot),
            new("GitFlow", "Configure GitFlow default settings", "GitFlow", Symbol.Flow),
            new("Remotes", "Configure multi-remote sync behavior", "Remotes", Symbol.Cloud),
            new("Sync All Remotes", "Push and pull to all remotes automatically", "Remotes", Symbol.Cloud),
            new("Multi-Remote", "Settings for repositories with multiple remotes", "Remotes", Symbol.Cloud),
        };

        // Configure UserControls
        AzureDevOpsSettings.OwnerWindow = this;
        AzureDevOpsSettings.SetSettingsService(settingsService);

        GitHubSettings.OwnerWindow = this;
        GitHubSettings.SetSettingsService(settingsService);

        AiSettings.SetSettingsService(settingsService);

        LoadCurrentSettings();

        // Select first item by default
        NavClonePath.IsSelected = true;
    }

    private void SettingsNavTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressNavSelection) return;

        if (e.NewValue is TreeViewItem item && item.Tag is string tag)
        {
            ShowContent(tag);
        }
    }

    private void ShowContent(string tag)
    {
        // Hide all content panels
        ContentClonePath.Visibility = Visibility.Collapsed;
        ContentWatchedFolders.Visibility = Visibility.Collapsed;
        ContentRemotes.Visibility = Visibility.Collapsed;
        ContentTerminal.Visibility = Visibility.Collapsed;
        ContentAuthGeneral.Visibility = Visibility.Collapsed;
        AzureDevOpsSettings.Visibility = Visibility.Collapsed;
        GitHubSettings.Visibility = Visibility.Collapsed;
        AiSettings.Visibility = Visibility.Collapsed;
        GitFlowSettings.Visibility = Visibility.Collapsed;
        ContentSearchResults.Visibility = Visibility.Collapsed;

        // Show the selected content
        switch (tag)
        {
            case "ClonePath":
                ContentClonePath.Visibility = Visibility.Visible;
                break;
            case "WatchedFolders":
                ContentWatchedFolders.Visibility = Visibility.Visible;
                LoadWatchedFolders();
                break;
            case "Remotes":
                ContentRemotes.Visibility = Visibility.Visible;
                break;
            case "Terminal":
                ContentTerminal.Visibility = Visibility.Visible;
                break;
            case "AuthGeneral":
                ContentAuthGeneral.Visibility = Visibility.Visible;
                break;
            case "AzureDevOps":
                AzureDevOpsSettings.Visibility = Visibility.Visible;
                break;
            case "GitHub":
                GitHubSettings.Visibility = Visibility.Visible;
                break;
            case "AIGeneral":
            case "Claude":
            case "Gemini":
            case "Codex":
            case "Ollama":
                AiSettings.Visibility = Visibility.Visible;
                AiSettings.ShowSection(tag);
                break;
            case "General":
                // Show clone path for General category
                ContentClonePath.Visibility = Visibility.Visible;
                break;
            case "Authentication":
                // Show Auth General for Authentication category
                ContentAuthGeneral.Visibility = Visibility.Visible;
                break;
            case "AI":
                // Show AI General for AI category
                AiSettings.Visibility = Visibility.Visible;
                AiSettings.ShowSection("AIGeneral");
                break;
            case "GitFlow":
                GitFlowSettings.Visibility = Visibility.Visible;
                break;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = SearchBox.Text.Trim();

        if (string.IsNullOrEmpty(searchText))
        {
            // Clear search, show normal navigation
            ContentSearchResults.Visibility = Visibility.Collapsed;
            SettingsNavTree.Visibility = Visibility.Visible;

            // Show previously selected content
            if (SettingsNavTree.SelectedItem is TreeViewItem item && item.Tag is string tag)
            {
                ShowContent(tag);
            }
            return;
        }

        // Perform search
        var results = _allSearchItems
            .Where(item => item.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                          item.Description.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Hide nav tree, show search results
        SettingsNavTree.Visibility = Visibility.Collapsed;

        // Hide all content panels
        ContentClonePath.Visibility = Visibility.Collapsed;
        ContentWatchedFolders.Visibility = Visibility.Collapsed;
        ContentRemotes.Visibility = Visibility.Collapsed;
        ContentTerminal.Visibility = Visibility.Collapsed;
        ContentAuthGeneral.Visibility = Visibility.Collapsed;
        AzureDevOpsSettings.Visibility = Visibility.Collapsed;
        GitHubSettings.Visibility = Visibility.Collapsed;
        AiSettings.Visibility = Visibility.Collapsed;
        GitFlowSettings.Visibility = Visibility.Collapsed;

        // Show search results
        ContentSearchResults.Visibility = Visibility.Visible;
        SearchResultsItemsControl.ItemsSource = results;
        NoSearchResultsText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tag)
        {
            // Clear search
            SearchBox.Text = "";
            SettingsNavTree.Visibility = Visibility.Visible;
            ContentSearchResults.Visibility = Visibility.Collapsed;

            // Navigate to the item
            _suppressNavSelection = true;
            SelectNavItem(tag);
            _suppressNavSelection = false;

            ShowContent(tag);
        }
    }

    private void SelectNavItem(string tag)
    {
        TreeViewItem? itemToSelect = tag switch
        {
            "ClonePath" => NavClonePath,
            "WatchedFolders" => NavWatchedFolders,
            "Remotes" => NavRemotes,
            "Terminal" => NavTerminal,
            "AuthGeneral" => NavAuthGeneral,
            "AzureDevOps" => NavAzureDevOps,
            "GitHub" => NavGitHub,
            "AIGeneral" => NavAIGeneral,
            "Claude" => NavClaude,
            "Gemini" => NavGemini,
            "Codex" => NavCodex,
            "Ollama" => NavOllama,
            "GitFlow" => NavGitFlow,
            _ => null
        };

        if (itemToSelect != null)
        {
            itemToSelect.IsSelected = true;
            itemToSelect.BringIntoView();
        }
    }

    private void LoadCurrentSettings()
    {
        // Load local settings
        ClonePathTextBox.Text = _settings.DefaultClonePath;
        TerminalShellExecutableTextBox.Text = _settings.TerminalShellExecutable;
        TerminalShellArgumentsTextBox.Text = _settings.TerminalShellArguments;
        TerminalMaxLinesTextBox.Text = _settings.TerminalMaxLines.ToString();
        TerminalFontSizeTextBox.Text = _settings.TerminalFontSize.ToString("0");
        TerminalAutoScrollCheckBox.IsChecked = _settings.TerminalAutoScroll;
        TerminalLogGitCommandsCheckBox.IsChecked = _settings.TerminalLogGitCommands;
        SyncAllRemotesCheckBox.IsChecked = _settings.SyncAllRemotes;

        // Load settings into UserControls
        AzureDevOpsSettings.LoadSettings(_settings, _credentialService);
        GitHubSettings.LoadSettings(_settings, _credentialService);
        AiSettings.LoadSettings(_settings, _credentialService);
        GitFlowSettings.LoadSettings(_settings, _credentialService);
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
        // Update local settings from UI
        _settings.DefaultClonePath = ClonePathTextBox.Text;
        var shellExecutable = TerminalShellExecutableTextBox.Text.Trim();
        var shellArguments = TerminalShellArgumentsTextBox.Text.Trim();
        _settings.TerminalShellExecutable = string.IsNullOrWhiteSpace(shellExecutable) ? "cmd.exe" : shellExecutable;
        _settings.TerminalShellArguments = string.IsNullOrWhiteSpace(shellArguments) ? "/c {command}" : shellArguments;
        if (int.TryParse(TerminalMaxLinesTextBox.Text, out var maxLines) && maxLines > 0)
        {
            _settings.TerminalMaxLines = maxLines;
        }
        if (double.TryParse(TerminalFontSizeTextBox.Text, out var fontSize) && fontSize > 6)
        {
            _settings.TerminalFontSize = fontSize;
        }
        _settings.TerminalAutoScroll = TerminalAutoScrollCheckBox.IsChecked == true;
        _settings.TerminalLogGitCommands = TerminalLogGitCommandsCheckBox.IsChecked == true;
        _settings.SyncAllRemotes = SyncAllRemotesCheckBox.IsChecked == true;

        // Save settings from UserControls
        AzureDevOpsSettings.SaveSettings(_settings, _credentialService);
        GitHubSettings.SaveSettings(_settings, _credentialService);
        AiSettings.SaveSettings(_settings, _credentialService);
        GitFlowSettings.SaveSettings(_settings, _credentialService);

        // Save all settings
        _settingsService.SaveSettings(_settings);

        DialogResult = true;
        Close();
    }

    private void ClearAllCredentials_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Remove all stored credentials (PATs and tokens)?\n\nYou will need to re-enter them in the Azure DevOps and GitHub settings.",
            "Clear All Credentials",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        // Remove all Leaf credentials from Windows Credential Manager
        var allKeys = _credentialService.GetStoredOrganizations().ToList();
        foreach (var key in allKeys)
        {
            _credentialService.RemovePat(key);
        }

        // Also clean up any refresh tokens from old OAuth flow
        _credentialService.DeleteRefreshToken("GitHub");
        _credentialService.DeleteRefreshToken("AzureDevOps");

        // Refresh the Azure DevOps and GitHub settings UI
        AzureDevOpsSettings.LoadSettings(_settings, _credentialService);
        GitHubSettings.LoadSettings(_settings, _credentialService);

        MessageBox.Show(
            "All credentials have been cleared.\n\nRe-enter your PATs under Azure DevOps and GitHub settings.",
            "Credentials Cleared",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    #region Watched Folders

    private void LoadWatchedFolders()
    {
        WatchedFoldersListBox.ItemsSource = _settings.WatchedFolders;
        WatchedFoldersListBox.SelectionChanged += WatchedFoldersListBox_SelectionChanged;
    }

    private void WatchedFoldersListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RemoveWatchedFolderButton.IsEnabled = WatchedFoldersListBox.SelectedItem != null;
    }

    private void AddWatchedFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Folder to Watch for Git Repositories"
        };

        if (dialog.ShowDialog() == true)
        {
            var folderPath = dialog.FolderName;

            // Check if already watched
            if (_settings.WatchedFolders.Contains(folderPath, StringComparer.OrdinalIgnoreCase))
            {
                MessageBox.Show("This folder is already being watched.", "Already Watched",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Add to settings
            _settings.WatchedFolders.Add(folderPath);
            _settingsService.SaveSettings(_settings);

            // Refresh the list
            WatchedFoldersListBox.ItemsSource = null;
            WatchedFoldersListBox.ItemsSource = _settings.WatchedFolders;
        }
    }

    private void RemoveWatchedFolder_Click(object sender, RoutedEventArgs e)
    {
        if (WatchedFoldersListBox.SelectedItem is string selectedFolder)
        {
            _settings.WatchedFolders.Remove(selectedFolder);
            _settingsService.SaveSettings(_settings);

            // Refresh the list
            WatchedFoldersListBox.ItemsSource = null;
            WatchedFoldersListBox.ItemsSource = _settings.WatchedFolders;
            RemoveWatchedFolderButton.IsEnabled = false;
        }
    }

    #endregion
}

/// <summary>
/// Represents a searchable settings item.
/// </summary>
public class SettingsSearchItem
{
    public string Title { get; }
    public string Description { get; }
    public string Tag { get; }
    public Symbol Icon { get; }

    public SettingsSearchItem(string title, string description, string tag, Symbol icon)
    {
        Title = title;
        Description = description;
        Tag = tag;
        Icon = icon;
    }
}
