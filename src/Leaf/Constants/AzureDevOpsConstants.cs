namespace Leaf.Constants;

/// <summary>
/// Constants for Azure DevOps Microsoft Entra ID authentication.
/// </summary>
public static class AzureDevOpsConstants
{
    /// <summary>
    /// The Leaf application's Client ID registered in Microsoft Entra ID.
    /// </summary>
    public const string EntraClientId = "ac33cfab-8f5c-483c-94c8-e2ac076eb7a6";

    /// <summary>
    /// The Microsoft Entra ID authority for multi-tenant work/school accounts.
    /// Note: Device code flow does NOT work with /common endpoint.
    /// </summary>
    public const string Authority = "https://login.microsoftonline.com/organizations";

    /// <summary>
    /// Endpoint to request a device code.
    /// </summary>
    public const string DeviceCodeEndpoint = $"{Authority}/oauth2/v2.0/devicecode";

    /// <summary>
    /// Endpoint to exchange device code for access token.
    /// </summary>
    public const string TokenEndpoint = $"{Authority}/oauth2/v2.0/token";

    /// <summary>
    /// URL where users enter their device code.
    /// </summary>
    public const string VerificationUri = "https://microsoft.com/devicelogin";

    /// <summary>
    /// Azure DevOps resource ID (application ID) for requesting tokens.
    /// </summary>
    public const string AzureDevOpsResourceId = "499b84ac-1321-427f-aa17-267ca6975798";

    /// <summary>
    /// The OAuth scope to request for Azure DevOps access.
    /// Using .default to get all permissions the app is registered for.
    /// </summary>
    public const string RequiredScope = $"{AzureDevOpsResourceId}/.default";

    /// <summary>
    /// Azure DevOps API base URL.
    /// </summary>
    public const string ApiBaseUrl = "https://dev.azure.com";

    /// <summary>
    /// Azure DevOps user profile endpoint format.
    /// </summary>
    public const string ConnectionDataEndpoint = "https://dev.azure.com/{0}/_apis/connectionData";
}
