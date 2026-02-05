namespace Leaf.Models;

/// <summary>
/// Information about a remote where a branch exists.
/// </summary>
public class RemoteBranchInfo
{
    public string RemoteName { get; set; } = string.Empty;
    public RemoteType RemoteType { get; set; } = RemoteType.Other;
    public string? TipSha { get; set; }
}

/// <summary>
/// Represents a branch label to display on the git graph.
/// Tracks whether the branch is local, remote, or both.
/// A single label can represent a branch that exists on multiple remotes.
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
    /// List of remotes where this branch exists.
    /// </summary>
    public List<RemoteBranchInfo> Remotes { get; set; } = [];

    /// <summary>
    /// True if this branch exists on at least one remote.
    /// </summary>
    public bool IsRemote => Remotes.Count > 0;

    /// <summary>
    /// Primary remote name (first remote in list) for backwards compatibility.
    /// </summary>
    public string? RemoteName => Remotes.FirstOrDefault()?.RemoteName;

    /// <summary>
    /// Primary remote type (first remote in list) for backwards compatibility.
    /// </summary>
    public RemoteType RemoteType => Remotes.FirstOrDefault()?.RemoteType ?? RemoteType.Other;

    /// <summary>
    /// True if local and remote are at the same commit (up-to-date).
    /// </summary>
    public bool IsSynced => IsLocal && IsRemote;

    /// <summary>
    /// True if this is the current (checked out) branch.
    /// </summary>
    public bool IsCurrent { get; set; }

    /// <summary>
    /// SHA of the commit this branch points to (local tip, or first remote tip if not local).
    /// Used for checkout operations from the git graph.
    /// </summary>
    public string? TipSha { get; set; }

    /// <summary>
    /// Full reference name for display purposes.
    /// </summary>
    public string FullName => IsRemote && !IsLocal && RemoteName != null
        ? $"{RemoteName}/{Name}"
        : Name;
}
