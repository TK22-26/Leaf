using System.IO;
using System.Text.Json;
using Leaf.Models;

// Re-export auth method enums from Models for AppSettings usage
using GitHubAuthMethod = Leaf.Models.GitHubAuthMethod;
using AzureDevOpsAuthMethod = Leaf.Models.AzureDevOpsAuthMethod;

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
    public string? LastSelectedRepositoryPath { get; set; }

    // GitHub OAuth settings
    public GitHubAuthMethod GitHubAuthMethod { get; set; } = GitHubAuthMethod.PersonalAccessToken;
    public DateTime? GitHubOAuthTokenCreatedAt { get; set; }
    public string? GitHubOAuthScopes { get; set; }

    // Azure DevOps OAuth settings
    public AzureDevOpsAuthMethod AzureDevOpsAuthMethod { get; set; } = AzureDevOpsAuthMethod.PersonalAccessToken;
    public DateTime? AzureDevOpsOAuthTokenCreatedAt { get; set; }
    public DateTime? AzureDevOpsOAuthTokenExpiresAt { get; set; }
    public string? AzureDevOpsOAuthScopes { get; set; }
    public string? AzureDevOpsUserDisplayName { get; set; }

    // GitFlow default settings
    public string GitFlowDefaultMainBranch { get; set; } = "main";
    public string GitFlowDefaultDevelopBranch { get; set; } = "develop";
    public string GitFlowDefaultFeaturePrefix { get; set; } = "feature/";
    public string GitFlowDefaultReleasePrefix { get; set; } = "release/";
    public string GitFlowDefaultHotfixPrefix { get; set; } = "hotfix/";
    public string GitFlowDefaultVersionTagPrefix { get; set; } = "v";
    public bool GitFlowDefaultDeleteBranch { get; set; } = true;
    public bool GitFlowDefaultGenerateChangelog { get; set; } = true;
}

/// <summary>
/// Persisted repository data.
/// </summary>
public class RepositoryData
{
    public List<RepositoryInfo> Repositories { get; set; } = [];
    public List<RepositoryGroup> CustomGroups { get; set; } = [];
}
