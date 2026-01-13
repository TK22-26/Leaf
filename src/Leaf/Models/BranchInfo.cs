namespace Leaf.Models;

/// <summary>
/// POCO representing a Git branch.
/// </summary>
public class BranchInfo
{
    /// <summary>
    /// Full name of the branch (e.g., "refs/heads/main").
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Friendly name of the branch (e.g., "main").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// True if this is the currently checked out branch.
    /// </summary>
    public bool IsCurrent { get; set; }

    /// <summary>
    /// True if this is a remote tracking branch.
    /// </summary>
    public bool IsRemote { get; set; }

    /// <summary>
    /// Name of the remote (e.g., "origin") if this is a remote branch.
    /// </summary>
    public string? RemoteName { get; set; }

    /// <summary>
    /// Name of the upstream tracking branch (if any).
    /// </summary>
    public string? TrackingBranchName { get; set; }

    /// <summary>
    /// SHA of the commit this branch points to.
    /// </summary>
    public string TipSha { get; set; } = string.Empty;

    /// <summary>
    /// Number of commits ahead of tracking branch.
    /// </summary>
    public int AheadBy { get; set; }

    /// <summary>
    /// Number of commits behind tracking branch.
    /// </summary>
    public int BehindBy { get; set; }

    /// <summary>
    /// True if this is the main/master/develop branch (highway branch).
    /// </summary>
    public bool IsMainBranch => Name is "main" or "master" or "develop";

    /// <summary>
    /// Expand state for tree views.
    /// </summary>
    public bool IsExpanded { get; set; }
}
