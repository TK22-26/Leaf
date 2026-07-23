using System.Collections.ObjectModel;

namespace Leaf.Models;

/// <summary>
/// Type of remote hosting service.
/// </summary>
public enum RemoteType
{
    Other,
    GitHub,
    AzureDevOps
}

/// <summary>
/// Groups remote branches by remote name (e.g., origin, upstream).
/// </summary>
public class RemoteBranchGroup
{
    /// <summary>
    /// Remote name (e.g., "origin", "upstream").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Remote URL.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Type of remote hosting service (GitHub, AzureDevOps, or Other).
    /// </summary>
    public RemoteType RemoteType { get; set; } = RemoteType.Other;

    /// <summary>
    /// Ungrouped branches from this remote (no "/" prefix in the
    /// remote-relative name). Branches with a prefix are placed under
    /// <see cref="DirectoryGroups"/> instead, mirroring the LOCAL/GITFLOW
    /// tree layout so the sidebar reads consistently across sections.
    /// </summary>
    public ObservableCollection<BranchInfo> Branches { get; set; } = [];

    /// <summary>
    /// Per-prefix directory groups for branches whose remote-relative
    /// name contains a "/" (e.g. "feature/foo", "hotfix/bar"). Issue #29:
    /// without this, the REMOTE section was a flat list while LOCAL and
    /// GITFLOW were folder-organised.
    /// </summary>
    public ObservableCollection<DirectoryBranchGroup> DirectoryGroups { get; set; } = [];

    /// <summary>
    /// Whether this remote group is expanded.
    /// </summary>
    public bool IsExpanded { get; set; } = true;

    /// <summary>
    /// Whether this is the default remote for push operations.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// True when this group is NOT a configured remote — a
    /// <c>refs/remotes/&lt;name&gt;/*</c> hierarchy with no matching
    /// <c>[remote]</c> in the config. These are local debris left by
    /// ad-hoc <c>git fetch &lt;url&gt; …:refs/remotes/&lt;name&gt;/*</c>
    /// commands; they back nothing on any server and are untouched by
    /// fetch/prune. Shown as their own node (never merged into origin)
    /// so they can't masquerade as a real remote's branches.
    /// </summary>
    public bool IsOrphaned { get; set; }

    /// <summary>
    /// Display name for the service type.
    /// </summary>
    public string ServiceDisplayName => IsOrphaned
        ? "Local-only (not a configured remote)"
        : RemoteType switch
        {
            RemoteType.GitHub => "GitHub",
            RemoteType.AzureDevOps => "Azure DevOps",
            _ => "Git"
        };

    /// <summary>
    /// Remote groups are never "current" - this silences binding warnings in TreeView.
    /// </summary>
    public bool IsCurrent => false;

    /// <summary>
    /// Remote groups are never "selected" - this silences binding warnings in TreeView.
    /// </summary>
    public bool IsSelected => false;

    /// <summary>
    /// Determines the remote type from a URL.
    /// </summary>
    public static RemoteType GetRemoteTypeFromUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return RemoteType.Other;

        if (url.Contains("github.com", StringComparison.OrdinalIgnoreCase))
            return RemoteType.GitHub;

        if (url.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase))
            return RemoteType.AzureDevOps;

        return RemoteType.Other;
    }
}
