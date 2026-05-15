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

    /// <summary>
    /// Store an API key for a direct-billing AI provider (Anthropic /
    /// Google / OpenAI / OpenAI-compatible endpoint). Stored at
    /// <c>Leaf:AI:{provider}</c> in Windows Credential Manager — same
    /// store as the GitHub / Azure DevOps PATs, so the user's existing
    /// credential hygiene applies. The key never appears in
    /// <c>settings.json</c>.
    /// </summary>
    /// <param name="provider">"Claude" | "Gemini" | "OpenAI" | "OpenAiCompatible"</param>
    void SetAiApiKey(string provider, string key);

    /// <summary>Retrieve a stored AI API key, or <c>null</c> when none is set.</summary>
    string? GetAiApiKey(string provider);

    /// <summary>Delete a stored AI API key. No-op when none is set.</summary>
    void DeleteAiApiKey(string provider);

    /// <summary>Whether a non-empty AI API key is currently stored for <paramref name="provider"/>.</summary>
    bool HasAiApiKey(string provider);
}
