using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service interface for GitFlow workflow operations.
/// </summary>
public interface IGitFlowService
{
    #region Initialization

    /// <summary>
    /// Checks if GitFlow has been initialized for the repository.
    /// </summary>
    Task<bool> IsInitializedAsync(string repoPath);

    /// <summary>
    /// Initializes GitFlow in the repository with the specified configuration.
    /// Creates the develop branch if it doesn't exist.
    /// </summary>
    Task InitializeAsync(string repoPath, GitFlowConfig config);

    /// <summary>
    /// Gets the GitFlow configuration for the repository.
    /// Returns null if GitFlow is not initialized.
    /// </summary>
    Task<GitFlowConfig?> GetConfigAsync(string repoPath);

    /// <summary>
    /// Saves the GitFlow configuration to the repository.
    /// </summary>
    Task SaveConfigAsync(string repoPath, GitFlowConfig config);

    #endregion

    #region Status

    /// <summary>
    /// Gets the current GitFlow status of the repository.
    /// </summary>
    Task<GitFlowStatus> GetStatusAsync(string repoPath);

    /// <summary>
    /// Determines the GitFlow branch type for a given branch name.
    /// </summary>
    GitFlowBranchType GetBranchType(string branchName, GitFlowConfig config);

    /// <summary>
    /// Extracts the flow name from a branch name (e.g., "my-feature" from "feature/my-feature").
    /// </summary>
    string? GetFlowName(string branchName, GitFlowConfig config);

    #endregion

    #region Feature Operations

    /// <summary>
    /// Starts a new feature branch from develop.
    /// </summary>
    Task StartFeatureAsync(string repoPath, string featureName, IProgress<string>? progress = null);

    /// <summary>
    /// Finishes a feature branch by merging it into develop.
    /// </summary>
    Task FinishFeatureAsync(string repoPath, string featureName, MergeStrategy strategy,
        bool deleteBranch, IProgress<string>? progress = null);

    /// <summary>
    /// Publishes a feature branch to the remote repository.
    /// </summary>
    Task PublishFeatureAsync(string repoPath, string featureName, IProgress<string>? progress = null);

    /// <summary>
    /// Pulls updates for a feature branch from the remote repository.
    /// </summary>
    Task PullFeatureAsync(string repoPath, string featureName, IProgress<string>? progress = null);

    /// <summary>
    /// Deletes a feature branch (local and optionally remote).
    /// </summary>
    Task DeleteFeatureAsync(string repoPath, string featureName, bool deleteRemote = false, IProgress<string>? progress = null);

    #endregion

    #region Release Operations

    /// <summary>
    /// Starts a new release branch from develop.
    /// </summary>
    Task StartReleaseAsync(string repoPath, string version, IProgress<string>? progress = null);

    /// <summary>
    /// Finishes a release branch by merging it into main AND develop, and creating a tag.
    /// </summary>
    Task FinishReleaseAsync(string repoPath, string version, MergeStrategy strategy,
        bool deleteBranch, string? tagMessage = null, IProgress<string>? progress = null);

    /// <summary>
    /// Publishes a release branch to the remote repository.
    /// </summary>
    Task PublishReleaseAsync(string repoPath, string version, IProgress<string>? progress = null);

    /// <summary>
    /// Deletes a release branch (local and optionally remote).
    /// </summary>
    Task DeleteReleaseAsync(string repoPath, string version, bool deleteRemote = false, IProgress<string>? progress = null);

    #endregion

    #region Hotfix Operations

    /// <summary>
    /// Starts a new hotfix branch from main.
    /// </summary>
    Task StartHotfixAsync(string repoPath, string version, IProgress<string>? progress = null);

    /// <summary>
    /// Finishes a hotfix branch by merging it into main AND develop, and creating a tag.
    /// </summary>
    Task FinishHotfixAsync(string repoPath, string version, MergeStrategy strategy,
        bool deleteBranch, string? tagMessage = null, IProgress<string>? progress = null);

    /// <summary>
    /// Publishes a hotfix branch to the remote repository.
    /// </summary>
    Task PublishHotfixAsync(string repoPath, string version, IProgress<string>? progress = null);

    /// <summary>
    /// Deletes a hotfix branch (local and optionally remote).
    /// </summary>
    Task DeleteHotfixAsync(string repoPath, string version, bool deleteRemote = false, IProgress<string>? progress = null);

    #endregion

    #region Support Operations

    /// <summary>
    /// Starts a new support branch from a specific tag or commit.
    /// </summary>
    Task StartSupportAsync(string repoPath, string supportName, string baseTagOrCommit, IProgress<string>? progress = null);

    #endregion

    #region Version Detection

    /// <summary>
    /// Detects the current version from git tags or version files.
    /// </summary>
    Task<SemanticVersion?> DetectCurrentVersionAsync(string repoPath);

    /// <summary>
    /// Suggests the next version based on the branch type.
    /// Release: bumps minor, Hotfix: bumps patch.
    /// </summary>
    Task<SemanticVersion> SuggestNextVersionAsync(string repoPath, GitFlowBranchType branchType);

    /// <summary>
    /// Gets all version tags from the repository.
    /// </summary>
    Task<List<SemanticVersion>> GetVersionTagsAsync(string repoPath);

    #endregion

    #region Changelog

    /// <summary>
    /// Generates a changelog from commits between two versions.
    /// </summary>
    Task<string> GenerateChangelogAsync(string repoPath, string? fromVersion, string toVersion);

    /// <summary>
    /// Appends changelog content to the CHANGELOG.md file.
    /// </summary>
    Task AppendToChangelogFileAsync(string repoPath, string changelogContent);

    /// <summary>
    /// Gets the path to the changelog file in the repository.
    /// </summary>
    string GetChangelogPath(string repoPath);

    #endregion

    #region Validation

    /// <summary>
    /// Validates that a feature can be started (no existing branch with same name).
    /// </summary>
    Task<(bool IsValid, string? Error)> ValidateStartFeatureAsync(string repoPath, string featureName);

    /// <summary>
    /// Validates that a release can be started (no existing release branch).
    /// </summary>
    Task<(bool IsValid, string? Error)> ValidateStartReleaseAsync(string repoPath, string version);

    /// <summary>
    /// Validates that a hotfix can be started (no existing hotfix branch).
    /// </summary>
    Task<(bool IsValid, string? Error)> ValidateStartHotfixAsync(string repoPath, string version);

    /// <summary>
    /// Validates that a branch can be finished (is the correct type, exists, etc.).
    /// </summary>
    Task<(bool IsValid, string? Error)> ValidateFinishBranchAsync(string repoPath, string branchName, GitFlowBranchType expectedType);

    #endregion
}
