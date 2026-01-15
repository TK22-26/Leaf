namespace Leaf.Models;

/// <summary>
/// Represents the current GitFlow status of a repository.
/// </summary>
public class GitFlowStatus
{
    /// <summary>
    /// Whether GitFlow has been initialized for this repository.
    /// </summary>
    public bool IsInitialized { get; set; }

    /// <summary>
    /// The type of the currently checked out branch.
    /// </summary>
    public GitFlowBranchType CurrentBranchType { get; set; } = GitFlowBranchType.None;

    /// <summary>
    /// The name portion of the current flow branch (without prefix).
    /// For example, "my-feature" for "feature/my-feature".
    /// </summary>
    public string? CurrentFlowName { get; set; }

    /// <summary>
    /// Full name of the current branch.
    /// </summary>
    public string? CurrentBranchName { get; set; }

    /// <summary>
    /// Whether there is an active feature branch in progress.
    /// </summary>
    public bool HasActiveFeature { get; set; }

    /// <summary>
    /// Whether there is an active release branch in progress.
    /// </summary>
    public bool HasActiveRelease { get; set; }

    /// <summary>
    /// Whether there is an active hotfix branch in progress.
    /// </summary>
    public bool HasActiveHotfix { get; set; }

    /// <summary>
    /// The current version detected from tags or version files.
    /// </summary>
    public SemanticVersion? CurrentVersion { get; set; }

    /// <summary>
    /// List of active feature branch names.
    /// </summary>
    public List<string> ActiveFeatures { get; set; } = [];

    /// <summary>
    /// List of active release branch names.
    /// </summary>
    public List<string> ActiveReleases { get; set; } = [];

    /// <summary>
    /// List of active hotfix branch names.
    /// </summary>
    public List<string> ActiveHotfixes { get; set; } = [];

    /// <summary>
    /// The GitFlow configuration for this repository.
    /// </summary>
    public GitFlowConfig? Config { get; set; }

    /// <summary>
    /// Whether the current branch is a GitFlow-managed branch.
    /// </summary>
    public bool IsOnGitFlowBranch => CurrentBranchType != GitFlowBranchType.None;

    /// <summary>
    /// Whether the current branch can be "finished" (feature, release, or hotfix).
    /// </summary>
    public bool CanFinishCurrentBranch => CurrentBranchType is
        GitFlowBranchType.Feature or
        GitFlowBranchType.Release or
        GitFlowBranchType.Hotfix;

    /// <summary>
    /// Gets a human-readable description of the current GitFlow state.
    /// </summary>
    public string GetStatusDescription()
    {
        if (!IsInitialized)
            return "GitFlow not initialized";

        return CurrentBranchType switch
        {
            GitFlowBranchType.Main => "On main branch",
            GitFlowBranchType.Develop => "On develop branch",
            GitFlowBranchType.Feature => $"Feature: {CurrentFlowName}",
            GitFlowBranchType.Release => $"Release: {CurrentFlowName}",
            GitFlowBranchType.Hotfix => $"Hotfix: {CurrentFlowName}",
            GitFlowBranchType.Support => $"Support: {CurrentFlowName}",
            _ => "Not on a GitFlow branch"
        };
    }
}
