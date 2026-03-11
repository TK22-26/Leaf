namespace Leaf.Models;

/// <summary>
/// A provider-specific update/timeline entry for a pull request.
/// </summary>
public class PullRequestUpdateInfo
{
    /// <summary>
    /// Provider-specific update/iteration identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Short update headline.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Optional secondary description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Display name of the actor responsible for the update.
    /// </summary>
    public string? AuthorDisplayName { get; set; }

    /// <summary>
    /// When the update occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Base/common commit associated with this update when available.
    /// </summary>
    public string? BaseCommitSha { get; set; }

    /// <summary>
    /// Commits associated with this update.
    /// </summary>
    public List<PullRequestCommitInfo> Commits { get; set; } = [];
}
