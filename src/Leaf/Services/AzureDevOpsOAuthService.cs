using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Leaf.Constants;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Handles Microsoft Entra ID Device Flow authentication for Azure DevOps.
/// </summary>
public class AzureDevOpsOAuthService
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Event raised when the device flow status changes.
    /// </summary>
    public event EventHandler<EntraDeviceFlowEventArgs>? DeviceFlowStatusChanged;

    public AzureDevOpsOAuthService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Leaf", "1.0"));
    }

    /// <summary>
    /// Starts the device flow by requesting a device code from Microsoft Entra ID.
    /// </summary>
    /// <returns>The device code response containing user code and verification URI.</returns>
    public async Task<EntraDeviceCodeResponse> StartDeviceFlowAsync()
    {
        RaiseStatusChanged(EntraDeviceFlowStatus.RequestingDeviceCode, "Requesting device code...");

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = AzureDevOpsConstants.EntraClientId,
            ["scope"] = AzureDevOpsConstants.RequiredScope
        });

        var request = new HttpRequestMessage(HttpMethod.Post, AzureDevOpsConstants.DeviceCodeEndpoint)
        {
            Content = content
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var deviceCodeResponse = JsonSerializer.Deserialize<EntraDeviceCodeResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse device code response");

        RaiseStatusChanged(EntraDeviceFlowStatus.WaitingForUserAuthorization,
            $"Enter code {deviceCodeResponse.UserCode} at {deviceCodeResponse.VerificationUri}");

        return deviceCodeResponse;
    }

    /// <summary>
    /// Polls Microsoft Entra ID for the access token after the user has authorized.
    /// </summary>
    /// <param name="deviceCode">The device code from StartDeviceFlowAsync.</param>
    /// <param name="interval">Polling interval in seconds.</param>
    /// <param name="expiresIn">Time until the device code expires in seconds.</param>
    /// <param name="cancellationToken">Cancellation token to stop polling.</param>
    /// <returns>The OAuth token result.</returns>
    public async Task<EntraOAuthResult> PollForAccessTokenAsync(
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

            RaiseStatusChanged(EntraDeviceFlowStatus.Polling,
                "Waiting for authorization...",
                (int)(expiration - DateTime.UtcNow).TotalSeconds);

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = AzureDevOpsConstants.EntraClientId,
                ["device_code"] = deviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            });

            var request = new HttpRequestMessage(HttpMethod.Post, AzureDevOpsConstants.TokenEndpoint)
            {
                Content = content
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenResponse = JsonSerializer.Deserialize<EntraTokenResponse>(json, JsonOptions);

            if (tokenResponse == null)
            {
                continue;
            }

            // Check if we got an access token
            if (!string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                RaiseStatusChanged(EntraDeviceFlowStatus.Success, "Authorization successful!");
                return new EntraOAuthResult
                {
                    Success = true,
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken,
                    TokenType = tokenResponse.TokenType,
                    Scope = tokenResponse.Scope,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
                };
            }

            // Handle errors
            var error = ParseError(tokenResponse.Error);
            switch (error)
            {
                case EntraOAuthError.AuthorizationPending:
                    // User hasn't authorized yet, keep polling
                    continue;

                case EntraOAuthError.SlowDown:
                    // Microsoft wants us to slow down, add 5 seconds to interval
                    currentInterval += 5;
                    RaiseStatusChanged(EntraDeviceFlowStatus.SlowingDown,
                        $"Slowing down, polling every {currentInterval}s");
                    continue;

                case EntraOAuthError.AuthorizationDeclined:
                    RaiseStatusChanged(EntraDeviceFlowStatus.Failed, "Authorization was denied");
                    return new EntraOAuthResult
                    {
                        Success = false,
                        Error = EntraOAuthError.AuthorizationDeclined,
                        ErrorMessage = "You denied the authorization request."
                    };

                case EntraOAuthError.ExpiredToken:
                    RaiseStatusChanged(EntraDeviceFlowStatus.Expired, "Device code expired");
                    return new EntraOAuthResult
                    {
                        Success = false,
                        Error = EntraOAuthError.ExpiredToken,
                        ErrorMessage = "The device code has expired. Please try again."
                    };

                case EntraOAuthError.BadVerificationCode:
                    RaiseStatusChanged(EntraDeviceFlowStatus.Failed, "Invalid verification code");
                    return new EntraOAuthResult
                    {
                        Success = false,
                        Error = EntraOAuthError.BadVerificationCode,
                        ErrorMessage = "The verification code is invalid."
                    };

                default:
                    RaiseStatusChanged(EntraDeviceFlowStatus.Failed, tokenResponse.ErrorDescription ?? "Unknown error");
                    return new EntraOAuthResult
                    {
                        Success = false,
                        Error = error,
                        ErrorMessage = tokenResponse.ErrorDescription ?? "An unknown error occurred."
                    };
            }
        }

        // Timeout
        RaiseStatusChanged(EntraDeviceFlowStatus.Expired, "Authorization timed out");
        return new EntraOAuthResult
        {
            Success = false,
            Error = EntraOAuthError.ExpiredToken,
            ErrorMessage = "Authorization timed out. Please try again."
        };
    }

    /// <summary>
    /// Refreshes an expired access token using a refresh token.
    /// </summary>
    /// <param name="refreshToken">The refresh token.</param>
    /// <returns>The new OAuth token result.</returns>
    public async Task<EntraOAuthResult> RefreshAccessTokenAsync(string refreshToken)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = AzureDevOpsConstants.EntraClientId,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
            ["scope"] = AzureDevOpsConstants.RequiredScope
        });

        var request = new HttpRequestMessage(HttpMethod.Post, AzureDevOpsConstants.TokenEndpoint)
        {
            Content = content
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<EntraTokenResponse>(json, JsonOptions);

            if (tokenResponse == null)
            {
                return new EntraOAuthResult
                {
                    Success = false,
                    Error = EntraOAuthError.Unknown,
                    ErrorMessage = "Failed to parse token response"
                };
            }

            if (!string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                return new EntraOAuthResult
                {
                    Success = true,
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken ?? refreshToken, // Use new refresh token if provided
                    TokenType = tokenResponse.TokenType,
                    Scope = tokenResponse.Scope,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
                };
            }

            return new EntraOAuthResult
            {
                Success = false,
                Error = ParseError(tokenResponse.Error),
                ErrorMessage = tokenResponse.ErrorDescription ?? "Failed to refresh token"
            };
        }
        catch (Exception ex)
        {
            return new EntraOAuthResult
            {
                Success = false,
                Error = EntraOAuthError.Unknown,
                ErrorMessage = $"Failed to refresh token: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Gets the connection data (user info) for the authenticated user from Azure DevOps.
    /// </summary>
    /// <param name="accessToken">The OAuth access token.</param>
    /// <param name="organization">The Azure DevOps organization name.</param>
    /// <returns>The connection data, or null if the request fails.</returns>
    public async Task<AzureDevOpsConnectionData?> GetConnectionDataAsync(string accessToken, string organization)
    {
        var url = string.Format(AzureDevOpsConstants.ConnectionDataEndpoint, organization);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<AzureDevOpsConnectionData>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Validates that an access token is still valid.
    /// </summary>
    /// <param name="accessToken">The OAuth access token to validate.</param>
    /// <param name="organization">The Azure DevOps organization name.</param>
    /// <returns>True if the token is valid, false otherwise.</returns>
    public async Task<bool> ValidateTokenAsync(string accessToken, string organization)
    {
        try
        {
            var connectionData = await GetConnectionDataAsync(accessToken, organization);
            return connectionData?.AuthenticatedUser != null;
        }
        catch
        {
            return false;
        }
    }

    private static EntraOAuthError ParseError(string? error)
    {
        return error switch
        {
            "authorization_pending" => EntraOAuthError.AuthorizationPending,
            "slow_down" => EntraOAuthError.SlowDown,
            "authorization_declined" => EntraOAuthError.AuthorizationDeclined,
            "access_denied" => EntraOAuthError.AuthorizationDeclined,
            "expired_token" => EntraOAuthError.ExpiredToken,
            "bad_verification_code" => EntraOAuthError.BadVerificationCode,
            _ => EntraOAuthError.Unknown
        };
    }

    private void RaiseStatusChanged(EntraDeviceFlowStatus status, string? message = null, int? secondsRemaining = null)
    {
        DeviceFlowStatusChanged?.Invoke(this, new EntraDeviceFlowEventArgs
        {
            Status = status,
            Message = message,
            SecondsRemaining = secondsRemaining
        });
    }
}
