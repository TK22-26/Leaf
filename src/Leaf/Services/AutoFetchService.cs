using System.Net;
using System.Net.NetworkInformation;
using System.Windows.Threading;

namespace Leaf.Services;

/// <summary>
/// Service for automatic background fetching of repositories.
/// </summary>
public class AutoFetchService : IAutoFetchService
{
    private readonly IGitService _gitService;
    private readonly CredentialService _credentialService;
    private DispatcherTimer? _timer;
    private Func<string?>? _getRepoPath;

    public DateTime? LastFetchTime { get; private set; }

    public event EventHandler<AutoFetchCompletedEventArgs>? FetchCompleted;

    public AutoFetchService(IGitService gitService, CredentialService credentialService)
    {
        _gitService = gitService;
        _credentialService = credentialService;
    }

    public void Start(TimeSpan interval, Func<string?> getRepoPath)
    {
        _getRepoPath = getRepoPath;

        _timer = new DispatcherTimer
        {
            Interval = interval
        };
        _timer.Tick += async (s, e) =>
        {
            var repoPath = _getRepoPath?.Invoke();
            if (!string.IsNullOrEmpty(repoPath))
            {
                await FetchAsync(repoPath);
            }
        };
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    public async Task FetchAsync(string repoPath)
    {
        try
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return;
            }

            // Try to get credentials from stored PAT
            var remotes = await _gitService.GetRemotesAsync(repoPath);
            var originUrl = remotes.FirstOrDefault(r => r.Name == "origin")?.Url;
            string? pat = null;

            if (!string.IsNullOrEmpty(originUrl))
            {
                if (TryGetRemoteHost(originUrl, out var host))
                {
                    try
                    {
                        await Dns.GetHostAddressesAsync(host);
                    }
                    catch
                    {
                        // Skip auto-fetch when host cannot be resolved.
                        return;
                    }
                }

                try
                {
                    var hostForPat = new Uri(originUrl).Host;
                    pat = _credentialService.GetPat(hostForPat);
                }
                catch
                {
                    // Invalid URL, skip PAT
                }
            }

            await _gitService.FetchAsync(repoPath, "origin", password: pat);
            LastFetchTime = DateTime.Now;

            // Get updated ahead/behind counts
            var info = await _gitService.GetRepositoryInfoAsync(repoPath);

            FetchCompleted?.Invoke(this, new AutoFetchCompletedEventArgs
            {
                FetchTime = LastFetchTime.Value,
                AheadBy = info.AheadBy,
                BehindBy = info.BehindBy
            });
        }
        catch
        {
            // Silent failure for auto-fetch - don't disrupt the user
        }
    }

    private static bool TryGetRemoteHost(string remoteUrl, out string host)
    {
        host = string.Empty;

        if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            host = uri.Host;
            return true;
        }

        // Handle scp-like syntax: git@github.com:owner/repo.git
        var atIndex = remoteUrl.IndexOf('@');
        var colonIndex = remoteUrl.IndexOf(':');
        if (atIndex >= 0 && colonIndex > atIndex + 1)
        {
            host = remoteUrl[(atIndex + 1)..colonIndex];
            return !string.IsNullOrWhiteSpace(host);
        }

        return false;
    }
}
