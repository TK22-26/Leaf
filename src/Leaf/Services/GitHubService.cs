using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Leaf.Services;

/// <summary>
/// Service for interacting with GitHub REST API.
/// </summary>
public class GitHubService
{
    private readonly HttpClient _httpClient;
    private readonly CredentialService _credentialService;

    public GitHubService(CredentialService credentialService)
    {
        _credentialService = credentialService;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Leaf", "1.0"));
    }

    /// <summary>
    /// Fetch all repositories accessible to the authenticated user.
    /// </summary>
    /// <param name="owner">Optional: specific owner/org for credential lookup. If null, uses the first configured GitHub credential.</param>
    public async Task<List<GitHubRepo>> GetRepositoriesAsync(string? owner = null)
    {
        string? pat = null;

        if (!string.IsNullOrEmpty(owner))
        {
            // Use specific owner's credential
            pat = _credentialService.GetPat($"GitHub:{owner}");
        }
        else
        {
            // Use the first configured GitHub credential
            var orgs = _credentialService.GetOrganizationsForProvider("GitHub").ToList();
            if (orgs.Count > 0)
            {
                pat = _credentialService.GetPat($"GitHub:{orgs[0]}");
            }
        }

        if (string.IsNullOrEmpty(pat))
        {
            throw new InvalidOperationException("No PAT configured. Please add your GitHub PAT in Settings.");
        }

        var allRepos = new List<GitHubRepo>();
        var page = 1;
        const int perPage = 100;

        while (true)
        {
            var url = $"https://api.github.com/user/repos?per_page={perPage}&page={page}&sort=updated&affiliation=owner,collaborator,organization_member";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pat);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Failed to fetch repositories: {response.StatusCode}\n{errorContent}");
            }

            var json = await response.Content.ReadAsStringAsync();
            var repos = JsonSerializer.Deserialize<List<GitHubRepo>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? [];

            if (repos.Count == 0)
                break;

            allRepos.AddRange(repos);

            // If we got less than perPage, we've reached the end
            if (repos.Count < perPage)
                break;

            page++;

            // Safety limit to prevent infinite loops
            if (page > 50)
                break;
        }

        return allRepos;
    }
}

/// <summary>
/// GitHub repository info from API.
/// </summary>
public class GitHubRepo
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("clone_url")]
    public string CloneUrl { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("private")]
    public bool IsPrivate { get; set; }

    public GitHubOwner? Owner { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Display name for the repository list.
    /// </summary>
    [JsonIgnore]
    public string DisplayName => FullName;

    /// <summary>
    /// URL for cloning (same as CloneUrl for consistency with Azure DevOps).
    /// </summary>
    [JsonIgnore]
    public string RemoteUrl => CloneUrl;
}

/// <summary>
/// GitHub repository owner info.
/// </summary>
public class GitHubOwner
{
    public long Id { get; set; }

    public string Login { get; set; } = string.Empty;

    [JsonPropertyName("avatar_url")]
    public string AvatarUrl { get; set; } = string.Empty;
}
