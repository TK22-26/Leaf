using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Leaf.Services;

/// <summary>
/// Service for interacting with Azure DevOps REST API.
/// </summary>
public class AzureDevOpsService
{
    private readonly HttpClient _httpClient;
    private readonly CredentialService _credentialService;

    public AzureDevOpsService(CredentialService credentialService)
    {
        _credentialService = credentialService;
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Fetch all repositories from an Azure DevOps organization.
    /// </summary>
    public async Task<List<AzureDevOpsRepo>> GetRepositoriesAsync(string organization)
    {
        if (string.IsNullOrEmpty(organization))
        {
            throw new InvalidOperationException("No organization configured. Please add your organization in Settings.");
        }

        // Use org-specific credential key
        var pat = _credentialService.GetPat($"AzureDevOps:{organization}");
        if (string.IsNullOrEmpty(pat))
        {
            throw new InvalidOperationException($"No PAT configured for Azure DevOps organization '{organization}'. Please add credentials in Settings.");
        }

        var url = $"https://dev.azure.com/{organization}/_apis/git/repositories?api-version=7.0";

        var request = new HttpRequestMessage(HttpMethod.Get, url);

        // Azure DevOps uses Basic auth with empty username and PAT as password
        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Failed to fetch repositories: {response.StatusCode}\n{errorContent}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<AzureDevOpsRepoResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return result?.Value ?? [];
    }
}

/// <summary>
/// Azure DevOps API response wrapper.
/// </summary>
public class AzureDevOpsRepoResponse
{
    public int Count { get; set; }
    public List<AzureDevOpsRepo> Value { get; set; } = [];
}

/// <summary>
/// Azure DevOps repository info from API.
/// </summary>
public class AzureDevOpsRepo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RemoteUrl { get; set; } = string.Empty;
    public string WebUrl { get; set; } = string.Empty;
    public AzureDevOpsProject? Project { get; set; }
    public long Size { get; set; }

    /// <summary>
    /// Display name including project.
    /// </summary>
    [JsonIgnore]
    public string DisplayName => Project != null ? $"{Project.Name}/{Name}" : Name;
}

/// <summary>
/// Azure DevOps project info.
/// </summary>
public class AzureDevOpsProject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
