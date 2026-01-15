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
    /// Branches from this remote.
    /// </summary>
    public ObservableCollection<BranchInfo> Branches { get; set; } = [];

    /// <summary>
    /// Whether this remote group is expanded.
    /// </summary>
    public bool IsExpanded { get; set; } = true;

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
