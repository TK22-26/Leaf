using System.Text.Json.Serialization;

namespace Leaf.Models;

/// <summary>
/// Response from Microsoft Entra ID's device code endpoint.
/// </summary>
public class EntraDeviceCodeResponse
{
    [JsonPropertyName("device_code")]
    public string DeviceCode { get; set; } = string.Empty;

    [JsonPropertyName("user_code")]
    public string UserCode { get; set; } = string.Empty;

    [JsonPropertyName("verification_uri")]
    public string VerificationUri { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; } = 5;

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// Internal response from Microsoft Entra ID's token endpoint.
/// </summary>
internal class EntraTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

/// <summary>
/// Result of the Entra ID OAuth token exchange.
/// </summary>
public class EntraOAuthResult
{
    public bool Success { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public string? TokenType { get; set; }
    public string? Scope { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public EntraOAuthError Error { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// OAuth error types from Microsoft Entra ID.
/// </summary>
public enum EntraOAuthError
{
    None,
    AuthorizationPending,
    AuthorizationDeclined,
    ExpiredToken,
    BadVerificationCode,
    SlowDown,
    Unknown
}

/// <summary>
/// Azure DevOps connection data response (user info).
/// </summary>
public class AzureDevOpsConnectionData
{
    [JsonPropertyName("authenticatedUser")]
    public AzureDevOpsAuthenticatedUser? AuthenticatedUser { get; set; }
}

/// <summary>
/// Azure DevOps authenticated user info.
/// </summary>
public class AzureDevOpsAuthenticatedUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("descriptor")]
    public string? Descriptor { get; set; }

    [JsonPropertyName("subjectDescriptor")]
    public string? SubjectDescriptor { get; set; }

    [JsonPropertyName("providerDisplayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("customDisplayName")]
    public string? CustomDisplayName { get; set; }
}

/// <summary>
/// Event arguments for Entra ID device flow status updates.
/// </summary>
public class EntraDeviceFlowEventArgs : EventArgs
{
    public EntraDeviceFlowStatus Status { get; set; }
    public string? Message { get; set; }
    public int? SecondsRemaining { get; set; }
}

/// <summary>
/// Status of the Entra ID device flow authentication process.
/// </summary>
public enum EntraDeviceFlowStatus
{
    RequestingDeviceCode,
    WaitingForUserAuthorization,
    Polling,
    SlowingDown,
    Success,
    Failed,
    Cancelled,
    Expired
}

/// <summary>
/// Authentication method for Azure DevOps.
/// </summary>
public enum AzureDevOpsAuthMethod
{
    OAuth,
    PersonalAccessToken
}
