using System.IO;
using System.Windows;
using System.Windows.Controls;
using Leaf.Models;
using Leaf.Services;
using Leaf.Services.Signing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace Leaf.Views.Settings;

/// <summary>
/// §5.8 Commit Signing settings panel — reads/writes git config keys
/// directly via <see cref="IGitService"/>'s GetConfigAsync /
/// SetConfigAsync. Settings live in <c>~/.gitconfig</c> (Global) or
/// the repo's <c>.git/config</c> (Local) so they're preserved across
/// every Git client, not Leaf-specific.
///
/// <para>The panel is disabled until <see cref="ISigningToolDetector"/>
/// has finished probing — we don't want to invite users to pick GPG
/// when GPG isn't installed.</para>
/// </summary>
public partial class CommitSigningSettingsControl : UserControl, ISettingsSectionControl
{
    private const string KeyFormat = "gpg.format";
    private const string KeySigningKey = "user.signingkey";
    private const string KeyCommitSign = "commit.gpgsign";
    private const string KeyTagSign = "tag.gpgsign";

    private IGitService? _gitService;
    private ISigningToolDetector? _detector;
    private string? _activeRepoPath;
    private GitConfigScope _scope = GitConfigScope.Global;
    private SigningToolAvailability? _availability;
    private bool _suppressEvents;

    public CommitSigningSettingsControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Path to the active repository. Set by the parent dialog before
    /// the panel is shown — needed for <see cref="GitConfigScope.Local"/>
    /// reads/writes. Null when no repo is open; the Local scope option
    /// is disabled in that state.
    /// </summary>
    public string? ActiveRepositoryPath
    {
        get => _activeRepoPath;
        set
        {
            _activeRepoPath = value;
            // The Local scope ComboBoxItem is the second one — disable it
            // so the user can't pick a scope that doesn't have a target.
            if (ScopeCombo?.Items.Count > 1 && ScopeCombo.Items[1] is ComboBoxItem localItem)
                localItem.IsEnabled = !string.IsNullOrEmpty(value);
        }
    }

    public void LoadSettings(AppSettings settings, CredentialService credentialService)
    {
        // Nothing to load from AppSettings — every value lives in git config.
        // Initial population happens in OnLoaded once services resolve.
    }

    public void SaveSettings(AppSettings settings, CredentialService credentialService)
    {
        // Each toggle / picker writes through git config immediately, so
        // SaveSettings has nothing to flush. The user's parent-dialog
        // Cancel won't undo signing changes — by design, since git
        // config writes are per-action and reversible via the same UI.
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _gitService ??= Leaf.App.Services?.GetService<IGitService>();
        _detector ??= Leaf.App.Services?.GetService<ISigningToolDetector>();
        if (_gitService is null || _detector is null) return;

        InitializeAsync().FireAndForget(nameof(InitializeAsync), isUserAction: true);
    }

    private async Task InitializeAsync()
    {
        _availability = await _detector!.DetectAsync();
        UpdateToolingDisplay(_availability);
        await ReloadConfigAsync();

        if (_availability.GpgAvailable)
        {
            var keys = await _detector.ListGpgSecretKeysAsync();
            GpgKeyCombo.ItemsSource = keys;
            // Pre-select the key matching the configured user.signingkey.
            var configuredKey = await ReadConfigSafeAsync(KeySigningKey);
            if (!string.IsNullOrWhiteSpace(configuredKey))
            {
                var match = keys.FirstOrDefault(k =>
                    string.Equals(k.LongKeyId, configuredKey, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(k.Fingerprint, configuredKey, StringComparison.OrdinalIgnoreCase));
                if (match is not null) GpgKeyCombo.SelectedItem = match;
            }
        }
    }

    private void UpdateToolingDisplay(SigningToolAvailability availability)
    {
        GpgStatusText.Text = availability.GpgAvailable ? "Detected" : "Missing";
        GpgVersionText.Text = availability.GpgVersion ?? "Not on PATH";
        SshStatusText.Text = availability.SshAvailable ? "Detected" : "Missing";
        SshVersionText.Text = availability.SshVersion ?? "Not on PATH";
        MissingHelpText.Visibility = (!availability.GpgAvailable && !availability.SshAvailable)
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Disable method options for tools that aren't available — saves
        // the user from picking GPG when there's no GPG binary on PATH.
        if (MethodCombo.Items.Count >= 3)
        {
            if (MethodCombo.Items[1] is ComboBoxItem gpgItem) gpgItem.IsEnabled = availability.GpgAvailable;
            if (MethodCombo.Items[2] is ComboBoxItem sshItem) sshItem.IsEnabled = availability.SshAvailable;
        }
    }

    /// <summary>
    /// Re-read the relevant git config keys for the current scope and
    /// repo. Suppresses event handlers while writing the controls so
    /// the load doesn't race the user's edits back into config.
    /// </summary>
    private async Task ReloadConfigAsync()
    {
        var format = await ReadConfigSafeAsync(KeyFormat);
        var commitSign = await ReadConfigSafeAsync(KeyCommitSign);
        var tagSign = await ReadConfigSafeAsync(KeyTagSign);
        var key = await ReadConfigSafeAsync(KeySigningKey);

        _suppressEvents = true;
        try
        {
            // gpg.format defaults to "openpgp" when unset; only "ssh" or
            // explicit "openpgp" map to a non-default UI state.
            var normalisedFormat = string.IsNullOrEmpty(format)
                ? (string.IsNullOrEmpty(commitSign) && string.IsNullOrEmpty(tagSign) && string.IsNullOrEmpty(key)
                    ? "None"
                    : "openpgp")
                : format;
            for (var i = 0; i < MethodCombo.Items.Count; i++)
            {
                if (MethodCombo.Items[i] is ComboBoxItem item
                    && string.Equals(item.Tag as string, normalisedFormat, StringComparison.OrdinalIgnoreCase))
                {
                    MethodCombo.SelectedIndex = i;
                    break;
                }
            }
            UpdateMethodVisibility();

            SshKeyPathTextBox.Text = string.Equals(normalisedFormat, "ssh", StringComparison.OrdinalIgnoreCase)
                ? key ?? string.Empty
                : string.Empty;

            SignCommitsCheckBox.IsChecked = ParseBool(commitSign);
            SignTagsCheckBox.IsChecked = ParseBool(tagSign);
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private static bool ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Trim() switch
        {
            "true" or "1" or "yes" or "on" => true,
            _ => false,
        };
    }

    private async Task<string?> ReadConfigSafeAsync(string key)
    {
        if (_gitService is null) return null;
        var path = _scope == GitConfigScope.Local
            ? _activeRepoPath
            : (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            return await _gitService.GetConfigAsync(path, key, _scope);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Log.Warn("Signing", $"Could not read git config '{key}' ({_scope}): {ex.Message}");
            return null;
        }
    }

    private async Task WriteConfigSafeAsync(string key, string? value)
    {
        if (_gitService is null) return;
        var path = _scope == GitConfigScope.Local
            ? _activeRepoPath
            : (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (string.IsNullOrEmpty(value))
                await _gitService.UnsetConfigAsync(path, key, _scope);
            else
                await _gitService.SetConfigAsync(path, key, value, _scope);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Log.Error("Signing", $"Could not write git config '{key}' ({_scope})", ex);
            MessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                $"Could not save the setting: {ex.Message}",
                "Commit signing",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ScopeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        _scope = (ScopeCombo.SelectedItem as ComboBoxItem)?.Tag as string == "Local"
            ? GitConfigScope.Local
            : GitConfigScope.Global;
        ReloadConfigAsync().FireAndForget(nameof(ReloadConfigAsync), isUserAction: true);
    }

    private void MethodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateMethodVisibility();
        if (_suppressEvents) return;

        var tag = (MethodCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        // None: clear the format key + clear the signing key + clear both
        // sign-toggles. Other clients reading commit.gpgsign=true with no
        // user.signingkey would prompt for a default key, which surprises
        // users who think they've turned signing off.
        ApplyMethodChangeAsync(tag).FireAndForget(nameof(MethodCombo_SelectionChanged), isUserAction: true);
    }

    private async Task ApplyMethodChangeAsync(string? tag)
    {
        switch (tag)
        {
            case "None":
                await WriteConfigSafeAsync(KeyFormat, null);
                await WriteConfigSafeAsync(KeySigningKey, null);
                await WriteConfigSafeAsync(KeyCommitSign, null);
                await WriteConfigSafeAsync(KeyTagSign, null);
                break;
            case "openpgp":
                await WriteConfigSafeAsync(KeyFormat, "openpgp");
                break;
            case "ssh":
                await WriteConfigSafeAsync(KeyFormat, "ssh");
                break;
        }
    }

    private void UpdateMethodVisibility()
    {
        var tag = (MethodCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        GpgKeyRow.Visibility = tag == "openpgp" ? Visibility.Visible : Visibility.Collapsed;
        SshKeyRow.Visibility = tag == "ssh" ? Visibility.Visible : Visibility.Collapsed;
        var enableToggles = tag is "openpgp" or "ssh";
        SignCommitsCheckBox.IsEnabled = enableToggles;
        SignTagsCheckBox.IsEnabled = enableToggles;
    }

    private void GpgKeyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        var key = (GpgKeyCombo.SelectedItem as GpgSecretKey)?.LongKeyId;
        WriteConfigSafeAsync(KeySigningKey, key).FireAndForget(nameof(GpgKeyCombo_SelectionChanged), isUserAction: true);
    }

    private void SshKeyPathTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitSshKeyPath();
    }

    private void SshKeyPathTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Enter commits — same UX shape as the rebind row in
        // KeyboardShortcutsSettingsControl. Without this, users who type
        // a path and hit Enter expecting save would have to tab-out first.
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            CommitSshKeyPath();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Persist the SSH key path. Triggered by LostFocus / Enter rather
    /// than TextChanged so a user typing the path doesn't spawn one git
    /// config process per keystroke. Empty path clears the override so
    /// signing falls through to the global default; per Engineering
    /// Software Policy the failure is visible (signing fails on commit)
    /// rather than silently substituted.
    /// </summary>
    private void CommitSshKeyPath()
    {
        if (_suppressEvents) return;
        var path = SshKeyPathTextBox.Text?.Trim() ?? string.Empty;
        WriteConfigSafeAsync(KeySigningKey, string.IsNullOrWhiteSpace(path) ? null : path)
            .FireAndForget(nameof(CommitSshKeyPath), isUserAction: true);
    }

    private void BrowseSshKey_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select SSH key",
            Filter = "Public key (*.pub)|*.pub|All files (*.*)|*.*",
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh"),
        };
        if (dialog.ShowDialog() == true)
            SshKeyPathTextBox.Text = dialog.FileName;
    }

    private void SignCommitsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        var value = SignCommitsCheckBox.IsChecked == true ? "true" : null;
        WriteConfigSafeAsync(KeyCommitSign, value).FireAndForget(nameof(SignCommitsCheckBox_Click), isUserAction: true);
    }

    private void SignTagsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        var value = SignTagsCheckBox.IsChecked == true ? "true" : null;
        WriteConfigSafeAsync(KeyTagSign, value).FireAndForget(nameof(SignTagsCheckBox_Click), isUserAction: true);
    }
}
