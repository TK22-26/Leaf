namespace Leaf.Services;

/// <summary>
/// Interface for credential storage (PAT tokens, etc.).
/// </summary>
public interface ICredentialService
{
    /// <summary>
    /// Store a PAT token for an organization.
    /// </summary>
    /// <param name="organization">Organization name or URL identifier</param>
    /// <param name="pat">Personal Access Token</param>
    void StorePat(string organization, string pat);

    /// <summary>
    /// Get a stored PAT token for an organization.
    /// </summary>
    /// <param name="organization">Organization name or URL identifier</param>
    /// <returns>The PAT token, or null if not found</returns>
    string? GetPat(string organization);

    /// <summary>
    /// Remove a stored PAT token.
    /// </summary>
    /// <param name="organization">Organization name or URL identifier</param>
    void RemovePat(string organization);

    /// <summary>
    /// Get all stored organization names.
    /// </summary>
    IEnumerable<string> GetStoredOrganizations();

    /// <summary>
    /// Gets all stored organizations for a specific provider.
    /// </summary>
    /// <param name="provider">"GitHub" or "AzureDevOps"</param>
    /// <returns>List of organization names (without provider prefix)</returns>
    IEnumerable<string> GetOrganizationsForProvider(string provider);

    /// <summary>
    /// Checks if a credential exists for the given key.
    /// </summary>
    /// <param name="key">The credential key (e.g., "GitHub:microsoft")</param>
    /// <returns>True if a credential exists and is non-empty</returns>
    bool HasCredential(string key);
}
