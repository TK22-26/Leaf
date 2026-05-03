using System.IO;
using System.Text.Json;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for persisting application settings and repository list.
/// </summary>
public class SettingsService
{
    private static readonly string DefaultAppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Leaf");

    // Instance paths (rather than static) so tests can route a fresh
    // service at a temp folder. Production callers use the parameterless
    // ctor and get the default %APPDATA%\Leaf location.
    private readonly string AppDataFolder;
    private readonly string SettingsFile;
    private readonly string RepositoriesFile;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IgnoreReadOnlyProperties = true
    };

    public SettingsService() : this(DefaultAppDataFolder) { }

    /// <summary>
    /// Override constructor used by tests to point the service at a
    /// throwaway folder. <c>internal</c> so it never shows up in the
    /// shipped public surface but stays accessible to <c>Leaf.Tests</c>
    /// via the existing <c>InternalsVisibleTo</c>.
    /// </summary>
    internal SettingsService(string appDataFolder)
    {
        AppDataFolder = appDataFolder;
        SettingsFile = Path.Combine(AppDataFolder, "settings.json");
        RepositoriesFile = Path.Combine(AppDataFolder, "repositories.json");
        Directory.CreateDirectory(AppDataFolder);
    }

    #region Settings

    public AppSettings LoadSettings()
    {
        Log.Info("Settings", $"Loading settings from {SettingsFile}");
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            else
            {
                Log.Warn("Settings", "Settings file not found, using defaults");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Settings", $"Settings file corrupt or unreadable, using defaults: {ex.Message}");
        }

        return new AppSettings();
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFile, json);
        }
        catch (Exception ex)
        {
            Log.Error("Settings", "Failed to save settings", ex);
        }
    }

    /// <summary>
    /// Migrate credentials from the old single-provider format to the new multi-org format.
    /// Should be called on application startup.
    /// </summary>
    public void MigrateCredentialsIfNeeded(CredentialService credentialService)
    {
        var settings = LoadSettings();
        if (settings.CredentialVersion >= 1)
            return;

        Log.Info("Settings", "Starting credential migration from v0 to v1");

        // Migrate legacy GitHub credential (Leaf:GitHub -> Leaf:GitHub:{username})
        var legacyGitHub = credentialService.GetCredential("GitHub");
        if (!string.IsNullOrEmpty(legacyGitHub) && !string.IsNullOrEmpty(settings.GitHubUsername))
        {
            Log.Info("Settings", $"Migrating GitHub credential for {settings.GitHubUsername}");
            credentialService.StorePat($"GitHub:{settings.GitHubUsername}", legacyGitHub);
            credentialService.DeleteCredential("GitHub");
            credentialService.DeleteRefreshToken("GitHub");
        }

        // Migrate legacy Azure DevOps credential (Leaf:AzureDevOps -> Leaf:AzureDevOps:{org})
        var legacyAdo = credentialService.GetCredential("AzureDevOps");
        if (!string.IsNullOrEmpty(legacyAdo) && !string.IsNullOrEmpty(settings.AzureDevOpsOrganization))
        {
            Log.Info("Settings", $"Migrating Azure DevOps credential for {settings.AzureDevOpsOrganization}");
            credentialService.StorePat($"AzureDevOps:{settings.AzureDevOpsOrganization}", legacyAdo);
            credentialService.DeleteCredential("AzureDevOps");
            credentialService.DeleteRefreshToken("AzureDevOps");
        }

        settings.CredentialVersion = 1;
        SaveSettings(settings);
        Log.Info("Settings", "Credential migration to v1 complete");
    }

    #endregion

    #region Repositories

    public RepositoryData LoadRepositories()
    {
        try
        {
            if (File.Exists(RepositoriesFile))
            {
                var json = File.ReadAllText(RepositoriesFile);
                return JsonSerializer.Deserialize<RepositoryData>(json, JsonOptions) ?? new RepositoryData();
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Settings", $"Repositories file corrupt or unreadable, using defaults: {ex.Message}");
        }

        return new RepositoryData();
    }

    public void SaveRepositories(RepositoryData data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(RepositoriesFile, json);
        }
        catch (Exception ex)
        {
            Log.Error("Settings", "Failed to save repositories", ex);
        }
    }

    #endregion
}

/// <summary>
/// Application settings.
/// </summary>
public class AppSettings
{
    // Version for credential migration
    public int CredentialVersion { get; set; } = 0;

    public string Theme { get; set; } = "System";
    public string DefaultClonePath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string AzureDevOpsOrganization { get; set; } = string.Empty;
    public string GitHubUsername { get; set; } = string.Empty;
    public string DefaultAiProvider { get; set; } = string.Empty;
    public bool IsClaudeConnected { get; set; }
    public bool IsGeminiConnected { get; set; }
    public bool IsCodexConnected { get; set; }
    public int AiCliTimeoutSeconds { get; set; } = 60;
    public double WindowWidth { get; set; } = 1200;
    public double WindowHeight { get; set; } = 700;
    public double WindowLeft { get; set; } = -1;
    public double WindowTop { get; set; } = -1;
    public bool IsCommitDetailVisible { get; set; } = true;
    public bool IsRepoPaneCollapsed { get; set; } = false;
    public double RepoPaneWidth { get; set; } = 220;
    public string? LastSelectedRepositoryPath { get; set; }
    public bool IsTerminalVisible { get; set; } = false;
    public double TerminalHeight { get; set; } = 220;
    public bool TerminalAutoScroll { get; set; } = true;
    public bool TerminalLogGitCommands { get; set; } = true;
    public int TerminalMaxLines { get; set; } = 2000;
    public double TerminalFontSize { get; set; } = 12;
    public string TerminalShellExecutable { get; set; } = "cmd.exe";
    public string TerminalShellArguments { get; set; } = "/c {command}";

    // GitFlow default settings
    public string GitFlowDefaultMainBranch { get; set; } = "main";
    public string GitFlowDefaultDevelopBranch { get; set; } = "develop";
    public string GitFlowDefaultFeaturePrefix { get; set; } = "feature/";
    public string GitFlowDefaultReleasePrefix { get; set; } = "release/";
    public string GitFlowDefaultHotfixPrefix { get; set; } = "hotfix/";
    public string GitFlowDefaultVersionTagPrefix { get; set; } = "v";
    public bool GitFlowDefaultDeleteBranch { get; set; } = true;
    public bool GitFlowDefaultGenerateChangelog { get; set; } = true;

    // Ollama settings (local LLM)
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
    public string OllamaSelectedModel { get; set; } = string.Empty;

    // Watched folders for auto-discovery of new repositories
    public List<string> WatchedFolders { get; set; } = [];

    // Multi-remote sync behavior
    public bool SyncAllRemotes { get; set; } = false;

    // UI display
    public bool CompactFileList { get; set; } = false;

    // Merge editor accessibility / ergonomics. When true, all merge-editor
    // motion helpers (scroll animations, pulse, bounce, popover entrance,
    // range-resolve crossfade) write their end-state instantly instead of
    // tweening. Matches the plan §Risks "opt-out setting under
    // AppSettings.ReduceMotion" — paired with the system prefers-reduced-
    // motion convention, though WPF has no native binding for that OS
    // preference so the toggle stays explicit.
    public bool ReduceMotion { get; set; } = false;

    // Merge palette user override. When non-null and the file exists, the
    // XAML at this absolute path is merged last into the running
    // Merge.xaml umbrella, so its Merge.* tokens override the shipped
    // Dark / Light palette. Enables the plan D1 "user-palette override"
    // goal without a formal UI — drop a file in place and set the path,
    // MergeThemeSwitcher picks it up on Initialize + palette flip.
    public string? CustomMergePalettePath { get; set; }

    // Whether the collapsible "Options" row under the commit input is
    // expanded (reveals the Amend checkbox). Persisted so users who
    // amend often don't have to re-open it every launch.
    public bool IsCommitOptionsExpanded { get; set; } = false;

    // Logging
    public string LogLevel { get; set; } = "Normal";

    // Error handling (plan §1.3 / §1.4)
    // When true, exceptions thrown by background operations (tooltip
    // previews, passive event handlers, auto-fetch, lazy loading) surface
    // as toasts in addition to being logged. Default false — most users
    // only want to see errors from actions they explicitly initiated.
    public bool ShowBackgroundOperationErrors { get; set; } = false;

    // AI merge assistant (plan §5 — Phase 5 AI-Assisted Resolution).
    // Opt-in only: disabled and no MCP server path by default. First click
    // on "Ask AI" in the merge editor triggers a one-time consent dialog
    // that, when acknowledged, flips AiMergeConsentGiven to true — after
    // which clicks go straight through. Resetting either flag to false
    // from settings re-triggers the consent dialog on next use.
    public bool AiMergeEnabled { get; set; } = false;
    public bool AiMergeConsentGiven { get; set; } = false;
    public string AiMergeMcpServerPath { get; set; } = string.Empty;

    // Merge editor layout (C1 grid splitters). FileListWidth is an absolute
    // pixel width; the three ratio properties are star-values that map to
    // ColumnDefinition.Width / RowDefinition.Height for the flexible panes.
    public double MergeFileListWidth { get; set; } = 280;
    public double MergeOursPaneRatio { get; set; } = 1.0;
    public double MergeTheirsPaneRatio { get; set; } = 1.0;
    public double MergeResultRowRatio { get; set; } = 1.0;

    // §5.9 customisable shortcuts. Keyed by the stable string id from
    // ShortcutCommandId; value is the gesture string (e.g. "Ctrl+Shift+P")
    // or empty when the user has explicitly unbound a shortcut. Only
    // entries that diverge from the registered default get persisted —
    // ShortcutService prunes the default-equal cases when it writes.
    public Dictionary<string, string> ShortcutOverrides { get; set; } = [];

    // §5.14 graph branch colour palette. Id matches a built-in
    // (BranchColorPaletteRegistry.DefaultId / OkabeItoId / PastelId /
    // HighContrastId) or a custom palette in CustomBranchColorPalettes.
    // Empty / unknown id falls back to the registry default — survives
    // a hand-edited settings.json that references a deleted palette.
    public string DefaultBranchColorPaletteId { get; set; } = string.Empty;

    // User-defined custom palettes. Built-in palettes are not persisted
    // here — the registry composes them at runtime so updates to the
    // shipped colour set apply automatically on next launch.
    public List<Leaf.Models.BranchColorPalette> CustomBranchColorPalettes { get; set; } = [];

    // §5.15 commit templates (global scope). Built-in presets are NOT
    // persisted here — they're composed at runtime by
    // CommitTemplateService so shipped updates apply automatically. This
    // list holds only user-created custom templates.
    public List<Leaf.Models.CommitTemplate> CommitTemplates { get; set; } = [];

    // §5.15 user tweaks to built-in presets, keyed by preset id. Lets a
    // user adjust the body or ticket regex of a shipped template without
    // duplicating the row. Empty by default; an entry here shadows the
    // shipped values for that preset.
    public Dictionary<string, Leaf.Models.CommitTemplate> CommitTemplateOverrides { get; set; } = [];

    // §5.15 last-applied template id for the keyboard-shortcut Apply
    // path. When the user presses Ctrl+T without opening the picker,
    // we reapply this template if it still exists.
    public string LastUsedCommitTemplateId { get; set; } = string.Empty;

    // §5.15 Phase 4 Conventional Commits structured form: persisted scope
    // history so the AutoSuggest-style scope input can offer recently-used
    // values. Capped at the most recent 20 entries.
    public List<string> ConventionalCommitScopeHistory { get; set; } = [];

    // §5.15 Phase 4: whether the Conventional Commits structured form is
    // currently the active commit input. Persisted across launches so a
    // user who opts in stays opted in.
    public bool UseConventionalCommitsForm { get; set; } = false;

    // §5.15 master toggle. When false, the commit panel's templates icon
    // button is hidden and the Ctrl+T shortcut is a no-op (the picker is
    // built but never shown). Lets users who don't want the feature get
    // it out of the way without losing their stored templates. Default
    // true so existing users see no change.
    public bool CommitTemplatesEnabled { get; set; } = true;
}

/// <summary>
/// Persisted repository data.
/// </summary>
public class RepositoryData
{
    public List<RepositoryInfo> Repositories { get; set; } = [];
    public List<RepositoryGroup> CustomGroups { get; set; } = [];
}
