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
}
