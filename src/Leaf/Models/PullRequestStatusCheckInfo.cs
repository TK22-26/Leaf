namespace Leaf.Models;

/// <summary>
/// A CI/CD status check on a pull request's head commit.
/// </summary>
public class PullRequestStatusCheckInfo
{
    /// <summary>
    /// Name of the check (e.g., "build", "tests").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description or summary of the check result.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Current status of the check.
    /// </summary>
    public CheckStatus Status { get; set; }

    /// <summary>
    /// URL to the check details page.
    /// </summary>
    public string? TargetUrl { get; set; }
}

/// <summary>
/// Status of a CI/CD check run.
/// </summary>
public enum CheckStatus
{
    Pending,
    Success,
    Failure,
    Error,
    Neutral,
    Cancelled
}
