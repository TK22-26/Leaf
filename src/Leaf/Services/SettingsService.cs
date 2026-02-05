using System.IO;
using System.Text.Json;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for persisting application settings and repository list.
/// </summary>
public class SettingsService
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Leaf");

    private static readonly string SettingsFile = Path.Combine(AppDataFolder, "settings.json");
    private static readonly string RepositoriesFile = Path.Combine(AppDataFolder, "repositories.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IgnoreReadOnlyProperties = true
    };

    public SettingsService()
    {
        // Ensure app data folder exists
        Directory.CreateDirectory(AppDataFolder);
    }

    #region Settings

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // Return defaults on error
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
        catch
        {
            // Ignore save errors
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

        // Migrate legacy GitHub credential (Leaf:GitHub -> Leaf:GitHub:{username})
        var legacyGitHub = credentialService.GetCredential("GitHub");
        if (!string.IsNullOrEmpty(legacyGitHub) && !string.IsNullOrEmpty(settings.GitHubUsername))
        {
            credentialService.StorePat($"GitHub:{settings.GitHubUsername}", legacyGitHub);
            credentialService.DeleteCredential("GitHub");
            credentialService.DeleteRefreshToken("GitHub");
        }

        // Migrate legacy Azure DevOps credential (Leaf:AzureDevOps -> Leaf:AzureDevOps:{org})
        var legacyAdo = credentialService.GetCredential("AzureDevOps");
        if (!string.IsNullOrEmpty(legacyAdo) && !string.IsNullOrEmpty(settings.AzureDevOpsOrganization))
        {
            credentialService.StorePat($"AzureDevOps:{settings.AzureDevOpsOrganization}", legacyAdo);
            credentialService.DeleteCredential("AzureDevOps");
            credentialService.DeleteRefreshToken("AzureDevOps");
        }

        settings.CredentialVersion = 1;
        SaveSettings(settings);
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
        catch
        {
            // Return defaults on error
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
        catch
        {
            // Ignore save errors
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
}

/// <summary>
/// Persisted repository data.
/// </summary>
public class RepositoryData
{
    public List<RepositoryInfo> Repositories { get; set; } = [];
    public List<RepositoryGroup> CustomGroups { get; set; } = [];
}
