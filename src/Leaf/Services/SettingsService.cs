using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
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
                json = MigrateRenamedKeys(json);
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

    /// <summary>
    /// Run one-time JSON-level renames before typed deserialization. We
    /// rewrite the on-disk file at the same time so future loads stay
    /// fast and the legacy keys disappear permanently — no per-property
    /// shim or [Obsolete] field on <see cref="AppSettings"/>, which would
    /// otherwise leak the old name forever.
    /// </summary>
    /// <remarks>
    /// Add new entries to <see cref="RenamedSettingsKeys"/> when keys move.
    /// The migration is idempotent: missing-old-key + present-new-key is a
    /// no-op. When both exist the new key wins (defensive — a hand-edited
    /// settings.json could end up in that state).
    /// </remarks>
    private string MigrateRenamedKeys(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { return json; }
        if (root is not JsonObject obj) return json;

        var changed = false;
        foreach (var (oldKey, newKey) in RenamedSettingsKeys)
        {
            if (!obj.ContainsKey(oldKey)) continue;
            var oldVal = obj[oldKey];
            var newAlreadyHasValue = obj.TryGetPropertyValue(newKey, out var existing)
                && existing is JsonValue existingValue
                && !string.IsNullOrEmpty(existingValue.GetValue<string?>() ?? string.Empty);
            if (!newAlreadyHasValue && oldVal is not null)
            {
                obj[newKey] = oldVal.DeepClone();
            }
            obj.Remove(oldKey);
            changed = true;
            Log.Info("Settings", $"Migrated renamed key '{oldKey}' → '{newKey}'.");
        }

        if (!changed) return json;

        var rewritten = root.ToJsonString(JsonOptions);
        try { File.WriteAllText(SettingsFile, rewritten); }
        catch (Exception ex)
        {
            // Persisting the migration is best-effort — if it fails the
            // in-memory settings still reflect the new shape and we'll
            // just retry the migration on next launch.
            Log.Warn("Settings", $"Failed to persist key-rename migration: {ex.Message}");
        }
        return rewritten;
    }

    /// <summary>
    /// Maps obsolete JSON property names to their replacements. Camel-cased
    /// to match the on-disk key style (<see cref="JsonOptions"/> uses
    /// <see cref="JsonNamingPolicy.CamelCase"/>). New entries land here
    /// when a setting is renamed.
    /// </summary>
    private static readonly (string OldKey, string NewKey)[] RenamedSettingsKeys =
    [
        // 2026-05: AI merge feature dropped MCP-only constraint; the
        // external-server path is now one of several providers.
        ("aiMergeMcpServerPath", "aiMergeExternalServerPath"),
    ];

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
    /// Returns the remembered answer for <paramref name="suppressionKey"/>
    /// when the user previously checked "Don't show this again" — true means
    /// "always answer Yes / OK", false means "always answer No / Cancel".
    /// Returns <c>null</c> when no preference is recorded; the caller must
    /// then show the dialog and let the user choose.
    /// </summary>
    public bool? GetSuppressedAnswer(string suppressionKey)
    {
        if (string.IsNullOrWhiteSpace(suppressionKey)) return null;
        var settings = LoadSettings();
        return settings.SuppressedMessageKeys.TryGetValue(suppressionKey, out var v) ? v : null;
    }

    /// <summary>
    /// Persist the user's answer so the dialog identified by
    /// <paramref name="suppressionKey"/> stops appearing. Pair with
    /// <see cref="GetSuppressedAnswer"/>; pair with <see cref="ClearSuppression"/>
    /// when you want a Settings UI affordance to "show me this again".
    /// </summary>
    public void SetSuppressedAnswer(string suppressionKey, bool answer)
    {
        if (string.IsNullOrWhiteSpace(suppressionKey)) return;
        var settings = LoadSettings();
        settings.SuppressedMessageKeys[suppressionKey] = answer;
        SaveSettings(settings);
        Log.Info("Settings", $"Suppressed dialog '{suppressionKey}' with answer={answer}");
    }

    /// <summary>
    /// Drop the recorded answer for <paramref name="suppressionKey"/>, so the
    /// dialog will be shown again on next invocation. No-op when the key is
    /// absent — safe to call from a "reset all hidden dialogs" Settings UI.
    /// </summary>
    public void ClearSuppression(string suppressionKey)
    {
        if (string.IsNullOrWhiteSpace(suppressionKey)) return;
        var settings = LoadSettings();
        if (settings.SuppressedMessageKeys.Remove(suppressionKey))
        {
            SaveSettings(settings);
            Log.Info("Settings", $"Cleared suppression for dialog '{suppressionKey}'");
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

    // API-key (direct-billing HTTP) transport for Claude. The actual key
    // lives in Windows Credential Manager at Leaf:AI:Claude — this file
    // only stores the connection flag, the user's chosen model, and the
    // transport selector that decides whether the merge/commit pipeline
    // dispatches to the CLI or the API client. The CLI path is unchanged
    // and remains the default for installs that don't opt in.
    public string ClaudeTransport { get; set; } = "Cli";
    public bool IsClaudeApiConnected { get; set; }
    // Model identifiers stay empty by default. After the user clicks
    // Save & Test, Leaf populates the dropdown from the provider's
    // /models endpoint and persists whatever the user picks. We avoid
    // shipping a hardcoded default because model names rotate fast and
    // a stale default would silently break new installs on the day a
    // provider deprecates the old name.
    public string ClaudeApiModel { get; set; } = string.Empty;

    // Same shape as the Claude API fields above. Key lives in
    // Credential Manager at Leaf:AI:Gemini.
    public string GeminiTransport { get; set; } = "Cli";
    public bool IsGeminiApiConnected { get; set; }
    public string GeminiApiModel { get; set; } = string.Empty;

    // OpenAI section — combines the Codex CLI path (IsCodexConnected
    // above) with the direct-billing API key path. OpenAiTransport is
    // the user's chosen sub-mode within the OpenAI section ("Cli" or
    // "Api"). Key at Leaf:AI:OpenAI for the API path; Codex CLI auth
    // is owned by the CLI itself.
    public string OpenAiTransport { get; set; } = "Cli";
    public bool IsOpenAiApiConnected { get; set; }
    public string OpenAiApiModel { get; set; } = string.Empty;

    // OpenAI-compatible custom endpoint (LM Studio, OpenRouter, vLLM,
    // Together, a corporate Azure OpenAI gateway, etc.). Same wire
    // format as OpenAI proper, different base URL + key. Stored
    // separately so a user can simultaneously have an OpenAI account
    // and a local LM Studio configured. Key at Leaf:AI:OpenAiCompatible.
    public bool IsOpenAiCompatibleConnected { get; set; }
    public string OpenAiCompatibleBaseUrl { get; set; } = string.Empty;
    public string OpenAiCompatibleModel { get; set; } = string.Empty;
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

    /// <summary>
    /// Per-dialog "Don't show this again" preferences. Keyed by a stable
    /// string identifier (e.g. <c>"branch.forceDelete"</c>); the value is
    /// the remembered answer (true = always Yes/OK, false = always No/Cancel).
    /// Read via <see cref="SettingsService.GetSuppressedAnswer"/> and written
    /// via <see cref="SettingsService.SetSuppressedAnswer"/>.
    /// </summary>
    public Dictionary<string, bool> SuppressedMessageKeys { get; set; } = [];

    // ─── Toast notification toggles ─────────────────────────────────────
    // One flag per Leaf.Models.NotificationCategory. EVERY category is
    // off by default — a user-initiated action already shows its own
    // visible result (graph refresh, branch list update, etc.), so the
    // toast is redundant noise. The user can switch any category back on
    // from Settings → Notifications if they want acknowledgement toasts
    // for that class of operation. Errors have no toggle and always show.
    public bool NotifySyncOperations { get; set; } = false;
    public bool NotifyBranchCheckout { get; set; } = false;
    public bool NotifyBranchAdmin { get; set; } = false;
    public bool NotifyMergeAndRebase { get; set; } = false;
    public bool NotifyGitFlow { get; set; } = false;
    public bool NotifyWorktree { get; set; } = false;
    public bool NotifySubmodule { get; set; } = false;
    public bool NotifyStash { get; set; } = false;
    public bool NotifyPullRequest { get; set; } = false;
    public bool NotifyPatch { get; set; } = false;
    public bool NotifyRepository { get; set; } = false;
    public bool NotifyRemoteConfig { get; set; } = false;
    public bool NotifyCancelledOperations { get; set; } = false;

    /// <summary>
    /// Power-user opt-out for the workspace inline-commit review.
    /// When false (default), clicking Commit-all in grid mode puts
    /// every dirty tile into compose state so the user reviews each
    /// AI-generated message before it ships. When true, the AI fans
    /// out in parallel and commits land immediately without surfacing
    /// the composer — same path as the previous one-click behaviour,
    /// for users who trust the AI output.
    /// </summary>
    public bool WorkspaceCommitSkipReview { get; set; } = false;

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

    // Repository sidebar nesting toggle. When true, every entry in a
    // parent's .gitmodules — including ones the user has never opened —
    // shows up as a virtual child under the parent. Clicking a virtual
    // child runs OpenSubmoduleAsRepositoryAsync (same as double-click
    // in the branch pane's SUBMODULES section) and the child becomes
    // a real entry. Off by default because the resulting tree is
    // noisy for repos with many submodules; some users prefer the
    // exhaustive view.
    public bool ShowAllSubmodulesInRepositoryList { get; set; } = false;

    // AI merge assistant. Opt-in only: disabled by default. First click
    // on "Ask AI" in the merge editor triggers a one-time consent
    // dialog that, when acknowledged, flips AiMergeConsentGiven to true
    // — after which clicks go straight through. Resetting either flag
    // to false from settings re-triggers the consent dialog on next use.
    //
    // AiMergeProvider selects which backend the router dispatches to:
    // "Claude" / "Gemini" / "Codex" / "Ollama" (CLI / HTTP wrappers
    // around the user's existing tooling) or "ExternalServer" (the
    // legacy stdio-JSON server, optional power-user / corporate path).
    // Empty string leaves the router to fall back to a sensible default
    // on first use (the user's commit-message provider if connected,
    // otherwise the first connected CLI provider).
    //
    // AiMergeExternalServerPath was previously named AiMergeMcpServerPath;
    // SettingsService.LoadSettings performs a one-time JSON-level rename
    // when an old settings file is loaded.
    public bool AiMergeEnabled { get; set; } = false;
    public bool AiMergeConsentGiven { get; set; } = false;
    public string AiMergeProvider { get; set; } = string.Empty;
    public string AiMergeExternalServerPath { get; set; } = string.Empty;

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
    // false — opt-in. Users who want the feature flip it on under
    // Settings → Commit Templates.
    public bool CommitTemplatesEnabled { get; set; } = false;
}

/// <summary>
/// Persisted repository data.
/// </summary>
public class RepositoryData
{
    public List<RepositoryInfo> Repositories { get; set; } = [];
    public List<RepositoryGroup> CustomGroups { get; set; } = [];
}
