using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Leaf.Constants;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Handles GitHub OAuth Device Flow authentication.
/// </summary>
public class GitHubOAuthService
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Event raised when the device flow status changes.
    /// </summary>
    public event EventHandler<DeviceFlowEventArgs>? DeviceFlowStatusChanged;

    public GitHubOAuthService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Leaf", "1.0"));
    }

    /// <summary>
    /// Starts the device flow by requesting a device code from GitHub.
    /// </summary>
    /// <returns>The device code response containing user code and verification URI.</returns>
    public async Task<DeviceCodeResponse> StartDeviceFlowAsync()
    {
        RaiseStatusChanged(DeviceFlowStatus.RequestingDeviceCode, "Requesting device code...");

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = GitHubConstants.OAuthClientId,
            ["scope"] = GitHubConstants.RequiredScopes
        });

        var request = new HttpRequestMessage(HttpMethod.Post, GitHubConstants.DeviceCodeEndpoint)
        {
            Content = content
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var deviceCodeResponse = JsonSerializer.Deserialize<DeviceCodeResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse device code response");

        RaiseStatusChanged(DeviceFlowStatus.WaitingForUserAuthorization,
            $"Enter code {deviceCodeResponse.UserCode} at {deviceCodeResponse.VerificationUri}");

        return deviceCodeResponse;
    }

    /// <summary>
    /// Polls GitHub for the access token after the user has authorized.
    /// </summary>
    /// <param name="deviceCode">The device code from StartDeviceFlowAsync.</param>
    /// <param name="interval">Polling interval in seconds.</param>
    /// <param name="expiresIn">Time until the device code expires in seconds.</param>
    /// <param name="cancellationToken">Cancellation token to stop polling.</param>
    /// <returns>The OAuth token result.</returns>
    public async Task<OAuthTokenResult> PollForAccessTokenAsync(
        string deviceCode,
        int interval,
        int expiresIn,
        CancellationToken cancellationToken)
    {
        var currentInterval = interval;
        var startTime = DateTime.UtcNow;
        var expiration = startTime.AddSeconds(expiresIn);

        while (DateTime.UtcNow < expiration)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Wait for the polling interval
            await Task.Delay(currentInterval * 1000, cancellationToken);

            RaiseStatusChanged(DeviceFlowStatus.Polling,
                "Waiting for authorization...",
                (int)(expiration - DateTime.UtcNow).TotalSeconds);

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = GitHubConstants.OAuthClientId,
                ["device_code"] = deviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            });

            var request = new HttpRequestMessage(HttpMethod.Post, GitHubConstants.AccessTokenEndpoint)
            {
                Content = content
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenResponse = JsonSerializer.Deserialize<AccessTokenResponse>(json, JsonOptions);

            if (tokenResponse == null)
            {
                continue;
            }

            // Check if we got an access token
            if (!string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                RaiseStatusChanged(DeviceFlowStatus.Success, "Authorization successful!");
                return new OAuthTokenResult
                {
                    Success = true,
                    AccessToken = tokenResponse.AccessToken,
                    TokenType = tokenResponse.TokenType,
                    Scope = tokenResponse.Scope
                };
            }

            // Handle errors
            var error = ParseError(tokenResponse.Error);
            switch (error)
            {
                case OAuthError.AuthorizationPending:
                    // User hasn't authorized yet, keep polling
                    continue;

                case OAuthError.SlowDown:
                    // GitHub wants us to slow down, add 5 seconds to interval
                    currentInterval += 5;
                    RaiseStatusChanged(DeviceFlowStatus.SlowingDown,
                        $"Slowing down, polling every {currentInterval}s");
                    continue;

                case OAuthError.AccessDenied:
                    RaiseStatusChanged(DeviceFlowStatus.Failed, "Authorization was denied");
                    return new OAuthTokenResult
                    {
                        Success = false,
                        Error = OAuthError.AccessDenied,
                        ErrorMessage = "You denied the authorization request."
                    };

                case OAuthError.ExpiredToken:
                    RaiseStatusChanged(DeviceFlowStatus.Expired, "Device code expired");
                    return new OAuthTokenResult
                    {
                        Success = false,
                        Error = OAuthError.ExpiredToken,
                        ErrorMessage = "The device code has expired. Please try again."
                    };

                default:
                    RaiseStatusChanged(DeviceFlowStatus.Failed, tokenResponse.ErrorDescription ?? "Unknown error");
                    return new OAuthTokenResult
                    {
                        Success = false,
                        Error = error,
                        ErrorMessage = tokenResponse.ErrorDescription ?? "An unknown error occurred."
                    };
            }
        }

        // Timeout
        RaiseStatusChanged(DeviceFlowStatus.Expired, "Authorization timed out");
        return new OAuthTokenResult
        {
            Success = false,
            Error = OAuthError.ExpiredToken,
            ErrorMessage = "Authorization timed out. Please try again."
        };
    }

    /// <summary>
    /// Gets the user information for the authenticated user.
    /// </summary>
    /// <param name="accessToken">The OAuth access token.</param>
    /// <returns>The GitHub user information, or null if the request fails.</returns>
    public async Task<GitHubUserInfo?> GetUserInfoAsync(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, GitHubConstants.UserEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<GitHubUserInfo>(json, JsonOptions);
    }

    /// <summary>
    /// Validates that an access token is still valid.
    /// </summary>
    /// <param name="accessToken">The OAuth access token to validate.</param>
    /// <returns>True if the token is valid, false otherwise.</returns>
    public async Task<bool> ValidateTokenAsync(string accessToken)
    {
        try
        {
            var userInfo = await GetUserInfoAsync(accessToken);
            return userInfo != null;
        }
        catch
        {
            return false;
        }
    }

    private static OAuthError ParseError(string? error)
    {
        return error switch
        {
            "authorization_pending" => OAuthError.AuthorizationPending,
            "slow_down" => OAuthError.SlowDown,
            "access_denied" => OAuthError.AccessDenied,
            "expired_token" => OAuthError.ExpiredToken,
            "incorrect_client_credentials" => OAuthError.IncorrectClientCredentials,
            "incorrect_device_code" => OAuthError.IncorrectDeviceCode,
            "unsupported_grant_type" => OAuthError.UnsupportedGrantType,
            "device_flow_disabled" => OAuthError.DeviceFlowDisabled,
            _ => OAuthError.Unknown
        };
    }

    private void RaiseStatusChanged(DeviceFlowStatus status, string? message = null, int? secondsRemaining = null)
    {
        DeviceFlowStatusChanged?.Invoke(this, new DeviceFlowEventArgs
        {
            Status = status,
            Message = message,
            SecondsRemaining = secondsRemaining
        });
    }
}
