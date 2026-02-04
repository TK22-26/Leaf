namespace Leaf.Utils;

/// <summary>
/// Supported credential providers.
/// </summary>
public enum CredentialProvider
{
    Unknown,
    GitHub,
    AzureDevOps
}

/// <summary>
/// Helper class for mapping remote URLs to credential storage keys.
/// </summary>
public static class CredentialHelper
{
    /// <summary>
    /// Extracts provider and organization from a remote URL.
    /// </summary>
    /// <param name="remoteUrl">The git remote URL (HTTPS or SSH)</param>
    /// <param name="provider">The detected credential provider</param>
    /// <param name="organization">The organization/owner extracted from the URL</param>
    /// <returns>True if extraction succeeded</returns>
    public static bool TryGetProviderAndOrg(string remoteUrl, out CredentialProvider provider, out string? organization)
    {
        provider = CredentialProvider.Unknown;
        organization = null;

        if (string.IsNullOrEmpty(remoteUrl))
            return false;

        // Try HTTPS URL parsing
        if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri))
        {
            return TryParseHttpsUrl(uri, out provider, out organization);
        }

        // Try SSH URL parsing (git@host:owner/repo.git)
        return TryParseSshUrl(remoteUrl, out provider, out organization);
    }

    /// <summary>
    /// Gets the credential key for a remote URL (e.g., "GitHub:microsoft").
    /// Returns null if provider/org cannot be determined.
    /// </summary>
    /// <param name="remoteUrl">The git remote URL</param>
    /// <returns>The credential key in format "Provider:organization", or null if not determinable</returns>
    public static string? GetCredentialKeyForUrl(string remoteUrl)
    {
        if (!TryGetProviderAndOrg(remoteUrl, out var provider, out var org))
            return null;

        if (provider == CredentialProvider.Unknown || string.IsNullOrEmpty(org))
            return null;

        return $"{provider}:{org}";
    }

    private static bool TryParseHttpsUrl(Uri uri, out CredentialProvider provider, out string? organization)
    {
        provider = CredentialProvider.Unknown;
        organization = null;

        var host = uri.Host.ToLowerInvariant();

        // GitHub: https://github.com/owner/repo
        if (host == "github.com")
        {
            provider = CredentialProvider.GitHub;
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length >= 1 && !string.IsNullOrEmpty(segments[0]))
            {
                organization = segments[0];
                return true;
            }
            return false;
        }

        // Azure DevOps: https://dev.azure.com/OrgName/Project/_git/Repo
        // Also handles: https://User@dev.azure.com/OrgName/Project/...
        if (host == "dev.azure.com")
        {
            provider = CredentialProvider.AzureDevOps;
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length >= 1 && !string.IsNullOrEmpty(segments[0]))
            {
                organization = segments[0];
                return true;
            }
            return false;
        }

        // Azure DevOps legacy: https://OrgName.visualstudio.com/...
        if (host.EndsWith(".visualstudio.com"))
        {
            provider = CredentialProvider.AzureDevOps;
            var orgEndIndex = host.IndexOf(".visualstudio.com", StringComparison.OrdinalIgnoreCase);
            if (orgEndIndex > 0)
            {
                organization = host[..orgEndIndex];
                return true;
            }
            return false;
        }

        return false;
    }

    private static bool TryParseSshUrl(string remoteUrl, out CredentialProvider provider, out string? organization)
    {
        provider = CredentialProvider.Unknown;
        organization = null;

        // Handle scp-like syntax: git@github.com:owner/repo.git
        var atIndex = remoteUrl.IndexOf('@');
        var colonIndex = remoteUrl.IndexOf(':');

        if (atIndex < 0 || colonIndex <= atIndex + 1)
            return false;

        var host = remoteUrl[(atIndex + 1)..colonIndex].ToLowerInvariant();
        var path = remoteUrl[(colonIndex + 1)..];

        // GitHub SSH: git@github.com:owner/repo.git
        if (host == "github.com")
        {
            provider = CredentialProvider.GitHub;
            var segments = path.Split('/');
            if (segments.Length >= 1 && !string.IsNullOrEmpty(segments[0]))
            {
                organization = segments[0];
                return true;
            }
            return false;
        }

        // Azure DevOps SSH: git@ssh.dev.azure.com:v3/OrgName/Project/Repo
        if (host == "ssh.dev.azure.com")
        {
            provider = CredentialProvider.AzureDevOps;
            var segments = path.Split('/');
            // Path format: v3/OrgName/Project/Repo
            if (segments.Length >= 2 && segments[0] == "v3" && !string.IsNullOrEmpty(segments[1]))
            {
                organization = segments[1];
                return true;
            }
            return false;
        }

        // Azure DevOps legacy SSH: OrgName@vs-ssh.visualstudio.com:v3/OrgName/Project/Repo
        if (host == "vs-ssh.visualstudio.com" || host.EndsWith(".vs-ssh.visualstudio.com"))
        {
            provider = CredentialProvider.AzureDevOps;
            var segments = path.Split('/');
            // Path format: v3/OrgName/Project/Repo
            if (segments.Length >= 2 && segments[0] == "v3" && !string.IsNullOrEmpty(segments[1]))
            {
                organization = segments[1];
                return true;
            }
            return false;
        }

        return false;
    }

    /// <summary>
    /// Maps a remote URL hostname to the credential storage key.
    /// </summary>
    /// <param name="host">The hostname from the remote URL</param>
    /// <returns>The credential key (e.g., "GitHub", "AzureDevOps") or the host itself for other providers</returns>
    public static string? GetCredentialKeyForHost(string host)
    {
        if (string.IsNullOrEmpty(host))
            return null;

        if (host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return "GitHub";

        if (host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase))
            return "AzureDevOps";

        // Return host as-is for other providers (may have stored by hostname)
        return host;
    }

    /// <summary>
    /// Extracts the hostname from a git remote URL.
    /// Handles both HTTPS URLs and SSH URLs (git@host:path format).
    /// </summary>
    /// <param name="remoteUrl">The remote URL</param>
    /// <param name="host">The extracted hostname if successful</param>
    /// <returns>True if hostname was extracted successfully</returns>
    public static bool TryGetRemoteHost(string remoteUrl, out string host)
    {
        host = string.Empty;

        if (string.IsNullOrEmpty(remoteUrl))
            return false;

        // Try standard URI parsing first (HTTPS URLs)
        if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            host = uri.Host;
            return true;
        }

        // Handle scp-like syntax: git@github.com:owner/repo.git
        var atIndex = remoteUrl.IndexOf('@');
        var colonIndex = remoteUrl.IndexOf(':');
        if (atIndex >= 0 && colonIndex > atIndex + 1)
        {
            host = remoteUrl[(atIndex + 1)..colonIndex];
            return !string.IsNullOrWhiteSpace(host);
        }

        return false;
    }
}
