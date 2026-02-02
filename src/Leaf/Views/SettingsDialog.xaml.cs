using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentIcons.Common;
using Leaf.Constants;
using Leaf.Models;
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
    private bool _isOllamaConnected;
    private bool _suppressAiSelectionSync;
    private bool _suppressNavSelection;
    private readonly OllamaService _ollamaService = new();

    // GitHub OAuth
    private GitHubOAuthService? _gitHubOAuthService;
    private CancellationTokenSource? _gitHubOAuthCancellationTokenSource;

    // Azure DevOps OAuth
    private AzureDevOpsOAuthService? _azureDevOpsOAuthService;
    private CancellationTokenSource? _azureDevOpsOAuthCancellationTokenSource;


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
            new("Terminal", "Configure the built-in command terminal", "Terminal", Symbol.Code),
            new("Terminal Shell", "Select which shell to run commands with", "Terminal", Symbol.Code),
            new("Terminal Output", "Control terminal output behavior", "Terminal", Symbol.Code),
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
        ContentTerminal.Visibility = Visibility.Collapsed;
        ContentAzureDevOps.Visibility = Visibility.Collapsed;
        ContentGitHub.Visibility = Visibility.Collapsed;
        ContentAIGeneral.Visibility = Visibility.Collapsed;
        ContentClaude.Visibility = Visibility.Collapsed;
        ContentGemini.Visibility = Visibility.Collapsed;
        ContentCodex.Visibility = Visibility.Collapsed;
        ContentOllama.Visibility = Visibility.Collapsed;
        ContentGitFlow.Visibility = Visibility.Collapsed;
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
            case "Terminal":
                ContentTerminal.Visibility = Visibility.Visible;
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
            case "Ollama":
                ContentOllama.Visibility = Visibility.Visible;
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
            case "GitFlow":
                ContentGitFlow.Visibility = Visibility.Visible;
                LoadGitFlowDefaults();
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
        ContentTerminal.Visibility = Visibility.Collapsed;
        ContentAzureDevOps.Visibility = Visibility.Collapsed;
        ContentGitHub.Visibility = Visibility.Collapsed;
        ContentAIGeneral.Visibility = Visibility.Collapsed;
        ContentClaude.Visibility = Visibility.Collapsed;
        ContentGemini.Visibility = Visibility.Collapsed;
        ContentCodex.Visibility = Visibility.Collapsed;
        ContentOllama.Visibility = Visibility.Collapsed;

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
            "Terminal" => NavTerminal,
            "AzureDevOps" => NavAzureDevOps,
            "GitHub" => NavGitHub,
            "AIGeneral" => NavAIGeneral,
            "Claude" => NavClaude,
            "Gemini" => NavGemini,
            "Codex" => NavCodex,
            "Ollama" => NavOllama,
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

    private async void OllamaRefreshModels_Click(object sender, RoutedEventArgs e)
    {
        var baseUrl = OllamaBaseUrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "http://localhost:11434";
            OllamaBaseUrlTextBox.Text = baseUrl;
        }

        OllamaRefreshModelsButton.IsEnabled = false;
        OllamaStatusText.Text = "Fetching models...";
        OllamaStatusText.Foreground = new SolidColorBrush(Colors.Gray);

        var (success, models, error) = await _ollamaService.GetAvailableModelsAsync(baseUrl);

        if (success && models.Count > 0)
        {
            OllamaModelComboBox.Items.Clear();
            foreach (var model in models)
            {
                OllamaModelComboBox.Items.Add(model);
            }

            // Auto-select first model or restore previous selection
            var savedModel = _settings.OllamaSelectedModel;
            if (!string.IsNullOrEmpty(savedModel) && models.Contains(savedModel))
            {
                OllamaModelComboBox.SelectedItem = savedModel;
            }
            else if (models.Count > 0)
            {
                OllamaModelComboBox.SelectedIndex = 0;
            }

            OllamaStatusText.Text = $"Found {models.Count} model(s)";
            OllamaStatusText.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
        }
        else
        {
            OllamaModelComboBox.Items.Clear();
            OllamaStatusText.Text = error ?? "Failed to connect";
            OllamaStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        }

        OllamaRefreshModelsButton.IsEnabled = true;
    }

    private async void OllamaConnect_Click(object sender, RoutedEventArgs e)
    {
        var baseUrl = OllamaBaseUrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "http://localhost:11434";
            OllamaBaseUrlTextBox.Text = baseUrl;
        }

        var selectedModel = OllamaModelComboBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(selectedModel))
        {
            // Try to fetch models first
            OllamaConnectButton.IsEnabled = false;
            OllamaStatusText.Text = "Connecting...";
            OllamaStatusText.Foreground = new SolidColorBrush(Colors.Gray);

            var (success, models, error) = await _ollamaService.GetAvailableModelsAsync(baseUrl);

            if (!success || models.Count == 0)
            {
                OllamaStatusText.Text = error ?? "No models available";
                OllamaStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                OllamaConnectButton.IsEnabled = true;
                return;
            }

            OllamaModelComboBox.Items.Clear();
            foreach (var model in models)
            {
                OllamaModelComboBox.Items.Add(model);
            }
            OllamaModelComboBox.SelectedIndex = 0;
            selectedModel = models[0];
        }

        // Validate connection by fetching models
        OllamaConnectButton.IsEnabled = false;
        OllamaStatusText.Text = "Validating connection...";
        OllamaStatusText.Foreground = new SolidColorBrush(Colors.Gray);

        var (validateSuccess, _, validateError) = await _ollamaService.GetAvailableModelsAsync(baseUrl);

        if (validateSuccess)
        {
            _isOllamaConnected = true;
            _settings.OllamaBaseUrl = baseUrl;
            _settings.OllamaSelectedModel = selectedModel;
            _settingsService.SaveSettings(_settings);

            OllamaStatusText.Text = $"Connected - {selectedModel}";
            OllamaStatusText.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
            OllamaConnectButton.IsEnabled = false;
            OllamaDisconnectButton.IsEnabled = true;
            UpdateAiDefaults();
        }
        else
        {
            OllamaStatusText.Text = validateError ?? "Connection failed";
            OllamaStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            OllamaConnectButton.IsEnabled = true;
        }
    }

    private void OllamaDisconnect_Click(object sender, RoutedEventArgs e)
    {
        _isOllamaConnected = false;
        _settings.OllamaSelectedModel = string.Empty;
        _settingsService.SaveSettings(_settings);

        OllamaStatusText.Text = "Not connected";
        OllamaStatusText.Foreground = new SolidColorBrush(Colors.Gray);
        OllamaConnectButton.IsEnabled = true;
        OllamaDisconnectButton.IsEnabled = false;
        UpdateAiDefaults();
    }

    private void LoadCurrentSettings()
    {
        // Load default clone path
        ClonePathTextBox.Text = _settings.DefaultClonePath;
        TerminalShellExecutableTextBox.Text = _settings.TerminalShellExecutable;
        TerminalShellArgumentsTextBox.Text = _settings.TerminalShellArguments;
        TerminalMaxLinesTextBox.Text = _settings.TerminalMaxLines.ToString();
        TerminalFontSizeTextBox.Text = _settings.TerminalFontSize.ToString("0");
        TerminalAutoScrollCheckBox.IsChecked = _settings.TerminalAutoScroll;
        TerminalLogGitCommandsCheckBox.IsChecked = _settings.TerminalLogGitCommands;

        // Load organization
        OrganizationTextBox.Text = _settings.AzureDevOpsOrganization;
        AiTimeoutTextBox.Text = _settings.AiCliTimeoutSeconds.ToString();

        // Check if Azure DevOps credential exists
        var existingAzureDevOpsToken = _credentialService.GetCredential("AzureDevOps");
        if (!string.IsNullOrEmpty(existingAzureDevOpsToken))
        {
            // Show connected state based on auth method
            ShowAzureDevOpsConnectedState(_settings.AzureDevOpsUserDisplayName ?? _settings.AzureDevOpsOrganization, _settings.AzureDevOpsAuthMethod);
        }
        else
        {
            // Show disconnected state
            ShowAzureDevOpsDisconnectedState();
        }

        var existingGitHubToken = _credentialService.GetCredential("GitHub");
        if (!string.IsNullOrEmpty(existingGitHubToken))
        {
            // Show connected state
            ShowGitHubConnectedState(_settings.GitHubUsername, _settings.GitHubAuthMethod);
        }
        else
        {
            // Show auth method panel
            ShowGitHubDisconnectedState();
        }

        _isClaudeConnected = _settings.IsClaudeConnected;
        _isGeminiConnected = _settings.IsGeminiConnected;
        _isCodexConnected = _settings.IsCodexConnected;
        _isOllamaConnected = !string.IsNullOrEmpty(_settings.OllamaSelectedModel);

        ApplyAiConnectionState(ClaudeStatusText, ClaudeConnectButton, ClaudeDisconnectButton, _isClaudeConnected);
        ApplyAiConnectionState(GeminiStatusText, GeminiConnectButton, GeminiDisconnectButton, _isGeminiConnected);
        ApplyAiConnectionState(CodexStatusText, CodexConnectButton, CodexDisconnectButton, _isCodexConnected);

        // Load Ollama settings
        OllamaBaseUrlTextBox.Text = _settings.OllamaBaseUrl;
        if (!string.IsNullOrEmpty(_settings.OllamaSelectedModel))
        {
            OllamaModelComboBox.Items.Clear();
            OllamaModelComboBox.Items.Add(_settings.OllamaSelectedModel);
            OllamaModelComboBox.SelectedIndex = 0;
            OllamaStatusText.Text = $"Connected - {_settings.OllamaSelectedModel}";
            OllamaStatusText.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
            OllamaConnectButton.IsEnabled = false;
            OllamaDisconnectButton.IsEnabled = true;
        }

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
        if (_isOllamaConnected)
            AiDefaultComboBox.Items.Add("Ollama");

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
        ShowAzureDevOpsConnectedState(organization, AzureDevOpsAuthMethod.PersonalAccessToken);
    }

    private void SaveGitHubPat_Click(object sender, RoutedEventArgs e)
    {
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
        ShowGitHubConnectedState(null, GitHubAuthMethod.PersonalAccessToken);
    }

    #region GitHub OAuth

    private async void GitHubOAuth_Click(object sender, RoutedEventArgs e)
    {
        _gitHubOAuthService ??= new GitHubOAuthService();
        _gitHubOAuthService.DeviceFlowStatusChanged += OnGitHubDeviceFlowStatusChanged;

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
                ShowGitHubConnectedState(username, GitHubAuthMethod.OAuth, userInfo?.AvatarUrl);
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
                ShowGitHubDisconnectedState();
            }
        }
        catch (OperationCanceledException)
        {
            // User cancelled
            ShowGitHubDisconnectedState();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"OAuth failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ShowGitHubDisconnectedState();
        }
        finally
        {
            _gitHubOAuthService.DeviceFlowStatusChanged -= OnGitHubDeviceFlowStatusChanged;
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

    private void OnGitHubDeviceFlowStatusChanged(object? sender, DeviceFlowEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            GitHubOAuthStatusMessage.Text = e.Message ?? e.Status.ToString();
        });
    }

    private void GitHubDisconnect_Click(object sender, RoutedEventArgs e)
    {
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

            ShowGitHubDisconnectedState();
        }
    }

    private void ShowGitHubConnectedState(string? username, GitHubAuthMethod authMethod, string? avatarUrl = null)
    {
        // For PAT, we don't have a username - just show "Connected"
        var displayName = string.IsNullOrEmpty(username) ? null : username;
        ImageSource? identiconSource = null;
        if (displayName != null)
        {
            var identiconConverter = (Converters.IdenticonConverter)FindResource("IdenticonConverter");
            identiconSource = identiconConverter.Convert(displayName, typeof(ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture) as ImageSource;
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

    private void ShowGitHubDisconnectedState()
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

    #region Azure DevOps OAuth

    private async void AzureDevOpsOAuth_Click(object sender, RoutedEventArgs e)
    {
        var organization = OrganizationTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(organization))
        {
            MessageBox.Show("Please enter an organization name first.", "Missing Organization",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _azureDevOpsOAuthService ??= new AzureDevOpsOAuthService();
        _azureDevOpsOAuthService.DeviceFlowStatusChanged += OnAzureDevOpsDeviceFlowStatusChanged;

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
                ShowAzureDevOpsConnectedState(displayName, AzureDevOpsAuthMethod.OAuth);
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
                ShowAzureDevOpsDisconnectedState();
            }
        }
        catch (OperationCanceledException)
        {
            // User cancelled
            ShowAzureDevOpsDisconnectedState();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"OAuth failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ShowAzureDevOpsDisconnectedState();
        }
        finally
        {
            _azureDevOpsOAuthService.DeviceFlowStatusChanged -= OnAzureDevOpsDeviceFlowStatusChanged;
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

    private void OnAzureDevOpsDeviceFlowStatusChanged(object? sender, EntraDeviceFlowEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            AzureDevOpsOAuthStatusMessage.Text = e.Message ?? e.Status.ToString();
        });
    }

    private void AzureDevOpsDisconnect_Click(object sender, RoutedEventArgs e)
    {
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

            ShowAzureDevOpsDisconnectedState();
        }
    }

    private void ShowAzureDevOpsConnectedState(string displayName, AzureDevOpsAuthMethod authMethod)
    {
        var name = string.IsNullOrEmpty(displayName) ? "Azure DevOps User" : displayName;
        var identiconConverter = (Converters.IdenticonConverter)FindResource("IdenticonConverter");
        var identiconSource = identiconConverter.Convert(name, typeof(ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture) as ImageSource;

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

    private void ShowAzureDevOpsDisconnectedState()
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
        // Update settings from UI
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
        _settings.AzureDevOpsOrganization = OrganizationTextBox.Text.Trim();
        _settings.DefaultAiProvider = AiDefaultComboBox.SelectedItem as string ?? string.Empty;

        if (int.TryParse(AiTimeoutTextBox.Text, out var timeoutSeconds) && timeoutSeconds > 0)
        {
            _settings.AiCliTimeoutSeconds = timeoutSeconds;
        }

        _settings.IsClaudeConnected = _isClaudeConnected;
        _settings.IsGeminiConnected = _isGeminiConnected;
        _settings.IsCodexConnected = _isCodexConnected;

        // Save Ollama settings
        _settings.OllamaBaseUrl = OllamaBaseUrlTextBox.Text.Trim();
        if (_isOllamaConnected && OllamaModelComboBox.SelectedItem is string selectedModel)
        {
            _settings.OllamaSelectedModel = selectedModel;
        }

        // Save GitFlow defaults
        SaveGitFlowDefaults();

        // Save all settings
        _settingsService.SaveSettings(_settings);

        DialogResult = true;
        Close();
    }

    #region GitFlow Defaults

    private void LoadGitFlowDefaults()
    {
        // Load defaults from settings
        GitFlowDefaultMainBranch.Text = _settings.GitFlowDefaultMainBranch;
        GitFlowDefaultDevelopBranch.Text = _settings.GitFlowDefaultDevelopBranch;
        GitFlowDefaultFeaturePrefix.Text = _settings.GitFlowDefaultFeaturePrefix;
        GitFlowDefaultReleasePrefix.Text = _settings.GitFlowDefaultReleasePrefix;
        GitFlowDefaultHotfixPrefix.Text = _settings.GitFlowDefaultHotfixPrefix;
        GitFlowDefaultVersionTagPrefix.Text = _settings.GitFlowDefaultVersionTagPrefix;
        GitFlowDefaultDeleteBranch.IsChecked = _settings.GitFlowDefaultDeleteBranch;
        GitFlowDefaultGenerateChangelog.IsChecked = _settings.GitFlowDefaultGenerateChangelog;
    }

    private void SaveGitFlowDefaults()
    {
        // Save defaults to settings
        _settings.GitFlowDefaultMainBranch = GitFlowDefaultMainBranch.Text;
        _settings.GitFlowDefaultDevelopBranch = GitFlowDefaultDevelopBranch.Text;
        _settings.GitFlowDefaultFeaturePrefix = GitFlowDefaultFeaturePrefix.Text;
        _settings.GitFlowDefaultReleasePrefix = GitFlowDefaultReleasePrefix.Text;
        _settings.GitFlowDefaultHotfixPrefix = GitFlowDefaultHotfixPrefix.Text;
        _settings.GitFlowDefaultVersionTagPrefix = GitFlowDefaultVersionTagPrefix.Text;
        _settings.GitFlowDefaultDeleteBranch = GitFlowDefaultDeleteBranch.IsChecked == true;
        _settings.GitFlowDefaultGenerateChangelog = GitFlowDefaultGenerateChangelog.IsChecked == true;
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
