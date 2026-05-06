using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Leaf.Models;
using Leaf.Services;
using Leaf.Services.Ssh;
using Leaf.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace Leaf.Views.Settings;

/// <summary>
/// §5.13 SSH Key Management settings panel. Hosts every phase of the
/// feature (key list, generate, ssh config editor, ssh-agent) in one
/// scrollable surface — same Fluent card-stack as the Commit Signing
/// panel so the two read as siblings under "Authentication".
/// </summary>
public partial class SshKeysSettingsControl : UserControl, ISettingsSectionControl
{
    private ISshKeyService? _sshService;
    private IClipboardService? _clipboardService;
    private IFileSystemService? _fileSystemService;

    private readonly ObservableCollection<SshKeyRow> _keys = [];
    private readonly ObservableCollection<HostRow> _hosts = [];
    private readonly ObservableCollection<AgentKeyRow> _agentKeys = [];

    private HostRow? _editingHost;

    public SshKeysSettingsControl()
    {
        InitializeComponent();
        KeysList.ItemsSource = _keys;
        HostsList.ItemsSource = _hosts;
        AgentKeysList.ItemsSource = _agentKeys;
        Loaded += OnLoaded;
    }

    public void LoadSettings(AppSettings settings, CredentialService credentialService)
    {
        // SSH state lives entirely on disk in ~/.ssh — there's nothing
        // to hydrate from AppSettings. Initial load happens in OnLoaded
        // once services resolve.
    }

    public void SaveSettings(AppSettings settings, CredentialService credentialService)
    {
        // Every action in the panel writes through immediately; the
        // dialog's Close-time SaveSettings has nothing to flush here.
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _sshService ??= Leaf.App.Services?.GetService<ISshKeyService>();
        _clipboardService ??= Leaf.App.Services?.GetService<IClipboardService>();
        _fileSystemService ??= Leaf.App.Services?.GetService<IFileSystemService>();
        if (_sshService is null) return;

        RefreshAllAsync().FireAndForget(nameof(RefreshAllAsync), isUserAction: true);
    }

    private async Task RefreshAllAsync()
    {
        await RefreshToolingAsync().ConfigureAwait(true);
        await RefreshKeysAsync().ConfigureAwait(true);
        await RefreshConfigAsync().ConfigureAwait(true);
        await RefreshAgentAsync().ConfigureAwait(true);
    }

    private async Task RefreshToolingAsync()
    {
        if (_sshService is null) return;
        var availability = await _sshService.DetectToolingAsync().ConfigureAwait(true);
        SshStatusText.Text = availability.HasSsh ? "Detected" : "Missing";
        SshKeygenStatusText.Text = availability.HasSshKeygen ? "Detected" : "Missing";
        SshAddStatusText.Text = availability.HasSshAdd ? "Detected" : "Missing";
        SshAgentStatusText.Text = availability.AgentStatus switch
        {
            SshAgentStatus.Running => "Running",
            SshAgentStatus.NotRunning => "Not running",
            SshAgentStatus.Unavailable => "ssh-add not on PATH",
            _ => "Unknown",
        };

        var missingTooling = !availability.HasSsh || !availability.HasSshKeygen;
        if (missingTooling)
        {
            ToolingHelpText.Visibility = Visibility.Visible;
            ToolingHelpText.Text =
                "OpenSSH wasn't fully detected. On Windows 10/11, install via "
                + "Settings → Apps → Optional features → \"OpenSSH Client\". "
                + "Re-open Settings after install.";
        }
        else if (availability.AgentStatus == SshAgentStatus.NotRunning)
        {
            ToolingHelpText.Visibility = Visibility.Visible;
            ToolingHelpText.Text =
                "ssh-agent isn't running. Start it via PowerShell: "
                + "`Get-Service ssh-agent | Set-Service -StartupType Automatic; Start-Service ssh-agent`.";
        }
        else
        {
            ToolingHelpText.Visibility = Visibility.Collapsed;
        }

        // Disable agent buttons that require a working ssh-add. The
        // generate button only needs ssh-keygen.
        ReloadAgentButton.IsEnabled = availability.HasSshAdd;
        GenerateKeyButton.IsEnabled = availability.HasSshKeygen;
        RunTestButton.IsEnabled = availability.HasSsh;
    }

    private async Task RefreshKeysAsync()
    {
        if (_sshService is null) return;
        var keys = await _sshService.ListPublicKeysAsync().ConfigureAwait(true);
        _keys.Clear();
        foreach (var k in keys) _keys.Add(SshKeyRow.From(k));
        EmptyKeysHint.Visibility = _keys.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task RefreshConfigAsync()
    {
        if (_sshService is null) return;
        var entries = await _sshService.ReadSshConfigAsync().ConfigureAwait(true);
        _hosts.Clear();
        foreach (var e in entries) _hosts.Add(HostRow.From(e));
        if (_hosts.Count > 0)
            HostsList.SelectedIndex = 0;
        else
            ApplyHostToEditor(null);
    }

    private async Task RefreshAgentAsync()
    {
        if (_sshService is null) return;
        var keys = await _sshService.ListAgentKeysAsync().ConfigureAwait(true);
        _agentKeys.Clear();
        foreach (var k in keys) _agentKeys.Add(AgentKeyRow.From(k));
        if (_agentKeys.Count == 0)
        {
            AgentEmptyText.Text = "ssh-agent has no keys loaded. Use \"Add to agent\" on a key above to load one.";
            AgentEmptyText.Visibility = Visibility.Visible;
        }
        else
        {
            AgentEmptyText.Visibility = Visibility.Collapsed;
        }
    }

    // --- Phase 1: keys list actions ----------------------------------------

    private void OpenSshFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        // EnsureSshDirectory applies the owner-only ACL on first
        // creation — a plain Directory.CreateDirectory inherits the
        // user-profile defaults, which include Users group entries
        // that OpenSSH StrictModes rejects.
        _sshService?.EnsureSshDirectory();
        _fileSystemService?.OpenInExplorer(dir);
    }

    private async void GenerateKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sshService is null) return;
        var dialog = new GenerateSshKeyDialog
        {
            Owner = Window.GetWindow(this),
        };
        if (dialog.ShowDialog() != true) return;

        var request = dialog.BuildRequest();
        if (request is null) return;

        var result = await _sshService.GenerateKeyAsync(request).ConfigureAwait(true);
        if (!result.Success)
        {
            FluentMessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                result.Message ?? "Key generation failed.",
                "Generate SSH key",
                MessageBoxButton.OK,
                FluentMessageBoxIcon.Warning);
            return;
        }

        await RefreshKeysAsync().ConfigureAwait(true);
        if (result.GeneratedKey is { } key)
        {
            try
            {
                var pubText = await _sshService.ReadPublicKeyTextAsync(key.PublicKeyPath).ConfigureAwait(true);
                _clipboardService?.SetText(pubText);
                FluentMessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                    "Public key generated and copied to clipboard. Paste it into your Git host's SSH keys page.",
                    "Generate SSH key",
                    MessageBoxButton.OK,
                    FluentMessageBoxIcon.Information);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn("Ssh", $"Generated key but failed to read text back: {ex.Message}");
            }
        }
    }

    private async void CopyPublicKey_Click(object sender, RoutedEventArgs e)
    {
        if (_sshService is null || _clipboardService is null) return;
        if ((sender as Button)?.Tag is not SshKeyRow row) return;
        try
        {
            var text = await _sshService.ReadPublicKeyTextAsync(row.PublicKeyPath).ConfigureAwait(true);
            _clipboardService.SetText(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FluentMessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                $"Could not read public key file: {ex.Message}",
                "Copy public key",
                MessageBoxButton.OK,
                FluentMessageBoxIcon.Warning);
        }
    }

    private void RevealKey_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not SshKeyRow row) return;
        _fileSystemService?.OpenInExplorerAndSelect(row.PublicKeyPath);
    }

    private async void RunTestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sshService is null) return;
        var target = TestHostCombo.Text?.Trim();
        if (string.IsNullOrEmpty(target))
        {
            FluentMessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                "Type or pick a host to test.",
                "Test connection",
                MessageBoxButton.OK,
                FluentMessageBoxIcon.Information);
            return;
        }

        RunTestButton.IsEnabled = false;
        try
        {
            var result = await _sshService.TestConnectionAsync(target).ConfigureAwait(true);
            ShowTestResult(result);
        }
        finally
        {
            RunTestButton.IsEnabled = true;
        }
    }

    private void ShowTestResult(SshConnectionTestResult result)
    {
        TestResultBorder.Visibility = Visibility.Visible;
        if (result.Authenticated)
        {
            TestResultHeader.Text = string.IsNullOrEmpty(result.Identity)
                ? "Authenticated successfully."
                : $"Authenticated as {result.Identity}.";
            TestResultHeader.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43));
            TestResultBorder.BorderBrush = TestResultHeader.Foreground;
        }
        else
        {
            TestResultHeader.Text = "Authentication failed.";
            TestResultHeader.Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0x35, 0x35));
            TestResultBorder.BorderBrush = TestResultHeader.Foreground;
        }
        TestResultBody.Text = string.IsNullOrWhiteSpace(result.Output)
            ? "(no output)"
            : result.Output;
    }

    // --- Phase 3: SSH config editor ----------------------------------------

    private void ReloadConfigButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshConfigAsync().FireAndForget(nameof(RefreshConfigAsync), isUserAction: true);
    }

    private void AddHostButton_Click(object sender, RoutedEventArgs e)
    {
        // In-memory only — the file is rewritten when the user clicks
        // Save. Persisting here would put a half-built `Host new-host`
        // stanza on disk that survives a Cancel-style dialog close.
        var row = new HostRow { HostPattern = "new-host" };
        _hosts.Add(row);
        HostsList.SelectedItem = row;
        HostPatternBox.Focus();
        HostPatternBox.SelectAll();
    }

    private void HostsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = HostsList.SelectedItem as HostRow;
        ApplyHostToEditor(row);
    }

    private void ApplyHostToEditor(HostRow? row)
    {
        _editingHost = row;
        HostEditor.IsEnabled = row is not null;
        if (row is null)
        {
            HostPatternBox.Clear();
            HostNameBox.Clear();
            HostUserBox.Clear();
            HostPortBox.Clear();
            HostIdentityFileBox.Clear();
            HostProxyCommandBox.Clear();
            ExtraOptionsText.Text = string.Empty;
            return;
        }
        HostPatternBox.Text = row.HostPattern;
        HostNameBox.Text = row.HostName ?? string.Empty;
        HostUserBox.Text = row.User ?? string.Empty;
        HostPortBox.Text = row.Port?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        HostIdentityFileBox.Text = row.IdentityFile ?? string.Empty;
        HostProxyCommandBox.Text = row.ProxyCommand ?? string.Empty;
        ExtraOptionsText.Text = row.ExtraOptions.Count == 0
            ? string.Empty
            : "Other options preserved verbatim:\n"
              + string.Join("\n", row.ExtraOptions.Select(o => $"    {o.Key} {o.Value}"));
    }

    private void BrowseIdentityFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select SSH private key",
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh"),
            Filter = "All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() == true)
        {
            HostIdentityFileBox.Text = dialog.FileName;
        }
    }

    private void SaveHostButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editingHost is null) return;
        var pattern = HostPatternBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(pattern))
        {
            FluentMessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                "Host pattern cannot be empty.",
                "Save SSH config",
                MessageBoxButton.OK,
                FluentMessageBoxIcon.Warning);
            return;
        }

        int? port = null;
        if (!string.IsNullOrWhiteSpace(HostPortBox.Text))
        {
            if (!int.TryParse(HostPortBox.Text, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed <= 0 || parsed > 65535)
            {
                FluentMessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                    "Port must be a number between 1 and 65535, or empty for the default (22).",
                    "Save SSH config",
                    MessageBoxButton.OK,
                    FluentMessageBoxIcon.Warning);
                return;
            }
            port = parsed;
        }

        _editingHost.HostPattern = pattern;
        _editingHost.HostName = NullIfBlank(HostNameBox.Text);
        _editingHost.User = NullIfBlank(HostUserBox.Text);
        _editingHost.Port = port;
        _editingHost.IdentityFile = NullIfBlank(HostIdentityFileBox.Text);
        _editingHost.ProxyCommand = NullIfBlank(HostProxyCommandBox.Text);

        PersistConfigAsync().FireAndForget(nameof(PersistConfigAsync), isUserAction: true);
    }

    private void DeleteHostButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editingHost is null) return;
        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
        var confirm = FluentMessageBox.Show(owner,
            $"Remove host '{_editingHost.HostPattern}' from ~/.ssh/config?",
            "Delete host",
            MessageBoxButton.YesNo,
            FluentMessageBoxIcon.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        _hosts.Remove(_editingHost);
        ApplyHostToEditor(null);
        PersistConfigAsync().FireAndForget(nameof(PersistConfigAsync), isUserAction: true);
    }

    private async Task PersistConfigAsync()
    {
        if (_sshService is null) return;
        var entries = _hosts.Select(h => h.ToEntry()).ToList();
        try
        {
            await _sshService.WriteSshConfigAsync(entries).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FluentMessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                $"Could not write ~/.ssh/config: {ex.Message}",
                "Save SSH config",
                MessageBoxButton.OK,
                FluentMessageBoxIcon.Warning);
        }
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // --- Phase 4: ssh-agent integration ------------------------------------

    private void ReloadAgentButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshAgentAsync().FireAndForget(nameof(RefreshAgentAsync), isUserAction: true);
    }

    private async void AddKeyToAgent_Click(object sender, RoutedEventArgs e)
    {
        if (_sshService is null) return;
        if ((sender as Button)?.Tag is not SshKeyRow row) return;

        var passphrase = PassphrasePromptDialog.Prompt(
            Window.GetWindow(this),
            $"Enter passphrase for {row.DisplayName} (leave blank if the key has none):");
        if (passphrase is null) return; // user cancelled

        var result = await _sshService.AddKeyToAgentAsync(row.PrivateKeyPath, passphrase).ConfigureAwait(true);
        if (!result.Success)
        {
            FluentMessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                string.IsNullOrWhiteSpace(result.Message) ? "ssh-add failed." : result.Message,
                "Add key to agent",
                MessageBoxButton.OK,
                FluentMessageBoxIcon.Warning);
        }
        await RefreshAgentAsync().ConfigureAwait(true);
    }

    private async void RemoveAgentKey_Click(object sender, RoutedEventArgs e)
    {
        if (_sshService is null) return;
        if ((sender as Button)?.Tag is not AgentKeyRow row) return;

        // ssh-add -d wants the private-key path. We only know the
        // fingerprint here, so map back via the keys-on-disk list.
        var match = _keys.FirstOrDefault(k =>
            string.Equals(k.Fingerprint, row.Fingerprint, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            FluentMessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                "Couldn't locate the matching private key on disk. Remove with `ssh-add -D` (all keys) or via a terminal.",
                "Remove key",
                MessageBoxButton.OK,
                FluentMessageBoxIcon.Information);
            return;
        }

        var result = await _sshService.RemoveKeyFromAgentAsync(match.PrivateKeyPath).ConfigureAwait(true);
        if (!result.Success)
        {
            FluentMessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                string.IsNullOrWhiteSpace(result.Message) ? "ssh-add -d failed." : result.Message,
                "Remove key",
                MessageBoxButton.OK,
                FluentMessageBoxIcon.Warning);
        }
        await RefreshAgentAsync().ConfigureAwait(true);
    }

    // --- Display rows ------------------------------------------------------

    private sealed class SshKeyRow
    {
        public string PublicKeyPath { get; init; } = string.Empty;
        public string PrivateKeyPath { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string AlgorithmDisplay { get; init; } = string.Empty;
        public string Comment { get; init; } = string.Empty;
        public string Fingerprint { get; init; } = string.Empty;
        public string BitsDisplay { get; init; } = string.Empty;
        public bool HasComment => !string.IsNullOrWhiteSpace(Comment);
        public bool HasPrivateKey { get; init; }

        public static SshKeyRow From(SshPublicKey k) => new()
        {
            PublicKeyPath = k.PublicKeyPath,
            PrivateKeyPath = k.PrivateKeyPath,
            DisplayName = k.DisplayName,
            AlgorithmDisplay = k.Algorithm.ToString().ToUpperInvariant(),
            Comment = k.Comment,
            Fingerprint = string.IsNullOrEmpty(k.Fingerprint) ? "(fingerprint unavailable)" : k.Fingerprint,
            BitsDisplay = k.KeyBits is { } b ? $"{b} bits" : string.Empty,
            HasPrivateKey = k.HasPrivateKey,
        };
    }

    /// <summary>
    /// View-model row for one Host stanza. Implements
    /// <see cref="System.ComponentModel.INotifyPropertyChanged"/> on every
    /// displayed property so the ListBox's <c>HostPattern</c> /
    /// <c>SubtitleDisplay</c> bindings refresh after an in-place edit
    /// (without this, edits to HostName left the list rendering the
    /// stale value).
    /// </summary>
    private sealed class HostRow : System.ComponentModel.INotifyPropertyChanged
    {
        private string _hostPattern = string.Empty;
        private string? _hostName;
        private string? _user;
        private int? _port;
        private string? _identityFile;
        private string? _proxyCommand;
        private IReadOnlyList<SshConfigOption> _extraOptions = [];

        public string HostPattern
        {
            get => _hostPattern;
            set { if (_hostPattern == value) return; _hostPattern = value; Raise(nameof(HostPattern)); Raise(nameof(SubtitleDisplay)); }
        }
        public string? HostName
        {
            get => _hostName;
            set { if (_hostName == value) return; _hostName = value; Raise(nameof(HostName)); Raise(nameof(SubtitleDisplay)); }
        }
        public string? User
        {
            get => _user;
            set { if (_user == value) return; _user = value; Raise(nameof(User)); Raise(nameof(SubtitleDisplay)); }
        }
        public int? Port
        {
            get => _port;
            set { if (_port == value) return; _port = value; Raise(nameof(Port)); Raise(nameof(SubtitleDisplay)); }
        }
        public string? IdentityFile
        {
            get => _identityFile;
            set { if (_identityFile == value) return; _identityFile = value; Raise(nameof(IdentityFile)); }
        }
        public string? ProxyCommand
        {
            get => _proxyCommand;
            set { if (_proxyCommand == value) return; _proxyCommand = value; Raise(nameof(ProxyCommand)); }
        }
        public IReadOnlyList<SshConfigOption> ExtraOptions
        {
            get => _extraOptions;
            set { _extraOptions = value; Raise(nameof(ExtraOptions)); }
        }

        /// <summary>
        /// Secondary line under the host pattern in the list — the
        /// "what does this stanza actually resolve to" view. Shape is
        /// <c>user@host:port</c>, with fields skipped when blank.
        /// Always includes the hostname when set (even when it matches
        /// the pattern), so the row reads <c>git@github.com</c> rather
        /// than just <c>git</c> for the canonical "Host github.com /
        /// HostName github.com / User git" stanza.
        /// </summary>
        public string SubtitleDisplay
        {
            get
            {
                var hostToken = !string.IsNullOrWhiteSpace(_hostName) ? _hostName : null;
                var userToken = !string.IsNullOrWhiteSpace(_user) ? _user : null;
                if (hostToken is null && userToken is null && _port is null) return string.Empty;

                var head = (userToken, hostToken) switch
                {
                    (not null, not null) => $"{userToken}@{hostToken}",
                    (not null, null) => userToken!,
                    (null, not null) => hostToken!,
                    _ => string.Empty,
                };

                if (_port is { } p && p != 22)
                    head = head.Length > 0 ? $"{head}:{p}" : $":{p}";

                return head;
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        private void Raise(string property) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(property));

        public static HostRow From(SshConfigEntry e) => new()
        {
            HostPattern = e.HostPattern,
            HostName = e.HostName,
            User = e.User,
            Port = e.Port,
            IdentityFile = e.IdentityFile,
            ProxyCommand = e.ProxyCommand,
            ExtraOptions = e.ExtraOptions,
        };

        public SshConfigEntry ToEntry() => new()
        {
            HostPattern = HostPattern,
            HostName = HostName,
            User = User,
            Port = Port,
            IdentityFile = IdentityFile,
            ProxyCommand = ProxyCommand,
            ExtraOptions = ExtraOptions,
        };
    }

    private sealed class AgentKeyRow
    {
        public string AlgorithmDisplay { get; init; } = string.Empty;
        public string Comment { get; init; } = string.Empty;
        public string Fingerprint { get; init; } = string.Empty;
        public string BitsDisplay { get; init; } = string.Empty;

        public static AgentKeyRow From(SshAgentKey k) => new()
        {
            AlgorithmDisplay = k.Algorithm == SshKeyAlgorithm.Unknown
                ? "Key"
                : k.Algorithm.ToString().ToUpperInvariant(),
            Comment = string.IsNullOrWhiteSpace(k.Comment) ? "(no comment)" : k.Comment,
            Fingerprint = k.Fingerprint,
            BitsDisplay = k.Bits is { } b ? $"{b} bits" : string.Empty,
        };
    }
}
