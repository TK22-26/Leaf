namespace Leaf.Models;

/// <summary>
/// A review submitted on a pull request.
/// </summary>
public class PullRequestReviewInfo
{
    /// <summary>
    /// Login/username of the reviewer.
    /// </summary>
    public string ReviewerLogin { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the reviewer.
    /// </summary>
    public string? ReviewerDisplayName { get; set; }

    /// <summary>
    /// Avatar URL for the reviewer.
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Review verdict.
    /// </summary>
    public PullRequestReviewState State { get; set; }

    /// <summary>
    /// Review body text.
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// When the review was submitted.
    /// </summary>
    public DateTimeOffset SubmittedAt { get; set; }
}

/// <summary>
/// State of a pull request review.
/// </summary>
public enum PullRequestReviewState
{
    Pending,
    Approved,
    ChangesRequested,
    Commented,
    Dismissed
}
