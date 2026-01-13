namespace Leaf.Models;

/// <summary>
/// POCO representing a Git remote.
/// </summary>
public class RemoteInfo
{
    /// <summary>
    /// Name of the remote (e.g., "origin").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Fetch URL of the remote.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Push URL of the remote (may differ from fetch URL).
    /// </summary>
    public string? PushUrl { get; set; }

    /// <summary>
    /// True if this is an Azure DevOps remote.
    /// </summary>
    public bool IsAzureDevOps =>
        Url.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
        Url.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True if this is a GitHub remote.
    /// </summary>
    public bool IsGitHub =>
        Url.Contains("github.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True if this is a GitLab remote.
    /// </summary>
    public bool IsGitLab =>
        Url.Contains("gitlab.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Extract organization name from Azure DevOps URL.
    /// </summary>
    public string? AzureDevOpsOrganization
    {
        get
        {
            if (!IsAzureDevOps) return null;

            // Pattern: https://dev.azure.com/{org}/...
            if (Url.Contains("dev.azure.com"))
            {
                var parts = new Uri(Url).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length > 0 ? parts[0] : null;
            }

            // Pattern: https://{org}.visualstudio.com/...
            if (Url.Contains("visualstudio.com"))
            {
                var host = new Uri(Url).Host;
                return host.Split('.')[0];
            }

            return null;
        }
    }
}
