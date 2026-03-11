namespace Leaf.Models;

/// <summary>
/// Result of a pull request merge operation.
/// </summary>
public class PullRequestMergeResult
{
    /// <summary>
    /// Whether the merge succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if the merge failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// SHA of the merge commit (if successful).
    /// </summary>
    public string? MergedSha { get; init; }
}
