using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Leaf.Services;

/// <summary>
/// Service for checking GitHub releases for updates.
/// Uses GitHub CLI (gh) for authentication with private repositories,
/// falls back to direct HTTP for public repositories.
/// </summary>
public class UpdateService
{
    private const string GitHubOwner = "TK22-26";
    private const string GitHubRepo = "Leaf";
    private const string ReleasesApiEndpoint = $"repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
    private const string ReleasesApiUrl = $"https://api.github.com/{ReleasesApiEndpoint}";
    private const string ReleasesPageUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases";
    private const string NewIssueUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/issues/new";

    private static readonly Lazy<HttpClient> HttpClientLazy = new(() =>
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Leaf", CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    });

    public UpdateService()
    {
    }

    /// <summary>
    /// Gets the current application version.
    /// </summary>
    public static Version CurrentVersion
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version ?? new Version(1, 0, 0);
        }
    }

    /// <summary>
    /// Gets the current version as a display string.
    /// </summary>
    public static string CurrentVersionString => $"v{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    /// <summary>
    /// Checks for updates on GitHub releases.
    /// Tries GitHub CLI first (for private repos), falls back to HTTP (for public repos).
    /// </summary>
    /// <returns>Update info if available, null if up to date or error</returns>
    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            // Try gh CLI first (works with private repos)
            var json = await FetchReleaseJsonViaGhAsync();

            // Fall back to HTTP if gh failed (works when repo is public)
            if (string.IsNullOrEmpty(json))
            {
                json = await FetchReleaseJsonViaHttpAsync();
            }

            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            var release = JsonSerializer.Deserialize<GitHubRelease>(json);

            if (release == null || string.IsNullOrEmpty(release.TagName))
            {
                return null;
            }

            var latestVersion = ParseVersion(release.TagName);
            if (latestVersion == null)
            {
                return null;
            }

            if (latestVersion > CurrentVersion)
            {
                return new UpdateInfo
                {
                    CurrentVersion = CurrentVersion,
                    LatestVersion = latestVersion,
                    TagName = release.TagName,
                    ReleaseName = release.Name ?? release.TagName,
                    ReleaseNotes = release.Body ?? "",
                    ReleaseUrl = release.HtmlUrl ?? ReleasesPageUrl,
                    PublishedAt = release.PublishedAt
                };
            }

            return null; // Up to date
        }
        catch
        {
            return null; // Network error or parse error
        }
    }

    /// <summary>
    /// Fetch release JSON using GitHub CLI (authenticated, works with private repos).
    /// </summary>
    private static async Task<string?> FetchReleaseJsonViaGhAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = $"api {ReleasesApiEndpoint}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();

            // Wait with timeout (10 seconds for API call)
            var completed = await Task.WhenAny(
                process.WaitForExitAsync(),
                Task.Delay(TimeSpan.FromSeconds(10)));

            if (!process.HasExited)
            {
                process.Kill();
                return null;
            }

            if (process.ExitCode != 0)
            {
                return null;
            }

            return await outputTask;
        }
        catch
        {
            return null; // gh CLI not available
        }
    }

    /// <summary>
    /// Fetch release JSON using direct HTTP (works with public repos only).
    /// </summary>
    private static async Task<string?> FetchReleaseJsonViaHttpAsync()
    {
        try
        {
            var response = await HttpClientLazy.Value.GetAsync(ReleasesApiUrl);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Opens the releases page in the default browser.
    /// </summary>
    public static void OpenReleasesPage()
    {
        OpenUrl(ReleasesPageUrl);
    }

    /// <summary>
    /// Opens the new issue page in the default browser.
    /// </summary>
    public static void OpenNewIssuePage()
    {
        // Pre-fill some issue template info
        var body = Uri.EscapeDataString(
            $"## Description\n\n[Describe the issue here]\n\n" +
            $"## Steps to Reproduce\n\n1. \n2. \n3. \n\n" +
            $"## Expected Behavior\n\n[What you expected to happen]\n\n" +
            $"## Actual Behavior\n\n[What actually happened]\n\n" +
            $"## Environment\n\n- Leaf Version: {CurrentVersionString}\n- OS: Windows {Environment.OSVersion.Version}");

        var url = $"{NewIssueUrl}?body={body}";
        OpenUrl(url);
    }

    /// <summary>
    /// Opens the download page for the latest release.
    /// </summary>
    public static void OpenDownloadPage(string? releaseUrl = null)
    {
        OpenUrl(releaseUrl ?? ReleasesPageUrl);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore errors opening browser
        }
    }

    private static Version? ParseVersion(string tagName)
    {
        // Remove 'v' prefix if present
        var versionString = tagName.TrimStart('v', 'V');

        // Try to parse as version
        if (Version.TryParse(versionString, out var version))
        {
            return version;
        }

        // Try parsing with only major.minor
        var parts = versionString.Split('.');
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], out var major) &&
            int.TryParse(parts[1], out var minor))
        {
            var build = parts.Length > 2 && int.TryParse(parts[2], out var b) ? b : 0;
            return new Version(major, minor, build);
        }

        return null;
    }
}

/// <summary>
/// Information about an available update.
/// </summary>
public class UpdateInfo
{
    public Version CurrentVersion { get; set; } = new(1, 0, 0);
    public Version LatestVersion { get; set; } = new(1, 0, 0);
    public string TagName { get; set; } = "";
    public string ReleaseName { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public string ReleaseUrl { get; set; } = "";
    public DateTime? PublishedAt { get; set; }
}

/// <summary>
/// GitHub release info from API.
/// </summary>
internal class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("published_at")]
    public DateTime? PublishedAt { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }
}
