namespace Leaf.Services;

/// <summary>
/// Service for automatic background fetching of repositories.
/// </summary>
public interface IAutoFetchService
{
    /// <summary>
    /// The time of the last successful fetch.
    /// </summary>
    DateTime? LastFetchTime { get; }

    /// <summary>
    /// Event raised when a fetch completes successfully.
    /// </summary>
    event EventHandler<AutoFetchCompletedEventArgs>? FetchCompleted;

    /// <summary>
    /// Start the auto-fetch timer.
    /// </summary>
    /// <param name="interval">Time between fetches.</param>
    /// <param name="getRepoPath">Function to get the current repository path.</param>
    void Start(TimeSpan interval, Func<string?> getRepoPath);

    /// <summary>
    /// Stop the auto-fetch timer.
    /// </summary>
    void Stop();

    /// <summary>
    /// Perform a fetch immediately.
    /// </summary>
    /// <param name="repoPath">Repository path to fetch.</param>
    Task FetchAsync(string repoPath);
}

/// <summary>
/// Event args for auto-fetch completion.
/// </summary>
public class AutoFetchCompletedEventArgs : EventArgs
{
    public DateTime FetchTime { get; init; }
    public int AheadBy { get; init; }
    public int BehindBy { get; init; }
}
