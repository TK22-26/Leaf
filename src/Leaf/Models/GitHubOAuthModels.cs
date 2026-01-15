using System.Text.Json.Serialization;

namespace Leaf.Models;

/// <summary>
/// Response from GitHub's device code endpoint.
/// </summary>
public class DeviceCodeResponse
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
}

/// <summary>
/// Internal response from GitHub's access token endpoint.
/// </summary>
internal class AccessTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

/// <summary>
/// Result of the OAuth token exchange.
/// </summary>
public class OAuthTokenResult
{
    public bool Success { get; set; }
    public string? AccessToken { get; set; }
    public string? TokenType { get; set; }
    public string? Scope { get; set; }
    public OAuthError Error { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// OAuth error types from GitHub.
/// </summary>
public enum OAuthError
{
    None,
    AuthorizationPending,
    SlowDown,
    AccessDenied,
    ExpiredToken,
    IncorrectClientCredentials,
    IncorrectDeviceCode,
    UnsupportedGrantType,
    DeviceFlowDisabled,
    Unknown
}

/// <summary>
/// GitHub user information from the API.
/// </summary>
public class GitHubUserInfo
{
    [JsonPropertyName("login")]
    public string Login { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

/// <summary>
/// Event arguments for device flow status updates.
/// </summary>
public class DeviceFlowEventArgs : EventArgs
{
    public DeviceFlowStatus Status { get; set; }
    public string? Message { get; set; }
    public int? SecondsRemaining { get; set; }
}

/// <summary>
/// Status of the device flow authentication process.
/// </summary>
public enum DeviceFlowStatus
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
/// Authentication method for GitHub.
/// </summary>
public enum GitHubAuthMethod
{
    OAuth,
    PersonalAccessToken
}
