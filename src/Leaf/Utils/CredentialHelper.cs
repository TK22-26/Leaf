namespace Leaf.Utils;

/// <summary>
/// Helper class for mapping remote URLs to credential storage keys.
/// </summary>
public static class CredentialHelper
{
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
