namespace Leaf.Models;

/// <summary>
/// A commit included in a pull request.
/// </summary>
public class PullRequestCommitInfo
{
    /// <summary>
    /// Full commit SHA.
    /// </summary>
    public string Sha { get; set; } = string.Empty;

    /// <summary>
    /// Abbreviated commit SHA for UI display.
    /// </summary>
    public string ShortSha => Sha.Length > 8 ? Sha[..8] : Sha;

    /// <summary>
    /// First-line commit message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Optional extended commit description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Commit author display name.
    /// </summary>
    public string AuthorDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Commit author email/login when available.
    /// </summary>
    public string? AuthorIdentity { get; set; }

    /// <summary>
    /// Commit timestamp.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Provider web URL for the commit when available.
    /// </summary>
    public string? Url { get; set; }
}
