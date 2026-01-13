namespace Leaf.Models;

/// <summary>
/// Represents a branch label to display on the git graph.
/// Tracks whether the branch is local, remote, or both.
/// </summary>
public class BranchLabel
{
    /// <summary>
    /// Display name of the branch (without remote prefix).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// True if this branch exists locally.
    /// </summary>
    public bool IsLocal { get; set; }

    /// <summary>
    /// True if this branch exists on a remote.
    /// </summary>
    public bool IsRemote { get; set; }

    /// <summary>
    /// Remote name (e.g., "origin") if this is a remote branch.
    /// </summary>
    public string? RemoteName { get; set; }

    /// <summary>
    /// True if local and remote are at the same commit (up-to-date).
    /// </summary>
    public bool IsSynced => IsLocal && IsRemote;

    /// <summary>
    /// True if this is the current (checked out) branch.
    /// </summary>
    public bool IsCurrent { get; set; }

    /// <summary>
    /// Full reference name for display purposes.
    /// </summary>
    public string FullName => IsRemote && !IsLocal && RemoteName != null
        ? $"{RemoteName}/{Name}"
        : Name;
}
