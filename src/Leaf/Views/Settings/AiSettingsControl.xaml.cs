using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Leaf.Models;
using Leaf.Services;
using Leaf.Services.Ai.Http;
using Leaf.Services.Merge;

namespace Leaf.Views.Settings;

/// <summary>
/// Settings control for AI integrations (Claude, Gemini, Codex, Ollama).
/// </summary>
public partial class AiSettingsControl : UserControl, ISettingsSectionControl
{
    private AppSettings? _settings;
    private SettingsService? _settingsService;

    private bool _isClaudeConnected;
    private bool _isClaudeApiConnected;
    private bool _isGeminiConnected;
    private bool _isGeminiApiConnected;
    private bool _isCodexConnected;
    private bool _isOpenAiApiConnected;
    private bool _isOpenAiCompatibleConnected;
    private bool _isOllamaConnected;
    private bool _suppressAiSelectionSync;
    private bool _suppressClaudeTransportSync;
    private bool _suppressGeminiTransportSync;

    private readonly OllamaService _ollamaService = new();
    private CredentialService? _credentialService;

    /// <summary>
    /// Curated model lists per provider. Seed the editable ComboBoxes so
    /// users get a one-click pick for cost optimization (haiku/flash/mini
    /// variants) while still being free to type a model identifier Leaf
    /// doesn't know about yet — useful when a provider ships a new model
    /// before Leaf updates this list.
    /// </summary>
    /// <remarks>
    /// Ordered roughly by "default recommendation" first. The user's
    /// saved choice always wins; this list only seeds the dropdown.
    /// </remarks>
    private static readonly string[] ClaudeApiModels =
    {
        "claude-sonnet-4-5",
        "claude-opus-4-7",
        "claude-haiku-4-5",
    };

    private static readonly string[] GeminiApiModels =
    {
        "gemini-2.5-pro",
        "gemini-2.5-flash",
        "gemini-2.5-flash-lite",
        "gemini-2.0-flash",
    };

    private static readonly string[] OpenAiApiModels =
    {
        "gpt-5-codex",
        "gpt-5",
        "gpt-5-mini",
        "gpt-5-nano",
        "o3",
        "o4-mini",
        "gpt-4o",
    };

    public AiSettingsControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Sets the settings service for saving settings during interactions.
    /// </summary>
    public void SetSettingsService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Optional callback invoked after the user saves a new API key or
    /// disconnects. Receives the credential-provider name
    /// ("Claude", "Gemini", "OpenAI", "OpenAiCompatible") so the host
    /// can invalidate the matching singleton's cached key — without
    /// this hook the next merge request would serve a stale key until
    /// app restart.
    /// </summary>
    public void SetApiKeyInvalidator(Action<string> invalidator)
    {
        _apiKeyInvalidator = invalidator;
    }

    private Action<string>? _apiKeyInvalidator;

    public void LoadSettings(AppSettings settings, CredentialService credentialService)
    {
        _settings = settings;
        _credentialService = credentialService;

        // Load timeout
        AiTimeoutTextBox.Text = settings.AiCliTimeoutSeconds.ToString();

        // Load AI Merge
        AiMergeEnabledCheckBox.IsChecked = settings.AiMergeEnabled;
        AiMergeExternalServerPathTextBox.Text = settings.AiMergeExternalServerPath ?? string.Empty;
        // Reset button is only meaningful when consent has been given.
        AiMergeResetConsentButton.IsEnabled = settings.AiMergeConsentGiven;
        // Provider dropdown populated AFTER connection-state flags below
        // are set, so the "(not connected)" suffix is accurate.

        // Load connection states
        _isClaudeConnected = settings.IsClaudeConnected;
        _isClaudeApiConnected = settings.IsClaudeApiConnected;
        _isGeminiConnected = settings.IsGeminiConnected;
        _isGeminiApiConnected = settings.IsGeminiApiConnected;
        _isCodexConnected = settings.IsCodexConnected;
        _isOpenAiApiConnected = settings.IsOpenAiApiConnected;
        _isOpenAiCompatibleConnected = settings.IsOpenAiCompatibleConnected;
        _isOllamaConnected = !string.IsNullOrEmpty(settings.OllamaSelectedModel);

        ApplyConnectionState(ClaudeStatusText, ClaudeConnectButton, ClaudeDisconnectButton, _isClaudeConnected);
        ApplyConnectionState(GeminiStatusText, GeminiConnectButton, GeminiDisconnectButton, _isGeminiConnected);
        ApplyConnectionState(CodexStatusText, CodexConnectButton, CodexDisconnectButton, _isCodexConnected);

        // Claude transport (CLI vs API) and the API body state. The
        // password field is intentionally left blank on load — we never
        // round-trip the plaintext key back to the UI. The status line
        // shows a masked tail if a key is currently stored.
        _suppressClaudeTransportSync = true;
        var transport = (settings.ClaudeTransport ?? "Cli").Trim();
        ClaudeTransportApiRadio.IsChecked = transport.Equals("Api", StringComparison.OrdinalIgnoreCase);
        ClaudeTransportCliRadio.IsChecked = !ClaudeTransportApiRadio.IsChecked.GetValueOrDefault();
        _suppressClaudeTransportSync = false;
        ApplyClaudeTransportVisibility();

        PopulateModelComboBox(ClaudeApiModelComboBox, ClaudeApiModels,
            string.IsNullOrWhiteSpace(settings.ClaudeApiModel) ? "claude-sonnet-4-5" : settings.ClaudeApiModel);
        UpdateClaudeApiStatusLine();

        // Gemini transport mirror.
        _suppressGeminiTransportSync = true;
        var geminiTransport = (settings.GeminiTransport ?? "Cli").Trim();
        GeminiTransportApiRadio.IsChecked = geminiTransport.Equals("Api", StringComparison.OrdinalIgnoreCase);
        GeminiTransportCliRadio.IsChecked = !GeminiTransportApiRadio.IsChecked.GetValueOrDefault();
        _suppressGeminiTransportSync = false;
        ApplyGeminiTransportVisibility();

        PopulateModelComboBox(GeminiApiModelComboBox, GeminiApiModels,
            string.IsNullOrWhiteSpace(settings.GeminiApiModel) ? "gemini-2.5-pro" : settings.GeminiApiModel);
        UpdateGeminiApiStatusLine();

        // OpenAI proper.
        PopulateModelComboBox(OpenAiApiModelComboBox, OpenAiApiModels,
            string.IsNullOrWhiteSpace(settings.OpenAiApiModel) ? "gpt-5-codex" : settings.OpenAiApiModel);
        UpdateOpenAiApiStatusLine();

        // OpenAI-compatible custom endpoint. No curated list — the model
        // identifier is endpoint-specific so we leave the dropdown items
        // empty and rely on the editable ComboBox for free-text entry.
        OpenAiCompatibleBaseUrlTextBox.Text = settings.OpenAiCompatibleBaseUrl ?? string.Empty;
        PopulateModelComboBox(OpenAiCompatibleModelComboBox, Array.Empty<string>(), settings.OpenAiCompatibleModel ?? string.Empty);
        UpdateOpenAiCompatibleStatusLine();

        // Load Ollama settings
        OllamaBaseUrlTextBox.Text = settings.OllamaBaseUrl;
        if (!string.IsNullOrEmpty(settings.OllamaSelectedModel))
        {
            OllamaModelComboBox.Items.Clear();
            OllamaModelComboBox.Items.Add(settings.OllamaSelectedModel);
            OllamaModelComboBox.SelectedIndex = 0;
            OllamaStatusText.Text = $"Connected - {settings.OllamaSelectedModel}";
            OllamaStatusText.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
            OllamaConnectButton.IsEnabled = false;
            OllamaDisconnectButton.IsEnabled = true;
        }

        UpdateAiDefaults();
        UpdateMergeProviderOptions();
    }

    /// <summary>
    /// Populate the merge-provider combo and select whatever the user
    /// has saved. Each provider's label includes a "(not connected)"
    /// suffix when its connection-state predicate is false, so the
    /// dropdown reads honestly without disabling rows (selecting an
    /// unavailable provider is the only way to surface the "go connect
    /// it" path through the existing settings UI).
    /// </summary>
    private void UpdateMergeProviderOptions()
    {
        if (_settings is null) return;
        _suppressAiSelectionSync = true;
        try
        {
            AiMergeProviderComboBox.Items.Clear();
            AiMergeProviderComboBox.Items.Add(BuildOption("Claude", _isClaudeConnected));
            AiMergeProviderComboBox.Items.Add(BuildOption("Claude (API)", _isClaudeApiConnected));
            AiMergeProviderComboBox.Items.Add(BuildOption("Gemini", _isGeminiConnected));
            AiMergeProviderComboBox.Items.Add(BuildOption("Gemini (API)", _isGeminiApiConnected));
            AiMergeProviderComboBox.Items.Add(BuildOption("Codex",  _isCodexConnected));
            AiMergeProviderComboBox.Items.Add(BuildOption("OpenAI (API)", _isOpenAiApiConnected));
            AiMergeProviderComboBox.Items.Add(BuildOption("OpenAI-Compatible", _isOpenAiCompatibleConnected));
            AiMergeProviderComboBox.Items.Add(BuildOption("Ollama", _isOllamaConnected));
            AiMergeProviderComboBox.Items.Add("External Server");

            // Map persisted setting → display label.
            var current = (_settings.AiMergeProvider ?? string.Empty).Trim();
            var target = current switch
            {
                "Claude" => BuildOption("Claude", _isClaudeConnected),
                "Claude (API)" or "ClaudeApi" => BuildOption("Claude (API)", _isClaudeApiConnected),
                "Gemini" => BuildOption("Gemini", _isGeminiConnected),
                "Gemini (API)" or "GeminiApi" => BuildOption("Gemini (API)", _isGeminiApiConnected),
                "Codex"  => BuildOption("Codex",  _isCodexConnected),
                "OpenAI (API)" or "OpenAi" or "OpenAI" => BuildOption("OpenAI (API)", _isOpenAiApiConnected),
                "OpenAI-Compatible" or "OpenAiCompatible" => BuildOption("OpenAI-Compatible", _isOpenAiCompatibleConnected),
                "Ollama" => BuildOption("Ollama", _isOllamaConnected),
                "ExternalServer" or "External" => "External Server",
                _ => DefaultProviderLabel(),
            };

            for (int i = 0; i < AiMergeProviderComboBox.Items.Count; i++)
            {
                if ((string)AiMergeProviderComboBox.Items[i]! == target)
                {
                    AiMergeProviderComboBox.SelectedIndex = i;
                    break;
                }
            }
        }
        finally { _suppressAiSelectionSync = false; }
    }

    private static string BuildOption(string name, bool connected)
        => connected ? name : $"{name} (not connected)";

    /// <summary>
    /// First-launch default provider — the user's commit-message
    /// provider if it's connected, otherwise the first connected
    /// CLI / Ollama in priority order, otherwise "External Server"
    /// as the always-present fallback.
    /// </summary>
    private string DefaultProviderLabel()
    {
        if (_settings is null) return "External Server";
        var preferred = (_settings.DefaultAiProvider ?? string.Empty).Trim();
        bool ConnectedFor(string name) => name switch
        {
            "Claude" => _isClaudeConnected,
            "Gemini" => _isGeminiConnected,
            "Codex"  => _isCodexConnected,
            "Ollama" => _isOllamaConnected,
            _ => false,
        };
        if (!string.IsNullOrEmpty(preferred) && ConnectedFor(preferred))
            return BuildOption(preferred, true);
        foreach (var name in new[] { "Claude", "Gemini", "Codex", "Ollama" })
            if (ConnectedFor(name)) return BuildOption(name, true);
        return "External Server";
    }

    private void AiMergeProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAiSelectionSync || _settings is null || _settingsService is null) return;
        var label = AiMergeProviderComboBox.SelectedItem as string ?? string.Empty;
        // Strip the "(not connected)" suffix when persisting; the
        // provider name is what AppSettings.AiMergeProvider stores.
        var name = label.Replace(" (not connected)", string.Empty).Trim();
        _settings.AiMergeProvider = name switch
        {
            "External Server" => "ExternalServer",
            _ => name,
        };
        _settingsService.SaveSettings(_settings);
    }

    public void SaveSettings(AppSettings settings, CredentialService credentialService)
    {
        // Save timeout
        if (int.TryParse(AiTimeoutTextBox.Text, out var timeoutSeconds) && timeoutSeconds > 0)
        {
            settings.AiCliTimeoutSeconds = timeoutSeconds;
        }

        // Save connection states
        settings.IsClaudeConnected = _isClaudeConnected;
        settings.IsClaudeApiConnected = _isClaudeApiConnected;
        settings.IsGeminiConnected = _isGeminiConnected;
        settings.IsGeminiApiConnected = _isGeminiApiConnected;
        settings.IsCodexConnected = _isCodexConnected;
        settings.IsOpenAiApiConnected = _isOpenAiApiConnected;
        settings.IsOpenAiCompatibleConnected = _isOpenAiCompatibleConnected;

        // Save default provider
        settings.DefaultAiProvider = AiDefaultComboBox.SelectedItem as string ?? string.Empty;

        // Save Ollama settings
        settings.OllamaBaseUrl = OllamaBaseUrlTextBox.Text.Trim();
        if (_isOllamaConnected && OllamaModelComboBox.SelectedItem is string selectedModel)
        {
            settings.OllamaSelectedModel = selectedModel;
        }
    }

    /// <summary>
    /// Shows a specific AI settings section.
    /// </summary>
    public void ShowSection(string section)
    {
        // Hide all sections
        ContentAIGeneral.Visibility = Visibility.Collapsed;
        ContentClaude.Visibility = Visibility.Collapsed;
        ContentGemini.Visibility = Visibility.Collapsed;
        ContentCodex.Visibility = Visibility.Collapsed;
        ContentOpenAi.Visibility = Visibility.Collapsed;
        ContentOpenAiCompatible.Visibility = Visibility.Collapsed;
        ContentOllama.Visibility = Visibility.Collapsed;
        ContentAiMerge.Visibility = Visibility.Collapsed;

        // Show requested section
        switch (section)
        {
            case "AIGeneral":
            case "AI":
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
            case "OpenAi":
                ContentOpenAi.Visibility = Visibility.Visible;
                break;
            case "OpenAiCompatible":
                ContentOpenAiCompatible.Visibility = Visibility.Visible;
                break;
            case "Ollama":
                ContentOllama.Visibility = Visibility.Visible;
                break;
            case "AiMerge":
                ContentAiMerge.Visibility = Visibility.Visible;
                break;
        }
    }

    #region AI Merge (Phase 5)

    private void AiMergeEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_settings is null || _settingsService is null) return;
        _settings.AiMergeEnabled = AiMergeEnabledCheckBox.IsChecked == true;
        _settingsService.SaveSettings(_settings);
    }

    private void AiMergeExternalServerPath_Changed(object sender, TextChangedEventArgs e)
    {
        if (_settings is null || _settingsService is null) return;
        _settings.AiMergeExternalServerPath = AiMergeExternalServerPathTextBox.Text.Trim();
        _settingsService.SaveSettings(_settings);
    }

    private void AiMergeExternalServerPathBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select external server executable",
            Filter = "Executables (*.exe;*.bat;*.cmd;*.ps1)|*.exe;*.bat;*.cmd;*.ps1|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true)
        {
            AiMergeExternalServerPathTextBox.Text = dlg.FileName;
            // TextChanged handler persists the change.
        }
    }

    private void AiMergeResetConsent_Click(object sender, RoutedEventArgs e)
    {
        if (_settings is null || _settingsService is null) return;
        _settings.AiMergeConsentGiven = false;
        _settingsService.SaveSettings(_settings);
        AiMergeResetConsentButton.IsEnabled = false;
    }

    #endregion

    #region Claude

    private async void ClaudeConnect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await CheckCliIntegrationAsync("claude", ClaudeStatusText, ClaudeConnectButton, () =>
            {
                _isClaudeConnected = true;
                ClaudeDisconnectButton.IsEnabled = true;
            });
        }
        catch (Exception ex)
        {
            AsyncErrorHandler.Handle(ex, nameof(ClaudeConnect_Click), isUserAction: true);
        }
    }

    private void ClaudeDisconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null || _settingsService == null) return;

        _isClaudeConnected = false;
        ClaudeStatusText.Text = "Not connected";
        ClaudeStatusText.Foreground = new SolidColorBrush(Colors.Gray);
        ClaudeConnectButton.IsEnabled = true;
        ClaudeDisconnectButton.IsEnabled = false;
        _settings.IsClaudeConnected = false;
        _settingsService.SaveSettings(_settings);
        UpdateAiDefaults();
    }

    // --- Claude API transport ---

    private void ClaudeTransport_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressClaudeTransportSync || _settings == null || _settingsService == null) return;
        _settings.ClaudeTransport = ClaudeTransportApiRadio.IsChecked == true ? "Api" : "Cli";
        _settingsService.SaveSettings(_settings);
        ApplyClaudeTransportVisibility();
    }

    private void ApplyClaudeTransportVisibility()
    {
        if (ClaudeCliBody == null || ClaudeApiBody == null) return;
        var api = ClaudeTransportApiRadio.IsChecked == true;
        ClaudeCliBody.Visibility = api ? Visibility.Collapsed : Visibility.Visible;
        ClaudeApiBody.Visibility = api ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClaudeApiModel_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) PersistClaudeApiModel();
    }

    private void ClaudeApiModel_LostFocus(object sender, RoutedEventArgs e) => PersistClaudeApiModel();

    private void PersistClaudeApiModel()
    {
        if (_settings == null || _settingsService == null) return;
        var model = (ClaudeApiModelComboBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(model)) return;
        _settings.ClaudeApiModel = model;
        _settingsService.SaveSettings(_settings);
    }

    private async void ClaudeApiSaveTest_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_settings == null || _settingsService == null || _credentialService == null) return;

            var typed = ClaudeApiKeyPasswordBox.Password ?? string.Empty;
            // Allow re-test without re-entering the key: if the field is
            // blank and we already have one stored, just re-validate.
            var existing = _credentialService.GetAiApiKey("Claude");
            var keyToTest = string.IsNullOrEmpty(typed) ? existing : typed;
            if (string.IsNullOrEmpty(keyToTest))
            {
                ClaudeApiStatusText.Text = "Enter an API key first.";
                ClaudeApiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                return;
            }

            var model = ClaudeApiModelComboBox.Text.Trim();
            if (string.IsNullOrEmpty(model))
            {
                ClaudeApiStatusText.Text = "Enter a model name first.";
                ClaudeApiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                return;
            }

            ClaudeApiSaveTestButton.IsEnabled = false;
            ClaudeApiStatusText.Text = "Testing…";
            ClaudeApiStatusText.Foreground = new SolidColorBrush(Colors.Gray);

            // If the user typed a new key, persist it before testing so
            // the test exercises exactly what subsequent merge calls
            // will use.
            if (!string.IsNullOrEmpty(typed))
            {
                _credentialService.SetAiApiKey("Claude", typed); _apiKeyInvalidator?.Invoke("Claude");
                // Clear the field so a shoulder-surfer can't read it from
                // the saved state. The status line shows a masked tail.
                ClaudeApiKeyPasswordBox.Clear();
            }
            _settings.ClaudeApiModel = model;
            _settingsService.SaveSettings(_settings);

            // Probe with a one-shot client — avoids needing the singleton
            // IAiApiClient to be wired into this control. This client is
            // discarded after the probe; the singleton is what actually
            // serves merge requests at runtime.
            var timeout = GetAiTimeoutSeconds();
            using var probeHttp = new HttpClient();
            var probe = new ClaudeApiClient(
                probeHttp,
                keyReader: () => keyToTest,
                modelProvider: () => model,
                timeoutSecondsProvider: () => timeout);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(2, timeout)));
            var error = await probe.TestConnectionAsync(cts.Token);

            if (error == null)
            {
                _isClaudeApiConnected = true;
                _settings.IsClaudeApiConnected = true;
                _settingsService.SaveSettings(_settings);
                UpdateClaudeApiStatusLine();
                ClaudeApiDisconnectButton.IsEnabled = true;
                UpdateAiDefaults();
            }
            else
            {
                _isClaudeApiConnected = false;
                _settings.IsClaudeApiConnected = false;
                _settingsService.SaveSettings(_settings);
                ClaudeApiStatusText.Text = TrimDetail(error);
                ClaudeApiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                ClaudeApiDisconnectButton.IsEnabled = _credentialService.HasAiApiKey("Claude");
            }
        }
        catch (Exception ex)
        {
            AsyncErrorHandler.Handle(ex, nameof(ClaudeApiSaveTest_Click), isUserAction: true);
        }
        finally
        {
            ClaudeApiSaveTestButton.IsEnabled = true;
        }
    }

    private void ClaudeApiDisconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null || _settingsService == null || _credentialService == null) return;

        _credentialService.DeleteAiApiKey("Claude"); _apiKeyInvalidator?.Invoke("Claude");
        _isClaudeApiConnected = false;
        _settings.IsClaudeApiConnected = false;
        _settingsService.SaveSettings(_settings);
        ClaudeApiKeyPasswordBox.Clear();
        UpdateClaudeApiStatusLine();
        ClaudeApiDisconnectButton.IsEnabled = false;
        UpdateAiDefaults();
    }

    /// <summary>
    /// Refresh the API-section status line based on stored key + connected
    /// flag. Shows a masked tail like <c>sk-ant-…••••1234</c> when a key
    /// is present so the user has a visual cue that one is saved without
    /// ever revealing the plaintext.
    /// </summary>
    private void UpdateClaudeApiStatusLine()
    {
        if (_credentialService == null || ClaudeApiStatusText == null) return;
        var key = _credentialService.GetAiApiKey("Claude");
        var hasKey = !string.IsNullOrEmpty(key);
        if (hasKey && _isClaudeApiConnected)
        {
            ClaudeApiStatusText.Text = $"Connected — {MaskKey(key!)}";
            ClaudeApiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
            ClaudeApiDisconnectButton.IsEnabled = true;
        }
        else if (hasKey)
        {
            ClaudeApiStatusText.Text = $"Key saved ({MaskKey(key!)}) — click Save & Test to validate.";
            ClaudeApiStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            ClaudeApiDisconnectButton.IsEnabled = true;
        }
        else
        {
            ClaudeApiStatusText.Text = "Not connected";
            ClaudeApiStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            ClaudeApiDisconnectButton.IsEnabled = false;
        }
    }

    private static string MaskKey(string key)
    {
        if (key.Length <= 12) return "••••";
        var prefix = key[..Math.Min(7, key.Length)];
        var suffix = key[^Math.Min(4, key.Length)..];
        return $"{prefix}…••••{suffix}";
    }

    /// <summary>
    /// Seed an editable model ComboBox with the curated list and select
    /// whatever the user previously saved — even if that value isn't in
    /// the curated list. The IsEditable=True ComboBox treats the text as
    /// authoritative when typed; we read .Text rather than .SelectedItem
    /// when persisting so a custom-typed model survives save/load round-
    /// tripping.
    /// </summary>
    private static void PopulateModelComboBox(ComboBox combo, IReadOnlyList<string> curatedModels, string currentValue)
    {
        combo.Items.Clear();
        foreach (var m in curatedModels) combo.Items.Add(m);
        // If the saved value isn't in the curated list, add it so the
        // dropdown shows the user's exact pick first.
        if (!string.IsNullOrEmpty(currentValue) && !curatedModels.Contains(currentValue))
        {
            combo.Items.Insert(0, currentValue);
        }
        combo.Text = currentValue;
    }

    #endregion

    #region Gemini

    private async void GeminiConnect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await CheckCliIntegrationAsync("gemini", GeminiStatusText, GeminiConnectButton, () =>
            {
                _isGeminiConnected = true;
                GeminiDisconnectButton.IsEnabled = true;
            });
        }
        catch (Exception ex)
        {
            AsyncErrorHandler.Handle(ex, nameof(GeminiConnect_Click), isUserAction: true);
        }
    }

    private void GeminiDisconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null || _settingsService == null) return;

        _isGeminiConnected = false;
        GeminiStatusText.Text = "Not connected";
        GeminiStatusText.Foreground = new SolidColorBrush(Colors.Gray);
        GeminiConnectButton.IsEnabled = true;
        GeminiDisconnectButton.IsEnabled = false;
        _settings.IsGeminiConnected = false;
        _settingsService.SaveSettings(_settings);
        UpdateAiDefaults();
    }

    // --- Gemini API transport ---

    private void GeminiTransport_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressGeminiTransportSync || _settings == null || _settingsService == null) return;
        _settings.GeminiTransport = GeminiTransportApiRadio.IsChecked == true ? "Api" : "Cli";
        _settingsService.SaveSettings(_settings);
        ApplyGeminiTransportVisibility();
    }

    private void ApplyGeminiTransportVisibility()
    {
        if (GeminiCliBody == null || GeminiApiBody == null) return;
        var api = GeminiTransportApiRadio.IsChecked == true;
        GeminiCliBody.Visibility = api ? Visibility.Collapsed : Visibility.Visible;
        GeminiApiBody.Visibility = api ? Visibility.Visible : Visibility.Collapsed;
    }

    private void GeminiApiModel_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) PersistGeminiApiModel();
    }

    private void GeminiApiModel_LostFocus(object sender, RoutedEventArgs e) => PersistGeminiApiModel();

    private void PersistGeminiApiModel()
    {
        if (_settings == null || _settingsService == null) return;
        var model = (GeminiApiModelComboBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(model)) return;
        _settings.GeminiApiModel = model;
        _settingsService.SaveSettings(_settings);
    }

    private async void GeminiApiSaveTest_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_settings == null || _settingsService == null || _credentialService == null) return;

            var typed = GeminiApiKeyPasswordBox.Password ?? string.Empty;
            var existing = _credentialService.GetAiApiKey("Gemini");
            var keyToTest = string.IsNullOrEmpty(typed) ? existing : typed;
            if (string.IsNullOrEmpty(keyToTest))
            {
                GeminiApiStatusText.Text = "Enter an API key first.";
                GeminiApiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                return;
            }

            var model = GeminiApiModelComboBox.Text.Trim();
            if (string.IsNullOrEmpty(model))
            {
                GeminiApiStatusText.Text = "Enter a model name first.";
                GeminiApiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                return;
            }

            GeminiApiSaveTestButton.IsEnabled = false;
            GeminiApiStatusText.Text = "Testing…";
            GeminiApiStatusText.Foreground = new SolidColorBrush(Colors.Gray);

            if (!string.IsNullOrEmpty(typed))
            {
                _credentialService.SetAiApiKey("Gemini", typed); _apiKeyInvalidator?.Invoke("Gemini");
                GeminiApiKeyPasswordBox.Clear();
            }
            _settings.GeminiApiModel = model;
            _settingsService.SaveSettings(_settings);

            var timeout = GetAiTimeoutSeconds();
            using var probeHttp = new HttpClient();
            var probe = new GeminiApiClient(
                probeHttp,
                keyReader: () => keyToTest,
                modelProvider: () => model,
                timeoutSecondsProvider: () => timeout);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(2, timeout)));
            var error = await probe.TestConnectionAsync(cts.Token);

            if (error == null)
            {
                _isGeminiApiConnected = true;
                _settings.IsGeminiApiConnected = true;
                _settingsService.SaveSettings(_settings);
                UpdateGeminiApiStatusLine();
                GeminiApiDisconnectButton.IsEnabled = true;
                UpdateAiDefaults();
            }
            else
            {
                _isGeminiApiConnected = false;
                _settings.IsGeminiApiConnected = false;
                _settingsService.SaveSettings(_settings);
                GeminiApiStatusText.Text = TrimDetail(error);
                GeminiApiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                GeminiApiDisconnectButton.IsEnabled = _credentialService.HasAiApiKey("Gemini");
            }
        }
        catch (Exception ex)
        {
            AsyncErrorHandler.Handle(ex, nameof(GeminiApiSaveTest_Click), isUserAction: true);
        }
        finally
        {
            GeminiApiSaveTestButton.IsEnabled = true;
        }
    }

    private void GeminiApiDisconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null || _settingsService == null || _credentialService == null) return;

        _credentialService.DeleteAiApiKey("Gemini"); _apiKeyInvalidator?.Invoke("Gemini");
        _isGeminiApiConnected = false;
        _settings.IsGeminiApiConnected = false;
        _settingsService.SaveSettings(_settings);
        GeminiApiKeyPasswordBox.Clear();
        UpdateGeminiApiStatusLine();
        GeminiApiDisconnectButton.IsEnabled = false;
        UpdateAiDefaults();
    }

    private void UpdateGeminiApiStatusLine()
    {
        if (_credentialService == null || GeminiApiStatusText == null) return;
        var key = _credentialService.GetAiApiKey("Gemini");
        var hasKey = !string.IsNullOrEmpty(key);
        if (hasKey && _isGeminiApiConnected)
        {
            GeminiApiStatusText.Text = $"Connected — {MaskKey(key!)}";
            GeminiApiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
            GeminiApiDisconnectButton.IsEnabled = true;
        }
        else if (hasKey)
        {
            GeminiApiStatusText.Text = $"Key saved ({MaskKey(key!)}) — click Save & Test to validate.";
            GeminiApiStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            GeminiApiDisconnectButton.IsEnabled = true;
        }
        else
        {
            GeminiApiStatusText.Text = "Not connected";
            GeminiApiStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            GeminiApiDisconnectButton.IsEnabled = false;
        }
    }

    #endregion

    #region Codex

    private async void CodexConnect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await CheckCliIntegrationAsync("codex", CodexStatusText, CodexConnectButton, () =>
            {
                _isCodexConnected = true;
                CodexDisconnectButton.IsEnabled = true;
            });
        }
        catch (Exception ex)
        {
            AsyncErrorHandler.Handle(ex, nameof(CodexConnect_Click), isUserAction: true);
        }
    }

    private void CodexDisconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null || _settingsService == null) return;

        _isCodexConnected = false;
        CodexStatusText.Text = "Not connected";
        CodexStatusText.Foreground = new SolidColorBrush(Colors.Gray);
        CodexConnectButton.IsEnabled = true;
        CodexDisconnectButton.IsEnabled = false;
        _settings.IsCodexConnected = false;
        _settingsService.SaveSettings(_settings);
        UpdateAiDefaults();
    }

    #endregion

    #region OpenAI (API key)

    private void OpenAiApiModel_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) PersistOpenAiApiModel();
    }

    private void OpenAiApiModel_LostFocus(object sender, RoutedEventArgs e) => PersistOpenAiApiModel();

    private void PersistOpenAiApiModel()
    {
        if (_settings == null || _settingsService == null) return;
        var model = (OpenAiApiModelComboBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(model)) return;
        _settings.OpenAiApiModel = model;
        _settingsService.SaveSettings(_settings);
    }

    private async void OpenAiApiSaveTest_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_settings == null || _settingsService == null || _credentialService == null) return;

            var typed = OpenAiApiKeyPasswordBox.Password ?? string.Empty;
            var existing = _credentialService.GetAiApiKey("OpenAI");
            var keyToTest = string.IsNullOrEmpty(typed) ? existing : typed;
            if (string.IsNullOrEmpty(keyToTest))
            {
                OpenAiApiStatusText.Text = "Enter an API key first.";
                OpenAiApiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                return;
            }

            var model = OpenAiApiModelComboBox.Text.Trim();
            if (string.IsNullOrEmpty(model))
            {
                OpenAiApiStatusText.Text = "Enter a model name first.";
                OpenAiApiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                return;
            }

            OpenAiApiSaveTestButton.IsEnabled = false;
            OpenAiApiStatusText.Text = "Testing…";
            OpenAiApiStatusText.Foreground = new SolidColorBrush(Colors.Gray);

            if (!string.IsNullOrEmpty(typed))
            {
                _credentialService.SetAiApiKey("OpenAI", typed); _apiKeyInvalidator?.Invoke("OpenAI");
                OpenAiApiKeyPasswordBox.Clear();
            }
            _settings.OpenAiApiModel = model;
            _settingsService.SaveSettings(_settings);

            var timeout = GetAiTimeoutSeconds();
            using var probeHttp = new HttpClient();
            var probe = new OpenAiApiClient(
                probeHttp,
                keyReader: () => keyToTest,
                modelProvider: () => model,
                timeoutSecondsProvider: () => timeout);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(2, timeout)));
            var error = await probe.TestConnectionAsync(cts.Token);

            if (error == null)
            {
                _isOpenAiApiConnected = true;
                _settings.IsOpenAiApiConnected = true;
                _settingsService.SaveSettings(_settings);
                UpdateOpenAiApiStatusLine();
                OpenAiApiDisconnectButton.IsEnabled = true;
                UpdateAiDefaults();
            }
            else
            {
                _isOpenAiApiConnected = false;
                _settings.IsOpenAiApiConnected = false;
                _settingsService.SaveSettings(_settings);
                OpenAiApiStatusText.Text = TrimDetail(error);
                OpenAiApiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                OpenAiApiDisconnectButton.IsEnabled = _credentialService.HasAiApiKey("OpenAI");
            }
        }
        catch (Exception ex)
        {
            AsyncErrorHandler.Handle(ex, nameof(OpenAiApiSaveTest_Click), isUserAction: true);
        }
        finally
        {
            OpenAiApiSaveTestButton.IsEnabled = true;
        }
    }

    private void OpenAiApiDisconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null || _settingsService == null || _credentialService == null) return;

        _credentialService.DeleteAiApiKey("OpenAI"); _apiKeyInvalidator?.Invoke("OpenAI");
        _isOpenAiApiConnected = false;
        _settings.IsOpenAiApiConnected = false;
        _settingsService.SaveSettings(_settings);
        OpenAiApiKeyPasswordBox.Clear();
        UpdateOpenAiApiStatusLine();
        OpenAiApiDisconnectButton.IsEnabled = false;
        UpdateAiDefaults();
    }

    private void UpdateOpenAiApiStatusLine()
    {
        if (_credentialService == null || OpenAiApiStatusText == null) return;
        var key = _credentialService.GetAiApiKey("OpenAI");
        var hasKey = !string.IsNullOrEmpty(key);
        if (hasKey && _isOpenAiApiConnected)
        {
            OpenAiApiStatusText.Text = $"Connected — {MaskKey(key!)}";
            OpenAiApiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
            OpenAiApiDisconnectButton.IsEnabled = true;
        }
        else if (hasKey)
        {
            OpenAiApiStatusText.Text = $"Key saved ({MaskKey(key!)}) — click Save & Test to validate.";
            OpenAiApiStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            OpenAiApiDisconnectButton.IsEnabled = true;
        }
        else
        {
            OpenAiApiStatusText.Text = "Not connected";
            OpenAiApiStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            OpenAiApiDisconnectButton.IsEnabled = false;
        }
    }

    #endregion

    #region OpenAI-Compatible

    private void OpenAiCompatibleBaseUrl_Changed(object sender, TextChangedEventArgs e)
    {
        if (_settings == null || _settingsService == null) return;
        _settings.OpenAiCompatibleBaseUrl = OpenAiCompatibleBaseUrlTextBox.Text.Trim();
        _settingsService.SaveSettings(_settings);
    }

    private void OpenAiCompatibleModel_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) PersistOpenAiCompatibleModel();
    }

    private void OpenAiCompatibleModel_LostFocus(object sender, RoutedEventArgs e) => PersistOpenAiCompatibleModel();

    private void PersistOpenAiCompatibleModel()
    {
        if (_settings == null || _settingsService == null) return;
        _settings.OpenAiCompatibleModel = (OpenAiCompatibleModelComboBox.Text ?? string.Empty).Trim();
        _settingsService.SaveSettings(_settings);
    }

    private async void OpenAiCompatibleSaveTest_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_settings == null || _settingsService == null || _credentialService == null) return;

            var typed = OpenAiCompatibleApiKeyPasswordBox.Password ?? string.Empty;
            var existing = _credentialService.GetAiApiKey("OpenAiCompatible");
            var keyToTest = string.IsNullOrEmpty(typed) ? existing : typed;
            if (string.IsNullOrEmpty(keyToTest))
            {
                OpenAiCompatibleStatusText.Text = "Enter an API key first (use any non-empty string for local servers).";
                OpenAiCompatibleStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                return;
            }

            var baseUrl = OpenAiCompatibleBaseUrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(baseUrl))
            {
                OpenAiCompatibleStatusText.Text = "Enter a base URL first.";
                OpenAiCompatibleStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                return;
            }

            var model = OpenAiCompatibleModelComboBox.Text.Trim();
            if (string.IsNullOrEmpty(model))
            {
                OpenAiCompatibleStatusText.Text = "Enter a model name first.";
                OpenAiCompatibleStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                return;
            }

            OpenAiCompatibleSaveTestButton.IsEnabled = false;
            OpenAiCompatibleStatusText.Text = "Testing…";
            OpenAiCompatibleStatusText.Foreground = new SolidColorBrush(Colors.Gray);

            if (!string.IsNullOrEmpty(typed))
            {
                _credentialService.SetAiApiKey("OpenAiCompatible", typed); _apiKeyInvalidator?.Invoke("OpenAiCompatible");
                OpenAiCompatibleApiKeyPasswordBox.Clear();
            }
            _settings.OpenAiCompatibleBaseUrl = baseUrl;
            _settings.OpenAiCompatibleModel = model;
            _settingsService.SaveSettings(_settings);

            var timeout = GetAiTimeoutSeconds();
            using var probeHttp = new HttpClient();
            var probe = new OpenAiChatCompletionsClient(
                probeHttp,
                keyReader: () => keyToTest,
                baseUrlProvider: () => baseUrl,
                modelProvider: () => model,
                timeoutSecondsProvider: () => timeout);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(2, timeout)));
            var error = await probe.TestConnectionAsync(cts.Token);

            if (error == null)
            {
                _isOpenAiCompatibleConnected = true;
                _settings.IsOpenAiCompatibleConnected = true;
                _settingsService.SaveSettings(_settings);
                UpdateOpenAiCompatibleStatusLine();
                OpenAiCompatibleDisconnectButton.IsEnabled = true;
                UpdateAiDefaults();
            }
            else
            {
                _isOpenAiCompatibleConnected = false;
                _settings.IsOpenAiCompatibleConnected = false;
                _settingsService.SaveSettings(_settings);
                OpenAiCompatibleStatusText.Text = TrimDetail(error);
                OpenAiCompatibleStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                OpenAiCompatibleDisconnectButton.IsEnabled = _credentialService.HasAiApiKey("OpenAiCompatible");
            }
        }
        catch (Exception ex)
        {
            AsyncErrorHandler.Handle(ex, nameof(OpenAiCompatibleSaveTest_Click), isUserAction: true);
        }
        finally
        {
            OpenAiCompatibleSaveTestButton.IsEnabled = true;
        }
    }

    private void OpenAiCompatibleDisconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null || _settingsService == null || _credentialService == null) return;

        _credentialService.DeleteAiApiKey("OpenAiCompatible"); _apiKeyInvalidator?.Invoke("OpenAiCompatible");
        _isOpenAiCompatibleConnected = false;
        _settings.IsOpenAiCompatibleConnected = false;
        _settingsService.SaveSettings(_settings);
        OpenAiCompatibleApiKeyPasswordBox.Clear();
        UpdateOpenAiCompatibleStatusLine();
        OpenAiCompatibleDisconnectButton.IsEnabled = false;
        UpdateAiDefaults();
    }

    private void UpdateOpenAiCompatibleStatusLine()
    {
        if (_credentialService == null || OpenAiCompatibleStatusText == null) return;
        var key = _credentialService.GetAiApiKey("OpenAiCompatible");
        var hasKey = !string.IsNullOrEmpty(key);
        if (hasKey && _isOpenAiCompatibleConnected)
        {
            OpenAiCompatibleStatusText.Text = $"Connected — {MaskKey(key!)}";
            OpenAiCompatibleStatusText.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
            OpenAiCompatibleDisconnectButton.IsEnabled = true;
        }
        else if (hasKey)
        {
            OpenAiCompatibleStatusText.Text = $"Key saved ({MaskKey(key!)}) — click Save & Test to validate.";
            OpenAiCompatibleStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            OpenAiCompatibleDisconnectButton.IsEnabled = true;
        }
        else
        {
            OpenAiCompatibleStatusText.Text = "Not connected";
            OpenAiCompatibleStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            OpenAiCompatibleDisconnectButton.IsEnabled = false;
        }
    }

    #endregion

    #region Ollama

    private async void OllamaRefreshModels_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_settings == null) return;

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
        catch (Exception ex)
        {
            OllamaRefreshModelsButton.IsEnabled = true;
            AsyncErrorHandler.Handle(ex, nameof(OllamaRefreshModels_Click), isUserAction: true);
        }
    }

    private async void OllamaConnect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_settings == null || _settingsService == null) return;

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
        catch (Exception ex)
        {
            OllamaConnectButton.IsEnabled = true;
            AsyncErrorHandler.Handle(ex, nameof(OllamaConnect_Click), isUserAction: true);
        }
    }

    private void OllamaDisconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null || _settingsService == null) return;

        _isOllamaConnected = false;
        _settings.OllamaSelectedModel = string.Empty;
        _settingsService.SaveSettings(_settings);

        OllamaStatusText.Text = "Not connected";
        OllamaStatusText.Foreground = new SolidColorBrush(Colors.Gray);
        OllamaConnectButton.IsEnabled = true;
        OllamaDisconnectButton.IsEnabled = false;
        UpdateAiDefaults();
    }

    #endregion

    #region AI Defaults

    private void AiDefaultComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAiSelectionSync || AiDefaultComboBox.SelectedItem is not string selected)
            return;

        if (_settings != null && _settingsService != null)
        {
            _settings.DefaultAiProvider = selected;
            _settingsService.SaveSettings(_settings);
        }
    }

    private void UpdateAiDefaults()
    {
        if (AiDefaultComboBox == null || _settings == null)
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

    #endregion

    #region CLI Integration

    private async Task CheckCliIntegrationAsync(string command, TextBlock statusText, Button actionButton, Action? onConnected)
    {
        if (statusText == null || actionButton == null || _settings == null || _settingsService == null)
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

    private int GetAiTimeoutSeconds()
    {
        if (_settings == null)
            return 30;

        if (AiTimeoutTextBox == null)
            return _settings.AiCliTimeoutSeconds;

        if (int.TryParse(AiTimeoutTextBox.Text, out var value) && value > 0)
        {
            _settings.AiCliTimeoutSeconds = value;
            _settingsService?.SaveSettings(_settings);
            return value;
        }

        return _settings.AiCliTimeoutSeconds;
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
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception killEx) when (killEx is InvalidOperationException
                                                or System.ComponentModel.Win32Exception
                                                or NotSupportedException)
                    {
                        // Process may have exited between timeout and Kill call.
                        Log.Info("AiSettings", $"Kill after timeout failed: {killEx.GetType().Name}: {killEx.Message}");
                    }
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
            catch (Exception ex) when (ex is InvalidOperationException
                                    or System.IO.IOException
                                    or UnauthorizedAccessException
                                    or System.Security.SecurityException)
            {
                Log.Info("AiSettings", $"CLI probe failed: {ex.GetType().Name}: {ex.Message}");
                return (CliCheckResult.Unknown, $"exception: {ex.GetType().Name}");
            }
        }

        return (CliCheckResult.NotConnected, "no output");
    }

    private static string TrimDetail(string detail)
    {
        var compact = detail.Replace("\r", " ").Replace("\n", " ");
        return compact.Length <= 120 ? compact : compact[..120] + "...";
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

    private static void ApplyConnectionState(TextBlock status, Button connect, Button disconnect, bool isConnected)
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

    #endregion
}
