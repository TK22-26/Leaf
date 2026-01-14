using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluentIcons.Common;
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
    private bool _isClaudeConnected;
    private bool _isGeminiConnected;
    private bool _isCodexConnected;
    private bool _suppressAiSelectionSync;
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
        };

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
        ContentAzureDevOps.Visibility = Visibility.Collapsed;
        ContentGitHub.Visibility = Visibility.Collapsed;
        ContentAIGeneral.Visibility = Visibility.Collapsed;
        ContentClaude.Visibility = Visibility.Collapsed;
        ContentGemini.Visibility = Visibility.Collapsed;
        ContentCodex.Visibility = Visibility.Collapsed;
        ContentSearchResults.Visibility = Visibility.Collapsed;

        // Show the selected content
        switch (tag)
        {
            case "ClonePath":
                ContentClonePath.Visibility = Visibility.Visible;
                break;
            case "AzureDevOps":
                ContentAzureDevOps.Visibility = Visibility.Visible;
                break;
            case "GitHub":
                ContentGitHub.Visibility = Visibility.Visible;
                break;
            case "AIGeneral":
                ContentAIGeneral.Visibility = Visibility.Visible;
                break;
            case "Claude":
                ContentClaude.Visibility = Visibility.Visible;
                break;
            case "Gemini":
                ContentGemini.Visibility = Visibility.Visible;
                break;
            case "Codex":
                ContentCodex.Visibility = Visibility.Visible;
                break;
            case "General":
                // Show clone path for General category
                ContentClonePath.Visibility = Visibility.Visible;
                break;
            case "Authentication":
                // Show Azure DevOps for Authentication category
                ContentAzureDevOps.Visibility = Visibility.Visible;
                break;
            case "AI":
                // Show AI General for AI category
                ContentAIGeneral.Visibility = Visibility.Visible;
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
        ContentAzureDevOps.Visibility = Visibility.Collapsed;
        ContentGitHub.Visibility = Visibility.Collapsed;
        ContentAIGeneral.Visibility = Visibility.Collapsed;
        ContentClaude.Visibility = Visibility.Collapsed;
        ContentGemini.Visibility = Visibility.Collapsed;
        ContentCodex.Visibility = Visibility.Collapsed;

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
            "AzureDevOps" => NavAzureDevOps,
            "GitHub" => NavGitHub,
            "AIGeneral" => NavAIGeneral,
            "Claude" => NavClaude,
            "Gemini" => NavGemini,
            "Codex" => NavCodex,
            _ => null
        };

        if (itemToSelect != null)
        {
            itemToSelect.IsSelected = true;
            itemToSelect.BringIntoView();
        }
    }

    private async void ClaudeConnect_Click(object sender, RoutedEventArgs e)
    {
        await CheckCliIntegrationAsync("claude", ClaudeStatusText, ClaudeConnectButton, () =>
        {
            _isClaudeConnected = true;
            ClaudeDisconnectButton.IsEnabled = true;
        });
    }

    private void ClaudeDisconnect_Click(object sender, RoutedEventArgs e)
    {
        _isClaudeConnected = false;
        ClaudeStatusText.Text = "Not connected";
        ClaudeStatusText.Foreground = new SolidColorBrush(Colors.Gray);
        ClaudeConnectButton.IsEnabled = true;
        ClaudeDisconnectButton.IsEnabled = false;
        _settings.IsClaudeConnected = false;
        _settingsService.SaveSettings(_settings);
        UpdateAiDefaults();
    }

    private async void GeminiConnect_Click(object sender, RoutedEventArgs e)
    {
        await CheckCliIntegrationAsync("gemini", GeminiStatusText, GeminiConnectButton, () =>
        {
            _isGeminiConnected = true;
            GeminiDisconnectButton.IsEnabled = true;
        });
    }

    private void GeminiDisconnect_Click(object sender, RoutedEventArgs e)
    {
        _isGeminiConnected = false;
        GeminiStatusText.Text = "Not connected";
        GeminiStatusText.Foreground = new SolidColorBrush(Colors.Gray);
        GeminiConnectButton.IsEnabled = true;
        GeminiDisconnectButton.IsEnabled = false;
        _settings.IsGeminiConnected = false;
        _settingsService.SaveSettings(_settings);
        UpdateAiDefaults();
    }

    private async void CodexConnect_Click(object sender, RoutedEventArgs e)
    {
        await CheckCliIntegrationAsync("codex", CodexStatusText, CodexConnectButton, () =>
        {
            _isCodexConnected = true;
            CodexDisconnectButton.IsEnabled = true;
        });
    }

    private void CodexDisconnect_Click(object sender, RoutedEventArgs e)
    {
        _isCodexConnected = false;
        CodexStatusText.Text = "Not connected";
        CodexStatusText.Foreground = new SolidColorBrush(Colors.Gray);
        CodexConnectButton.IsEnabled = true;
        CodexDisconnectButton.IsEnabled = false;
        _settings.IsCodexConnected = false;
        _settingsService.SaveSettings(_settings);
        UpdateAiDefaults();
    }

    private void LoadCurrentSettings()
    {
        // Load default clone path
        ClonePathTextBox.Text = _settings.DefaultClonePath;

        // Load organization
        OrganizationTextBox.Text = _settings.AzureDevOpsOrganization;
        GitHubUsernameTextBox.Text = _settings.GitHubUsername;
        AiTimeoutTextBox.Text = _settings.AiCliTimeoutSeconds.ToString();

        // Check if PAT exists
        var existingPat = _credentialService.GetCredential("AzureDevOps");
        if (!string.IsNullOrEmpty(existingPat))
        {
            // Show dots to indicate PAT exists (not the actual PAT)
            _suppressPatSync = true;
            PatPasswordBox.Password = "••••••••••••••••";
            PatTextBox.Text = "••••••••••••••••";
            _suppressPatSync = false;

            PatStatusText.Text = "Connected";
            PatStatusText.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
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
            // Show dots to indicate PAT exists (not the actual PAT)
            _suppressGitHubPatSync = true;
            GitHubPatPasswordBox.Password = "••••••••••••••••";
            GitHubPatTextBox.Text = "••••••••••••••••";
            _suppressGitHubPatSync = false;

            GitHubStatusText.Text = "Connected";
            GitHubStatusText.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
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

        _isClaudeConnected = _settings.IsClaudeConnected;
        _isGeminiConnected = _settings.IsGeminiConnected;
        _isCodexConnected = _settings.IsCodexConnected;

        ApplyAiConnectionState(ClaudeStatusText, ClaudeConnectButton, ClaudeDisconnectButton, _isClaudeConnected);
        ApplyAiConnectionState(GeminiStatusText, GeminiConnectButton, GeminiDisconnectButton, _isGeminiConnected);
        ApplyAiConnectionState(CodexStatusText, CodexConnectButton, CodexDisconnectButton, _isCodexConnected);

        UpdateAiDefaults();
    }

    private async Task CheckCliIntegrationAsync(string command, TextBlock statusText, Button actionButton, Action? onConnected)
    {
        if (statusText == null || actionButton == null)
            return;

        actionButton.IsEnabled = false;
        statusText.Text = "Checking...";
        statusText.Foreground = new SolidColorBrush(Colors.Gray);

        var timeoutSeconds = GetAiTimeoutSeconds();
        var (result, detail) = await Task.Run(() => TryRunCli(command, timeoutSeconds));

        switch (result)
        {
            case CliCheckResult.Connected:
                statusText.Text = "Connected";
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
                actionButton.IsEnabled = false;
                onConnected?.Invoke();
                switch (command.ToLowerInvariant())
                {
                    case "claude":
                        _settings.IsClaudeConnected = _isClaudeConnected;
                        break;
                    case "gemini":
                        _settings.IsGeminiConnected = _isGeminiConnected;
                        break;
                    case "codex":
                        _settings.IsCodexConnected = _isCodexConnected;
                        break;
                }
                _settingsService.SaveSettings(_settings);
                UpdateAiDefaults();
                break;
            case CliCheckResult.NotInstalled:
                statusText.Text = string.IsNullOrWhiteSpace(detail) ? "Not installed" : $"Not installed: {detail}";
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                actionButton.IsEnabled = true;
                break;
            case CliCheckResult.NotConnected:
                statusText.Text = string.IsNullOrWhiteSpace(detail) ? "Not connected" : $"Not connected: {detail}";
                statusText.Foreground = new SolidColorBrush(Colors.Gray);
                actionButton.IsEnabled = true;
                break;
            default:
                statusText.Text = string.IsNullOrWhiteSpace(detail) ? "No response" : $"No response: {detail}";
                statusText.Foreground = new SolidColorBrush(Colors.Gray);
                actionButton.IsEnabled = true;
                break;
        }
    }

    private static (CliCheckResult result, string detail) TryRunCli(string command, int timeoutSeconds)
    {
        string[] argsCandidates = command.ToLowerInvariant() switch
        {
            "codex" => new[] { "exec -m gpt-5.1-codex-mini --skip-git-repo-check \"ping\"" },
            "claude" => new[] { "-p \"ping\" --model sonnet" },
            "gemini" => new[] { "-p \"ping\"" },
            _ => new[] { "-p \"ping\"" }
        };

        var (resolvedPath, combinedPath) = ResolveCommandPath(command);

        foreach (var args in argsCandidates)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = resolvedPath ?? command,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                if (!string.IsNullOrWhiteSpace(combinedPath))
                {
                    psi.Environment["PATH"] = combinedPath;
                }

                using var process = Process.Start(psi);
                if (process == null)
                {
                    continue;
                }

                if (!process.WaitForExit(Math.Max(1, timeoutSeconds) * 1000))
                {
                    try { process.Kill(); } catch { }
                    return (CliCheckResult.Unknown, $"timed out after {Math.Max(1, timeoutSeconds)}s");
                }

                var output = (process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd()).Trim();
                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    return (CliCheckResult.Connected, string.Empty);
                }

                if (output.Contains("login", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("authenticate", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("auth", StringComparison.OrdinalIgnoreCase))
                {
                    return (CliCheckResult.NotConnected, $"auth required (exit {process.ExitCode})");
                }
                if (process.ExitCode != 0)
                {
                    var detail = string.IsNullOrWhiteSpace(output) ? $"exit {process.ExitCode}" : $"exit {process.ExitCode}: {TrimDetail(output)}";
                    return (CliCheckResult.NotConnected, detail);
                }
                if (!string.IsNullOrWhiteSpace(output))
                {
                    return (CliCheckResult.NotConnected, TrimDetail(output));
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return (CliCheckResult.NotInstalled, "command not found on PATH");
            }
            catch
            {
                return (CliCheckResult.Unknown, "exception");
            }
        }

        return (CliCheckResult.NotConnected, "no output");
    }

    private static string TrimDetail(string detail)
    {
        var compact = detail.Replace("\r", " ").Replace("\n", " ");
        return compact.Length <= 120 ? compact : compact[..120] + "...";
    }

    private int GetAiTimeoutSeconds()
    {
        if (AiTimeoutTextBox == null)
            return _settings.AiCliTimeoutSeconds;

        if (int.TryParse(AiTimeoutTextBox.Text, out var value) && value > 0)
        {
            _settings.AiCliTimeoutSeconds = value;
            _settingsService.SaveSettings(_settings);
            return value;
        }

        return _settings.AiCliTimeoutSeconds;
    }

    private static (string? fullPath, string? combinedPath) ResolveCommandPath(string command)
    {
        var paths = new List<string>();
        var processPath = Environment.GetEnvironmentVariable("PATH");
        var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
        var machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);

        if (!string.IsNullOrWhiteSpace(processPath))
            paths.Add(processPath);
        if (!string.IsNullOrWhiteSpace(userPath))
            paths.Add(userPath);
        if (!string.IsNullOrWhiteSpace(machinePath))
            paths.Add(machinePath);

        var combinedPath = string.Join(";", paths);
        var searchPaths = combinedPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var extensions = Path.HasExtension(command) ? new[] { string.Empty } : new[] { ".exe", ".cmd", ".bat" };
        foreach (var dir in searchPaths)
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, command + ext);
                if (File.Exists(candidate))
                {
                    return (candidate, combinedPath);
                }
            }
        }

        return (null, combinedPath);
    }

    private enum CliCheckResult
    {
        Connected,
        NotInstalled,
        NotConnected,
        Unknown
    }

    private void UpdateAiDefaults()
    {
        if (AiDefaultComboBox == null)
            return;

        _suppressAiSelectionSync = true;
        var previous = AiDefaultComboBox.SelectedItem as string;
        AiDefaultComboBox.Items.Clear();

        if (_isClaudeConnected)
            AiDefaultComboBox.Items.Add("Claude");
        if (_isGeminiConnected)
            AiDefaultComboBox.Items.Add("Gemini");
        if (_isCodexConnected)
            AiDefaultComboBox.Items.Add("Codex");

        if (AiDefaultComboBox.Items.Count == 0)
        {
            AiDefaultComboBox.IsEnabled = false;
            _suppressAiSelectionSync = false;
            return;
        }

        AiDefaultComboBox.IsEnabled = true;
        var saved = string.IsNullOrWhiteSpace(_settings.DefaultAiProvider) ? null : _settings.DefaultAiProvider;
        if (saved != null && AiDefaultComboBox.Items.Contains(saved))
        {
            AiDefaultComboBox.SelectedItem = saved;
        }
        else if (previous != null && AiDefaultComboBox.Items.Contains(previous))
        {
            AiDefaultComboBox.SelectedItem = previous;
        }
        else
        {
            AiDefaultComboBox.SelectedIndex = 0;
        }
        _suppressAiSelectionSync = false;
    }

    private void AiDefaultComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAiSelectionSync || AiDefaultComboBox.SelectedItem is not string selected)
            return;

        _settings.DefaultAiProvider = selected;
        _settingsService.SaveSettings(_settings);
    }

    private static void ApplyAiConnectionState(TextBlock status, Button connect, Button disconnect, bool isConnected)
    {
        if (isConnected)
        {
            status.Text = "Connected";
            status.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
            connect.IsEnabled = false;
            disconnect.IsEnabled = true;
        }
        else
        {
            status.Text = "Not connected";
            status.Foreground = new SolidColorBrush(Colors.Gray);
            connect.IsEnabled = true;
            disconnect.IsEnabled = false;
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
        PatStatusText.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
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
        GitHubStatusText.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
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

        var defaultAi = AiDefaultComboBox.SelectedItem as string ?? string.Empty;
        if (_settings.DefaultAiProvider != defaultAi)
        {
            _settings.DefaultAiProvider = defaultAi;
            changed = true;
        }

        if (int.TryParse(AiTimeoutTextBox.Text, out var timeoutSeconds) && timeoutSeconds > 0)
        {
            if (_settings.AiCliTimeoutSeconds != timeoutSeconds)
            {
                _settings.AiCliTimeoutSeconds = timeoutSeconds;
                changed = true;
            }
        }

        if (_settings.IsClaudeConnected != _isClaudeConnected)
        {
            _settings.IsClaudeConnected = _isClaudeConnected;
            changed = true;
        }
        if (_settings.IsGeminiConnected != _isGeminiConnected)
        {
            _settings.IsGeminiConnected = _isGeminiConnected;
            changed = true;
        }
        if (_settings.IsCodexConnected != _isCodexConnected)
        {
            _settings.IsCodexConnected = _isCodexConnected;
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
