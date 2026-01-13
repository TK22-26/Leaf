using System.Diagnostics;
using System.IO;
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
    private bool _isClaudeConnected;
    private bool _isGeminiConnected;
    private bool _isCodexConnected;
    private bool _suppressAiSelectionSync;

    public SettingsDialog(CredentialService credentialService, SettingsService settingsService)
    {
        InitializeComponent();

        _credentialService = credentialService;
        _settingsService = settingsService;
        _settings = settingsService.LoadSettings();

        LoadCurrentSettings();
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
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 150, 0));
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
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(180, 0, 0));
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
            status.Foreground = new SolidColorBrush(Color.FromRgb(0, 150, 0));
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
