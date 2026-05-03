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
    private IGitCommandRunner? _commandRunner;
    private ISigningToolDetector? _detector;
    private string? _activeRepoPath;
    private GitConfigScope _scope = GitConfigScope.Global;
    private SigningToolAvailability? _availability;
    private bool _suppressEvents;

    // ComboBoxItem IsSelected="True" markers in the XAML cause WPF to
    // fire SelectionChanged for ScopeCombo / MethodCombo while later
    // child controls (MethodCombo itself, GpgKeyRow, SshKeyRow) are
    // still being parsed. Any handler that touches them in that window
    // hits a NullReferenceException. We block every handler until the
    // control has fully Loaded — at which point every named child is
    // accessible.
    private bool _isLoaded;

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
        _commandRunner ??= Leaf.App.Services?.GetService<IGitCommandRunner>();
        _detector ??= Leaf.App.Services?.GetService<ISigningToolDetector>();
        // Flip the guard regardless of service availability so the
        // selection-changed handlers stop swallowing events even when
        // the DI container isn't built (designer / standalone XAML
        // preview). Handlers also check _gitService before issuing
        // git operations, so a null service is harmless.
        _isLoaded = true;
        if (_gitService is null || _detector is null) return;

        InitializeAsync().FireAndForget(nameof(InitializeAsync), isUserAction: true);
    }

    private async Task InitializeAsync()
    {
        _availability = await _detector!.DetectAsync();
        UpdateToolingDisplay(_availability);

        // §5.8 — auto-detect the most relevant scope on open. If any of
        // the four signing keys is set in this repo's local config,
        // default Scope to "This repo" so the panel's initial state
        // reflects what's actually in effect for the user. Without this,
        // a per-repo setting is invisible until the user manually flips
        // Scope, which the original report called out as confusing.
        if (!string.IsNullOrEmpty(_activeRepoPath)
            && await HasAnyLocalSigningConfigAsync().ConfigureAwait(true))
        {
            _suppressEvents = true;
            try
            {
                ScopeCombo.SelectedIndex = 1; // "This repo"
                _scope = GitConfigScope.Local;
            }
            finally
            {
                _suppressEvents = false;
            }
        }

        // Populate the GPG key list once. Re-pre-selection on scope
        // switch is handled inside ReloadConfigAsync via the cached
        // ItemsSource.
        if (_availability.GpgAvailable)
        {
            var keys = await _detector.ListGpgSecretKeysAsync();
            GpgKeyCombo.ItemsSource = keys;
        }

        await ReloadConfigAsync();
    }

    /// <summary>
    /// Run <c>git config --local --get</c> or <c>--global --get</c>
    /// directly via the command runner. The shared
    /// <see cref="IGitService.GetConfigAsync"/> for <see cref="GitConfigScope.Local"/>
    /// reads with normal fall-through (local → global → system) which
    /// hides per-scope visibility — fine for "what's in effect" but the
    /// settings panel needs "what's actually set in THIS file" to make
    /// auto-detect and pre-selection accurate.
    /// </summary>
    private async Task<string?> ReadStrictScopedConfigAsync(string key, GitConfigScope scope)
    {
        if (_commandRunner is null) return null;

        var path = scope == GitConfigScope.Local
            ? _activeRepoPath
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(path)) return null;

        var args = scope == GitConfigScope.Local
            ? new[] { "config", "--local", "--get", key }
            : new[] { "config", "--global", "--get", key };
        try
        {
            var result = await _commandRunner.RunAsync(path, args);
            // git config --get returns exit 1 when the key isn't set in
            // the requested scope. Don't treat that as an error — it's
            // exactly what we need to know.
            return result.Success ? result.StandardOutput.Trim() : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Log.Warn("Signing", $"Strict scope read failed for '{key}' ({scope}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Probe whether any of the four signing-related keys
    /// (<c>gpg.format</c>, <c>user.signingkey</c>, <c>commit.gpgsign</c>,
    /// <c>tag.gpgsign</c>) is set at the local level on the active repo.
    /// Drives the auto-detect of the initial Scope on panel open.
    /// </summary>
    private async Task<bool> HasAnyLocalSigningConfigAsync()
    {
        foreach (var key in new[] { KeyFormat, KeySigningKey, KeyCommitSign, KeyTagSign })
        {
            var value = await ReadStrictScopedConfigAsync(key, GitConfigScope.Local);
            if (!string.IsNullOrWhiteSpace(value)) return true;
        }
        return false;
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
    /// repo. Uses strict <c>--local</c> / <c>--global</c> reads so the
    /// panel always reflects what's actually set in the chosen scope's
    /// file — not the fall-through value. Suppresses event handlers
    /// while writing the controls so the load doesn't race the user's
    /// edits back into config.
    /// </summary>
    private async Task ReloadConfigAsync()
    {
        var format = await ReadStrictScopedConfigAsync(KeyFormat, _scope);
        var commitSign = await ReadStrictScopedConfigAsync(KeyCommitSign, _scope);
        var tagSign = await ReadStrictScopedConfigAsync(KeyTagSign, _scope);
        var key = await ReadStrictScopedConfigAsync(KeySigningKey, _scope);

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

            // Re-pre-select the GPG key for whatever scope we're now
            // showing. Done inside the suppress block so the
            // SelectionChanged handler doesn't echo the read back as a
            // spurious config write.
            if (string.Equals(normalisedFormat, "openpgp", StringComparison.OrdinalIgnoreCase)
                && GpgKeyCombo.ItemsSource is IEnumerable<GpgSecretKey> existingKeys
                && !string.IsNullOrWhiteSpace(key))
            {
                var match = existingKeys.FirstOrDefault(k =>
                    string.Equals(k.LongKeyId, key, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(k.Fingerprint, key, StringComparison.OrdinalIgnoreCase));
                GpgKeyCombo.SelectedItem = match;
            }
            else
            {
                GpgKeyCombo.SelectedItem = null;
            }
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
        if (!_isLoaded || _suppressEvents) return;
        _scope = (ScopeCombo.SelectedItem as ComboBoxItem)?.Tag as string == "Local"
            ? GitConfigScope.Local
            : GitConfigScope.Global;
        ReloadConfigAsync().FireAndForget(nameof(ReloadConfigAsync), isUserAction: true);
    }

    private void MethodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;
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
        if (!_isLoaded || _suppressEvents) return;
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
        if (!_isLoaded || _suppressEvents) return;
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
        if (!_isLoaded || _suppressEvents) return;
        var value = SignCommitsCheckBox.IsChecked == true ? "true" : null;
        WriteConfigSafeAsync(KeyCommitSign, value).FireAndForget(nameof(SignCommitsCheckBox_Click), isUserAction: true);
    }

    private void SignTagsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded || _suppressEvents) return;
        var value = SignTagsCheckBox.IsChecked == true ? "true" : null;
        WriteConfigSafeAsync(KeyTagSign, value).FireAndForget(nameof(SignTagsCheckBox_Click), isUserAction: true);
    }
}
