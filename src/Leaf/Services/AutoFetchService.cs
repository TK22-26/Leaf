using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using Leaf.Utils;

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
        Log.Info("AutoFetch", $"Timer started with interval {interval.TotalMinutes:F1} min");
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
        Log.Info("AutoFetch", "Timer stopped");
    }

    public async Task FetchAsync(string repoPath)
    {
        var sw = Log.StartTimer();
        try
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                Log.Warn("AutoFetch", "Network unavailable, skipping fetch");
                return;
            }

            // Fetch from all remotes
            var remotes = await _gitService.GetRemotesAsync(repoPath);

            foreach (var remote in remotes)
            {
                string? pat = null;

                if (!string.IsNullOrEmpty(remote.Url))
                {
                    // Check if host is reachable before attempting fetch
                    if (CredentialHelper.TryGetRemoteHost(remote.Url, out var host))
                    {
                        try
                        {
                            await Dns.GetHostAddressesAsync(host);
                        }
                        catch
                        {
                            // Skip this remote when host cannot be resolved
                            Log.Warn("AutoFetch", $"Skipping {remote.Name} - host {host} unreachable");
                            continue;
                        }
                    }

                    // Get credentials for this remote using URL-based lookup
                    var credentialKey = CredentialHelper.GetCredentialKeyForUrl(remote.Url);
                    if (!string.IsNullOrEmpty(credentialKey))
                    {
                        pat = _credentialService.GetPat(credentialKey);
                    }
                }

                try
                {
                    await _gitService.FetchAsync(repoPath, remote.Name, password: pat);
                }
                catch (Exception ex)
                {
                    // Log but continue with other remotes
                    Log.Error("AutoFetch", $"Failed for {remote.Name}: {ex.Message}");
                }
            }

            LastFetchTime = DateTime.Now;

            // Get updated ahead/behind counts
            var info = await _gitService.GetRepositoryInfoFastAsync(repoPath);

            Log.Perf("AutoFetch", "Fetch cycle complete", sw.ElapsedMilliseconds);

            FetchCompleted?.Invoke(this, new AutoFetchCompletedEventArgs
            {
                FetchTime = LastFetchTime.Value,
                AheadBy = info.AheadBy,
                BehindBy = info.BehindBy
            });
        }
        catch (Exception ex)
        {
            Log.Error("AutoFetch", "Fetch cycle failed", ex);
        }
    }
}
