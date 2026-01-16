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
            // Try to get credentials from stored PAT
            var remotes = await _gitService.GetRemotesAsync(repoPath);
            var originUrl = remotes.FirstOrDefault(r => r.Name == "origin")?.Url;
            string? pat = null;

            if (!string.IsNullOrEmpty(originUrl))
            {
                try
                {
                    var host = new Uri(originUrl).Host;
                    pat = _credentialService.GetPat(host);
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
}
