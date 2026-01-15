using System.Text.Json;
using System.Text.RegularExpressions;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for GitFlow workflow operations.
/// Manages feature, release, and hotfix branches following the GitFlow branching model.
/// </summary>
public partial class GitFlowService : IGitFlowService
{
    private readonly IGitService _gitService;
    private const string ConfigFileName = ".gitflow";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public GitFlowService(IGitService gitService)
    {
        _gitService = gitService;
    }

    #region Initialization

    public async Task<bool> IsInitializedAsync(string repoPath)
    {
        var config = await GetConfigAsync(repoPath);
        return config?.IsInitialized == true;
    }

    public async Task InitializeAsync(string repoPath, GitFlowConfig config)
    {
        // Ensure main branch exists
        var branches = await _gitService.GetBranchesAsync(repoPath);
        var mainExists = branches.Any(b => b.Name == config.MainBranch && !b.IsRemote);

        if (!mainExists)
        {
            throw new InvalidOperationException($"Main branch '{config.MainBranch}' does not exist.");
        }

        // Create develop branch if it doesn't exist
        var developExists = branches.Any(b => b.Name == config.DevelopBranch && !b.IsRemote);
        if (!developExists)
        {
            // Checkout main first to ensure we branch from it
            await _gitService.CheckoutAsync(repoPath, config.MainBranch);
            await _gitService.CreateBranchAsync(repoPath, config.DevelopBranch, checkout: true);
        }

        config.IsInitialized = true;
        await SaveConfigAsync(repoPath, config);
    }

    public Task<GitFlowConfig?> GetConfigAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            var configPath = System.IO.Path.Combine(repoPath, ConfigFileName);
            if (!System.IO.File.Exists(configPath))
            {
                return null;
            }

            try
            {
                var json = System.IO.File.ReadAllText(configPath);
                return JsonSerializer.Deserialize<GitFlowConfig>(json, JsonOptions);
            }
            catch
            {
                return null;
            }
        });
    }

    public Task SaveConfigAsync(string repoPath, GitFlowConfig config)
    {
        return Task.Run(() =>
        {
            var configPath = System.IO.Path.Combine(repoPath, ConfigFileName);
            var json = JsonSerializer.Serialize(config, JsonOptions);
            System.IO.File.WriteAllText(configPath, json);
        });
    }

    #endregion

    #region Status

    public async Task<GitFlowStatus> GetStatusAsync(string repoPath)
    {
        var config = await GetConfigAsync(repoPath);
        var status = new GitFlowStatus
        {
            IsInitialized = config?.IsInitialized == true,
            Config = config
        };

        if (!status.IsInitialized || config == null)
        {
            return status;
        }

        var branches = await _gitService.GetBranchesAsync(repoPath);
        var repoInfo = await _gitService.GetRepositoryInfoAsync(repoPath);

        status.CurrentBranchName = repoInfo.CurrentBranch;
        status.CurrentBranchType = GetBranchType(repoInfo.CurrentBranch ?? "", config);
        status.CurrentFlowName = GetFlowName(repoInfo.CurrentBranch ?? "", config);

        // Find all active GitFlow branches
        foreach (var branch in branches.Where(b => !b.IsRemote))
        {
            var branchType = GetBranchType(branch.Name, config);
            var flowName = GetFlowName(branch.Name, config);

            switch (branchType)
            {
                case GitFlowBranchType.Feature:
                    status.ActiveFeatures.Add(flowName ?? branch.Name);
                    status.HasActiveFeature = true;
                    break;
                case GitFlowBranchType.Release:
                    status.ActiveReleases.Add(flowName ?? branch.Name);
                    status.HasActiveRelease = true;
                    break;
                case GitFlowBranchType.Hotfix:
                    status.ActiveHotfixes.Add(flowName ?? branch.Name);
                    status.HasActiveHotfix = true;
                    break;
            }
        }

        // Detect current version from tags
        status.CurrentVersion = await DetectCurrentVersionAsync(repoPath);

        return status;
    }

    public GitFlowBranchType GetBranchType(string branchName, GitFlowConfig config)
    {
        if (string.IsNullOrEmpty(branchName))
            return GitFlowBranchType.None;

        if (branchName.Equals(config.MainBranch, StringComparison.OrdinalIgnoreCase))
            return GitFlowBranchType.Main;

        if (branchName.Equals(config.DevelopBranch, StringComparison.OrdinalIgnoreCase))
            return GitFlowBranchType.Develop;

        if (branchName.StartsWith(config.FeaturePrefix, StringComparison.OrdinalIgnoreCase))
            return GitFlowBranchType.Feature;

        if (branchName.StartsWith(config.ReleasePrefix, StringComparison.OrdinalIgnoreCase))
            return GitFlowBranchType.Release;

        if (branchName.StartsWith(config.HotfixPrefix, StringComparison.OrdinalIgnoreCase))
            return GitFlowBranchType.Hotfix;

        if (branchName.StartsWith(config.SupportPrefix, StringComparison.OrdinalIgnoreCase))
            return GitFlowBranchType.Support;

        return GitFlowBranchType.None;
    }

    public string? GetFlowName(string branchName, GitFlowConfig config)
    {
        var type = GetBranchType(branchName, config);
        return type switch
        {
            GitFlowBranchType.Feature => branchName[config.FeaturePrefix.Length..],
            GitFlowBranchType.Release => branchName[config.ReleasePrefix.Length..],
            GitFlowBranchType.Hotfix => branchName[config.HotfixPrefix.Length..],
            GitFlowBranchType.Support => branchName[config.SupportPrefix.Length..],
            _ => null
        };
    }

    #endregion

    #region Feature Operations

    public async Task StartFeatureAsync(string repoPath, string featureName, IProgress<string>? progress = null)
    {
        var config = await GetConfigAsync(repoPath) ?? throw new InvalidOperationException("GitFlow not initialized.");
        var branchName = $"{config.FeaturePrefix}{featureName}";

        progress?.Report($"Creating feature branch '{branchName}' from {config.DevelopBranch}...");

        // Checkout develop and create feature branch
        await _gitService.CheckoutAsync(repoPath, config.DevelopBranch);
        await _gitService.CreateBranchAsync(repoPath, branchName, checkout: true);

        progress?.Report($"Feature '{featureName}' started successfully.");
    }

    public async Task FinishFeatureAsync(string repoPath, string featureName, MergeStrategy strategy,
        bool deleteBranch, IProgress<string>? progress = null)
    {
        var config = await GetConfigAsync(repoPath) ?? throw new InvalidOperationException("GitFlow not initialized.");
        var branchName = $"{config.FeaturePrefix}{featureName}";

        progress?.Report($"Finishing feature '{featureName}'...");

        // Checkout develop
        await _gitService.CheckoutAsync(repoPath, config.DevelopBranch);

        // Merge based on strategy
        MergeResult result;
        switch (strategy)
        {
            case MergeStrategy.Squash:
                progress?.Report("Squash merging...");
                result = await _gitService.SquashMergeAsync(repoPath, branchName);
                if (result.Success)
                {
                    // For squash, we need to commit the staged changes
                    await _gitService.CommitAsync(repoPath, $"Squash merge feature '{featureName}'");
                }
                break;

            case MergeStrategy.Rebase:
                progress?.Report("Rebasing...");
                // First rebase feature onto develop, then fast-forward merge
                await _gitService.CheckoutAsync(repoPath, branchName);
                result = await _gitService.RebaseAsync(repoPath, config.DevelopBranch, progress);
                if (result.Success)
                {
                    await _gitService.CheckoutAsync(repoPath, config.DevelopBranch);
                    result = await _gitService.FastForwardAsync(repoPath, branchName);
                }
                break;

            default: // Merge
                progress?.Report("Merging with --no-ff...");
                result = await _gitService.MergeBranchAsync(repoPath, branchName);
                break;
        }

        if (!result.Success)
        {
            if (result.HasConflicts)
            {
                throw new InvalidOperationException("Merge conflicts detected. Please resolve conflicts and try again.");
            }
            throw new InvalidOperationException($"Failed to merge: {result.ErrorMessage}");
        }

        // Delete branch if requested
        if (deleteBranch)
        {
            progress?.Report($"Deleting branch '{branchName}'...");
            await _gitService.DeleteBranchAsync(repoPath, branchName, force: true);
        }

        progress?.Report($"Feature '{featureName}' finished successfully.");
    }

    public async Task PublishFeatureAsync(string repoPath, string featureName, IProgress<string>? progress = null)
    {
        var config = await GetConfigAsync(repoPath) ?? throw new InvalidOperationException("GitFlow not initialized.");
        var branchName = $"{config.FeaturePrefix}{featureName}";

        progress?.Report($"Publishing feature '{featureName}' to remote...");
        await _gitService.CheckoutAsync(repoPath, branchName);
        await _gitService.PushAsync(repoPath, progress: progress);
        progress?.Report($"Feature '{featureName}' published successfully.");
    }

    public async Task PullFeatureAsync(string repoPath, string featureName, IProgress<string>? progress = null)
    {
        var config = await GetConfigAsync(repoPath) ?? throw new InvalidOperationException("GitFlow not initialized.");
        var branchName = $"{config.FeaturePrefix}{featureName}";

        progress?.Report($"Pulling updates for feature '{featureName}'...");
        await _gitService.CheckoutAsync(repoPath, branchName);
        await _gitService.PullAsync(repoPath, progress: progress);
        progress?.Report($"Feature '{featureName}' updated successfully.");
    }

    public async Task DeleteFeatureAsync(string repoPath, string featureName, bool deleteRemote = false, IProgress<string>? progress = null)
    {
        var config = await GetConfigAsync(repoPath) ?? throw new InvalidOperationException("GitFlow not initialized.");
        var branchName = $"{config.FeaturePrefix}{featureName}";

        progress?.Report($"Deleting feature branch '{branchName}'...");
        await _gitService.DeleteBranchAsync(repoPath, branchName, force: true);

        if (deleteRemote)
        {
            progress?.Report($"Deleting remote branch...");
            await _gitService.DeleteRemoteBranchAsync(repoPath, "origin", branchName);
        }

        progress?.Report($"Feature '{featureName}' deleted.");
    }

    #endregion

    #region Release Operations

    public async Task StartReleaseAsync(string repoPath, string version, IProgress<string>? progress = null)
    {
        var config = await GetConfigAsync(repoPath) ?? throw new InvalidOperationException("GitFlow not initialized.");
        var branchName = $"{config.ReleasePrefix}{version}";

        progress?.Report($"Creating release branch '{branchName}' from {config.DevelopBranch}...");

        await _gitService.CheckoutAsync(repoPath, config.DevelopBranch);
        await _gitService.CreateBranchAsync(repoPath, branchName, checkout: true);

        progress?.Report($"Release '{version}' started successfully.");
    }

    public async Task FinishReleaseAsync(string repoPath, string version, MergeStrategy strategy,
        bool deleteBranch, string? tagMessage = null, IProgress<string>? progress = null)
    {
        var config = await GetConfigAsync(repoPath) ?? throw new InvalidOperationException("GitFlow not initialized.");
        var branchName = $"{config.ReleasePrefix}{version}";
        var tagName = $"{config.VersionTagPrefix}{version}";

        progress?.Report($"Finishing release '{version}'...");

        // First, merge into main
        progress?.Report($"Merging into {config.MainBranch}...");
        await _gitService.CheckoutAsync(repoPath, config.MainBranch);

        var mainResult = await MergeWithStrategy(repoPath, branchName, strategy, progress);
        if (!mainResult.Success)
        {
            throw new InvalidOperationException($"Failed to merge into {config.MainBranch}: {mainResult.ErrorMessage}");
        }

        // Create tag on main
        progress?.Report($"Creating tag '{tagName}'...");
        var actualTagMessage = tagMessage ?? $"Release {version}";
        await _gitService.CreateTagAsync(repoPath, tagName, actualTagMessage);

        // Then, merge into develop
        progress?.Report($"Merging into {config.DevelopBranch}...");
        await _gitService.CheckoutAsync(repoPath, config.DevelopBranch);

        var developResult = await MergeWithStrategy(repoPath, branchName, strategy, progress);
        if (!developResult.Success)
        {
            throw new InvalidOperationException($"Failed to merge into {config.DevelopBranch}: {developResult.ErrorMessage}");
        }

        // Delete branch if requested
        if (deleteBranch)
        {
            progress?.Report($"Deleting branch '{branchName}'...");
            await _gitService.DeleteBranchAsync(repoPath, branchName, force: true);
        }

        progress?.Report($"Release '{version}' finished successfully. Tag '{tagName}' created.");
    }

    public async Task PublishReleaseAsync(string repoPath, string version, IProgress<string>? progress = null)
    {
        var config = await GetConfigAsync(repoPath) ?? throw new InvalidOperationException("GitFlow not initialized.");
        var branchName = $"{config.ReleasePrefix}{version}";

        progress?.Report($"Publishing release '{version}' to remote...");
        await _gitService.CheckoutAsync(repoPath, branchName);
        await _gitService.PushAsync(repoPath, progress: progress);
        progress?.Report($"Release '{version}' published successfully.");
    }

    public async Task DeleteReleaseAsync(string repoPath, string version, bool deleteRemote = false, IProgress<string>? progress = null)
    {
        var config = await GetConfigAsync(repoPath) ?? throw new InvalidOperationException("GitFlow not initialized.");
        var branchName = $"{config.ReleasePrefix}{version}";

        progress?.Report($"Deleting release branch '{branchName}'...");
        await _gitService.DeleteBranchAsync(repoPath, branchName, force: true);

        if (deleteRemote)
        {
            progress?.Report($"Deleting remote branch...");
            await _gitService.DeleteRemoteBranchAsync(repoPath, "origin", branchName);
        }

        progress?.Report($"Release branch '{version}' deleted.");
    }

    #endregion

    #region Hotfix Operations

    public async Task StartHotfixAsync(string repoPath, string version, IProgress<string>? progress = null)
    {
        var config = await GetConfigAsync(repoPath) ?? throw new InvalidOperationException("GitFlow not initialized.");
        var branchName = $"{config.HotfixPrefix}{version}";

        progress?.Report($"Creating hotfix branch '{branchName}' from {config.MainBranch}...");

        await _gitService.CheckoutAsync(repoPath, config.MainBranch);
        await _gitService.CreateBranchAsync(repoPath, branchName, checkout: true);

        progress?.Report($"Hotfix '{version}' started successfully.");
    }

    public async Task FinishHotfixAsync(string repoPath, string version, MergeStrategy strategy,
        bool deleteBranch, string? tagMessage = null, IProgress<string>? progress = null)
    {
        var config = await GetConfigAsync(repoPath) ?? throw new InvalidOperationException("GitFlow not initialized.");
        var branchName = $"{config.HotfixPrefix}{version}";
        var tagName = $"{config.VersionTagPrefix}{version}";

        progress?.Report($"Finishing hotfix '{version}'...");

        // First, merge into main
        progress?.Report($"Merging into {config.MainBranch}...");
        await _gitService.CheckoutAsync(repoPath, config.MainBranch);

        var mainResult = await MergeWithStrategy(repoPath, branchName, strategy, progress);
        if (!mainResult.Success)
        {
            throw new InvalidOperationException($"Failed to merge into {config.MainBranch}: {mainResult.ErrorMessage}");
        }

        // Create tag on main
        progress?.Report($"Creating tag '{tagName}'...");
        var actualTagMessage = tagMessage ?? $"Hotfix {version}";
        await _gitService.CreateTagAsync(repoPath, tagName, actualTagMessage);

        // Then, merge into develop
        progress?.Report($"Merging into {config.DevelopBranch}...");
        await _gitService.CheckoutAsync(repoPath, config.DevelopBranch);

        var developResult = await MergeWithStrategy(repoPath, branchName, strategy, progress);
        if (!developResult.Success)
        {
            throw new InvalidOperationException($"Failed to merge into {config.DevelopBranch}: {developResult.ErrorMessage}");
        }

        // Delete branch if requested
        if (deleteBranch)
        {
            progress?.Report($"Deleting branch '{branchName}'...");
            await _gitService.DeleteBranchAsync(repoPath, branchName, force: true);
        }

        progress?.Report($"Hotfix '{version}' finished successfully. Tag '{tagName}' created.");
    }

    public async Task PublishHotfixAsync(string repoPath, string version, IProgress<string>? progress = null)
    {
        var config = await GetConfigAsync(repoPath) ?? throw new InvalidOperationException("GitFlow not initialized.");
        var branchName = $"{config.HotfixPrefix}{version}";

        progress?.Report($"Publishing hotfix '{version}' to remote...");
        await _gitService.CheckoutAsync(repoPath, branchName);
        await _gitService.PushAsync(repoPath, progress: progress);
        progress?.Report($"Hotfix '{version}' published successfully.");
    }

    public async Task DeleteHotfixAsync(string repoPath, string version, bool deleteRemote = false, IProgress<string>? progress = null)
    {
        var config = await GetConfigAsync(repoPath) ?? throw new InvalidOperationException("GitFlow not initialized.");
        var branchName = $"{config.HotfixPrefix}{version}";

        progress?.Report($"Deleting hotfix branch '{branchName}'...");
        await _gitService.DeleteBranchAsync(repoPath, branchName, force: true);

        if (deleteRemote)
        {
            progress?.Report($"Deleting remote branch...");
            await _gitService.DeleteRemoteBranchAsync(repoPath, "origin", branchName);
        }

        progress?.Report($"Hotfix branch '{version}' deleted.");
    }

    #endregion

    #region Support Operations

    public async Task StartSupportAsync(string repoPath, string supportName, string baseTagOrCommit, IProgress<string>? progress = null)
    {
        var config = await GetConfigAsync(repoPath) ?? throw new InvalidOperationException("GitFlow not initialized.");
        var branchName = $"{config.SupportPrefix}{supportName}";

        progress?.Report($"Creating support branch '{branchName}' from {baseTagOrCommit}...");

        // Checkout the base tag/commit and create support branch
        await _gitService.CheckoutAsync(repoPath, baseTagOrCommit);
        await _gitService.CreateBranchAsync(repoPath, branchName, checkout: true);

        progress?.Report($"Support branch '{supportName}' started successfully.");
    }

    #endregion

    #region Version Detection

    public async Task<SemanticVersion?> DetectCurrentVersionAsync(string repoPath)
    {
        // Try to get version from tags first
        var tags = await _gitService.GetTagsAsync(repoPath);
        var versionTags = tags
            .Select(t => (Tag: t, Version: t.GetSemanticVersion()))
            .Where(tv => tv.Version != null)
            .OrderByDescending(tv => tv.Version)
            .ToList();

        if (versionTags.Count > 0)
        {
            return versionTags[0].Version;
        }

        // Try to detect from version files
        return await DetectVersionFromFilesAsync(repoPath);
    }

    public async Task<SemanticVersion> SuggestNextVersionAsync(string repoPath, GitFlowBranchType branchType)
    {
        var currentVersion = await DetectCurrentVersionAsync(repoPath) ?? new SemanticVersion(0, 0, 0);

        return branchType switch
        {
            GitFlowBranchType.Release => currentVersion.BumpMinor(),
            GitFlowBranchType.Hotfix => currentVersion.BumpPatch(),
            _ => currentVersion.BumpMinor()
        };
    }

    public async Task<List<SemanticVersion>> GetVersionTagsAsync(string repoPath)
    {
        var tags = await _gitService.GetTagsAsync(repoPath);
        return tags
            .Select(t => t.GetSemanticVersion())
            .Where(v => v != null)
            .Cast<SemanticVersion>()
            .OrderByDescending(v => v)
            .ToList();
    }

    private Task<SemanticVersion?> DetectVersionFromFilesAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            // Try package.json
            var packageJsonPath = System.IO.Path.Combine(repoPath, "package.json");
            if (System.IO.File.Exists(packageJsonPath))
            {
                try
                {
                    var json = System.IO.File.ReadAllText(packageJsonPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("version", out var versionElement))
                    {
                        var version = SemanticVersion.TryParse(versionElement.GetString());
                        if (version != null) return version;
                    }
                }
                catch { /* Ignore parsing errors */ }
            }

            // Try .csproj files
            var csprojFiles = System.IO.Directory.GetFiles(repoPath, "*.csproj", System.IO.SearchOption.AllDirectories);
            foreach (var csprojFile in csprojFiles)
            {
                try
                {
                    var content = System.IO.File.ReadAllText(csprojFile);
                    var match = VersionRegex().Match(content);
                    if (match.Success)
                    {
                        var version = SemanticVersion.TryParse(match.Groups[1].Value);
                        if (version != null) return version;
                    }
                }
                catch { /* Ignore parsing errors */ }
            }

            // Try VERSION file
            var versionFilePath = System.IO.Path.Combine(repoPath, "VERSION");
            if (System.IO.File.Exists(versionFilePath))
            {
                var content = System.IO.File.ReadAllText(versionFilePath).Trim();
                var version = SemanticVersion.TryParse(content);
                if (version != null) return version;
            }

            return null;
        });
    }

    [GeneratedRegex(@"<Version>([^<]+)</Version>", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();

    #endregion

    #region Changelog

    public async Task<string> GenerateChangelogAsync(string repoPath, string? fromVersion, string? toVersion)
    {
        var config = await GetConfigAsync(repoPath);
        var tagPrefix = config?.VersionTagPrefix ?? "v";

        string? fromRef = null;
        if (!string.IsNullOrEmpty(fromVersion))
        {
            fromRef = fromVersion.StartsWith(tagPrefix) ? fromVersion : $"{tagPrefix}{fromVersion}";
        }

        var toRef = string.IsNullOrEmpty(toVersion) ? "HEAD" :
            (toVersion.StartsWith(tagPrefix) ? toVersion : $"{tagPrefix}{toVersion}");

        List<CommitInfo> commits;
        if (fromRef == null)
        {
            // Get all commits up to toRef
            commits = await _gitService.GetCommitHistoryAsync(repoPath, 500);
        }
        else
        {
            commits = await _gitService.GetCommitsBetweenAsync(repoPath, fromRef, toRef);
        }

        return GenerateChangelogFromCommits(commits, toVersion ?? "Unreleased");
    }

    public Task AppendToChangelogFileAsync(string repoPath, string changelogContent)
    {
        return Task.Run(() =>
        {
            var changelogPath = GetChangelogPath(repoPath);
            string existingContent = "";

            if (System.IO.File.Exists(changelogPath))
            {
                existingContent = System.IO.File.ReadAllText(changelogPath);
            }

            // If existing content has a header, insert after it
            var headerEnd = existingContent.IndexOf("\n## ", StringComparison.Ordinal);
            if (headerEnd > 0)
            {
                var header = existingContent[..headerEnd];
                var rest = existingContent[headerEnd..];
                existingContent = $"{header}\n\n{changelogContent}{rest}";
            }
            else if (existingContent.StartsWith("# "))
            {
                // Has a title, insert after first line
                var firstNewline = existingContent.IndexOf('\n');
                if (firstNewline > 0)
                {
                    var title = existingContent[..(firstNewline + 1)];
                    var rest = existingContent[(firstNewline + 1)..];
                    existingContent = $"{title}\n{changelogContent}\n{rest}";
                }
            }
            else
            {
                // No existing content or header, create new file
                existingContent = $"# Changelog\n\nAll notable changes to this project will be documented in this file.\n\n{changelogContent}";
            }

            System.IO.File.WriteAllText(changelogPath, existingContent);
        });
    }

    public string GetChangelogPath(string repoPath)
    {
        return System.IO.Path.Combine(repoPath, "CHANGELOG.md");
    }

    private static string GenerateChangelogFromCommits(List<CommitInfo> commits, string version)
    {
        var features = new List<string>();
        var fixes = new List<string>();
        var docs = new List<string>();
        var refactors = new List<string>();
        var other = new List<string>();

        foreach (var commit in commits)
        {
            var message = commit.MessageShort;

            if (message.StartsWith("feat:", StringComparison.OrdinalIgnoreCase) ||
                message.StartsWith("feature:", StringComparison.OrdinalIgnoreCase))
            {
                features.Add(CleanCommitMessage(message));
            }
            else if (message.StartsWith("fix:", StringComparison.OrdinalIgnoreCase) ||
                     message.StartsWith("bugfix:", StringComparison.OrdinalIgnoreCase))
            {
                fixes.Add(CleanCommitMessage(message));
            }
            else if (message.StartsWith("docs:", StringComparison.OrdinalIgnoreCase) ||
                     message.StartsWith("doc:", StringComparison.OrdinalIgnoreCase))
            {
                docs.Add(CleanCommitMessage(message));
            }
            else if (message.StartsWith("refactor:", StringComparison.OrdinalIgnoreCase))
            {
                refactors.Add(CleanCommitMessage(message));
            }
            else if (!message.StartsWith("chore:", StringComparison.OrdinalIgnoreCase) &&
                     !message.StartsWith("ci:", StringComparison.OrdinalIgnoreCase) &&
                     !message.StartsWith("test:", StringComparison.OrdinalIgnoreCase))
            {
                other.Add(message);
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## [{version}] - {DateTime.Now:yyyy-MM-dd}");
        sb.AppendLine();

        if (features.Count > 0)
        {
            sb.AppendLine("### Added");
            foreach (var item in features)
                sb.AppendLine($"- {item}");
            sb.AppendLine();
        }

        if (fixes.Count > 0)
        {
            sb.AppendLine("### Fixed");
            foreach (var item in fixes)
                sb.AppendLine($"- {item}");
            sb.AppendLine();
        }

        if (refactors.Count > 0)
        {
            sb.AppendLine("### Changed");
            foreach (var item in refactors)
                sb.AppendLine($"- {item}");
            sb.AppendLine();
        }

        if (docs.Count > 0)
        {
            sb.AppendLine("### Documentation");
            foreach (var item in docs)
                sb.AppendLine($"- {item}");
            sb.AppendLine();
        }

        if (other.Count > 0 && features.Count == 0 && fixes.Count == 0)
        {
            // Only include other if there's nothing else
            sb.AppendLine("### Other");
            foreach (var item in other.Take(10)) // Limit to 10
                sb.AppendLine($"- {item}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string CleanCommitMessage(string message)
    {
        // Remove conventional commit prefix
        var colonIndex = message.IndexOf(':');
        if (colonIndex > 0 && colonIndex < 15)
        {
            message = message[(colonIndex + 1)..].Trim();
        }

        // Capitalize first letter
        if (message.Length > 0)
        {
            message = char.ToUpper(message[0]) + message[1..];
        }

        return message;
    }

    #endregion

    #region Validation

    public async Task<(bool IsValid, string? Error)> ValidateStartFeatureAsync(string repoPath, string featureName)
    {
        var config = await GetConfigAsync(repoPath);
        if (config == null || !config.IsInitialized)
            return (false, "GitFlow is not initialized for this repository.");

        if (string.IsNullOrWhiteSpace(featureName))
            return (false, "Feature name cannot be empty.");

        var branchName = $"{config.FeaturePrefix}{featureName}";
        var branches = await _gitService.GetBranchesAsync(repoPath);

        if (branches.Any(b => b.Name.Equals(branchName, StringComparison.OrdinalIgnoreCase)))
            return (false, $"A branch named '{branchName}' already exists.");

        return (true, null);
    }

    public async Task<(bool IsValid, string? Error)> ValidateStartReleaseAsync(string repoPath, string version)
    {
        var config = await GetConfigAsync(repoPath);
        if (config == null || !config.IsInitialized)
            return (false, "GitFlow is not initialized for this repository.");

        if (string.IsNullOrWhiteSpace(version))
            return (false, "Version cannot be empty.");

        var branchName = $"{config.ReleasePrefix}{version}";
        var branches = await _gitService.GetBranchesAsync(repoPath);

        if (branches.Any(b => b.Name.Equals(branchName, StringComparison.OrdinalIgnoreCase)))
            return (false, $"A release branch for version '{version}' already exists.");

        var tags = await _gitService.GetTagsAsync(repoPath);
        var tagName = $"{config.VersionTagPrefix}{version}";
        if (tags.Any(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase)))
            return (false, $"A tag for version '{version}' already exists.");

        return (true, null);
    }

    public async Task<(bool IsValid, string? Error)> ValidateStartHotfixAsync(string repoPath, string version)
    {
        var config = await GetConfigAsync(repoPath);
        if (config == null || !config.IsInitialized)
            return (false, "GitFlow is not initialized for this repository.");

        if (string.IsNullOrWhiteSpace(version))
            return (false, "Version cannot be empty.");

        var branchName = $"{config.HotfixPrefix}{version}";
        var branches = await _gitService.GetBranchesAsync(repoPath);

        if (branches.Any(b => b.Name.Equals(branchName, StringComparison.OrdinalIgnoreCase)))
            return (false, $"A hotfix branch for version '{version}' already exists.");

        var tags = await _gitService.GetTagsAsync(repoPath);
        var tagName = $"{config.VersionTagPrefix}{version}";
        if (tags.Any(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase)))
            return (false, $"A tag for version '{version}' already exists.");

        return (true, null);
    }

    public async Task<(bool IsValid, string? Error)> ValidateFinishBranchAsync(string repoPath, string branchName, GitFlowBranchType expectedType)
    {
        var config = await GetConfigAsync(repoPath);
        if (config == null || !config.IsInitialized)
            return (false, "GitFlow is not initialized for this repository.");

        var branches = await _gitService.GetBranchesAsync(repoPath);
        if (!branches.Any(b => b.Name.Equals(branchName, StringComparison.OrdinalIgnoreCase)))
            return (false, $"Branch '{branchName}' does not exist.");

        var actualType = GetBranchType(branchName, config);
        if (actualType != expectedType)
            return (false, $"Branch '{branchName}' is not a {expectedType} branch.");

        return (true, null);
    }

    #endregion

    #region Helpers

    private async Task<MergeResult> MergeWithStrategy(string repoPath, string branchName, MergeStrategy strategy, IProgress<string>? progress)
    {
        return strategy switch
        {
            MergeStrategy.Squash => await SquashMergeWithCommit(repoPath, branchName),
            MergeStrategy.Rebase => await RebaseMerge(repoPath, branchName, progress),
            _ => await _gitService.MergeBranchAsync(repoPath, branchName)
        };
    }

    private async Task<MergeResult> SquashMergeWithCommit(string repoPath, string branchName)
    {
        var result = await _gitService.SquashMergeAsync(repoPath, branchName);
        if (result.Success)
        {
            await _gitService.CommitAsync(repoPath, $"Merge branch '{branchName}'");
        }
        return result;
    }

    private async Task<MergeResult> RebaseMerge(string repoPath, string branchName, IProgress<string>? progress)
    {
        // For rebase merge, we merge with fast-forward after the caller has rebased
        return await _gitService.MergeBranchAsync(repoPath, branchName);
    }

    #endregion
}
