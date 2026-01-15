namespace Leaf.Constants;

/// <summary>
/// GitHub API and OAuth constants.
/// </summary>
public static class GitHubConstants
{
    // OAuth App Configuration
    // NOTE: This is a public client ID for the device flow (no secret required)
    public const string OAuthClientId = "Ov23liVu0oppp7vv05Yl";

    // OAuth Endpoints
    public const string DeviceCodeEndpoint = "https://github.com/login/device/code";
    public const string AccessTokenEndpoint = "https://github.com/login/oauth/access_token";
    public const string AuthorizeEndpoint = "https://github.com/login/device";

    // API Endpoints
    public const string ApiBaseUrl = "https://api.github.com";
    public const string UserEndpoint = $"{ApiBaseUrl}/user";

    // Scopes - repo for repository access, read:user for user profile
    public const string RequiredScopes = "repo read:user";
}
