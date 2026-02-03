namespace Leaf.Models;

/// <summary>
/// Merge strategy options for GitFlow operations.
/// </summary>
public enum MergeStrategy
{
    /// <summary>
    /// Standard merge with --no-ff (preserves full history).
    /// </summary>
    Merge,

    /// <summary>
    /// Squash all commits into one before merging.
    /// </summary>
    Squash,

    /// <summary>
    /// Rebase commits onto target branch before merging.
    /// </summary>
    Rebase,

    /// <summary>
    /// Squash and rebase: combines all commits into one and rebases onto target.
    /// Results in a clean, linear history with a single commit.
    /// </summary>
    SquashRebase
}

/// <summary>
/// GitFlow branch type classification.
/// </summary>
public enum GitFlowBranchType
{
    None,
    Main,
    Develop,
    Feature,
    Release,
    Hotfix,
    Support
}

/// <summary>
/// Configuration for GitFlow workflow in a repository.
/// Stored in .gitflow file at repository root.
/// </summary>
public class GitFlowConfig
{
    /// <summary>
    /// Whether GitFlow has been initialized for this repository.
    /// </summary>
    public bool IsInitialized { get; set; }

    /// <summary>
    /// Name of the main/production branch (default: "main").
    /// </summary>
    public string MainBranch { get; set; } = "main";

    /// <summary>
    /// Name of the development branch (default: "develop").
    /// </summary>
    public string DevelopBranch { get; set; } = "develop";

    /// <summary>
    /// Prefix for feature branches (default: "feature/").
    /// </summary>
    public string FeaturePrefix { get; set; } = "feature/";

    /// <summary>
    /// Prefix for release branches (default: "release/").
    /// </summary>
    public string ReleasePrefix { get; set; } = "release/";

    /// <summary>
    /// Prefix for hotfix branches (default: "hotfix/").
    /// </summary>
    public string HotfixPrefix { get; set; } = "hotfix/";

    /// <summary>
    /// Prefix for support branches (default: "support/").
    /// </summary>
    public string SupportPrefix { get; set; } = "support/";

    /// <summary>
    /// Prefix for version tags (default: "v").
    /// </summary>
    public string VersionTagPrefix { get; set; } = "v";

    /// <summary>
    /// Default merge strategy when finishing branches.
    /// </summary>
    public MergeStrategy DefaultMergeStrategy { get; set; } = MergeStrategy.Merge;

    /// <summary>
    /// Whether to automatically push after finishing a branch.
    /// </summary>
    public bool AutoPushAfterFinish { get; set; } = false;

    /// <summary>
    /// Whether to delete the source branch after finishing.
    /// </summary>
    public bool DeleteBranchAfterFinish { get; set; } = true;

    /// <summary>
    /// Whether to generate changelog when finishing releases.
    /// </summary>
    public bool GenerateChangelog { get; set; } = true;

    /// <summary>
    /// Creates a default GitFlow configuration.
    /// </summary>
    public static GitFlowConfig CreateDefault() => new()
    {
        IsInitialized = true,
        MainBranch = "main",
        DevelopBranch = "develop",
        FeaturePrefix = "feature/",
        ReleasePrefix = "release/",
        HotfixPrefix = "hotfix/",
        SupportPrefix = "support/",
        VersionTagPrefix = "v",
        DefaultMergeStrategy = MergeStrategy.Merge,
        AutoPushAfterFinish = false,
        DeleteBranchAfterFinish = true,
        GenerateChangelog = true
    };
}
