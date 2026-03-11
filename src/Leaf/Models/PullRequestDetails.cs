namespace Leaf.Models;

/// <summary>
/// Expanded pull request detail payload loaded for the detail view.
/// </summary>
public class PullRequestDetails
{
    /// <summary>
    /// Summary info (same data shown in the tree row).
    /// </summary>
    public required PullRequestInfo Summary { get; init; }

    /// <summary>
    /// PR description body (raw Markdown).
    /// </summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>
    /// Reviews submitted on this PR.
    /// </summary>
    public List<PullRequestReviewInfo> Reviews { get; init; } = [];

    /// <summary>
    /// File-level and inline comments.
    /// </summary>
    public List<PullRequestCommentInfo> Comments { get; init; } = [];

    /// <summary>
    /// Changed files in this PR.
    /// </summary>
    public List<PullRequestFileInfo> Files { get; init; } = [];

    /// <summary>
    /// CI/CD status checks for the head commit.
    /// </summary>
    public List<PullRequestStatusCheckInfo> StatusChecks { get; init; } = [];

    /// <summary>
    /// Reviewers requested on this PR.
    /// </summary>
    public List<ReviewerInfo> RequestedReviewers { get; init; } = [];

    /// <summary>
    /// Commits included in the pull request.
    /// </summary>
    public List<PullRequestCommitInfo> Commits { get; init; } = [];

    /// <summary>
    /// Update/timeline events for the pull request (provider-specific).
    /// </summary>
    public List<PullRequestUpdateInfo> Updates { get; init; } = [];

    /// <summary>
    /// Labels/tags attached to the pull request.
    /// </summary>
    public List<PullRequestLabelInfo> Labels { get; init; } = [];

    /// <summary>
    /// Work items linked to the pull request.
    /// </summary>
    public List<PullRequestWorkItemInfo> WorkItems { get; init; } = [];

    /// <summary>
    /// Whether the PR is mergeable according to the provider.
    /// </summary>
    public bool IsMergeable { get; init; }

    /// <summary>
    /// Whether the provider reports merge conflicts for this pull request.
    /// </summary>
    public bool HasConflicts { get; init; }

    /// <summary>
    /// Provider-supplied merge/conflict message.
    /// </summary>
    public string? MergeStatusMessage { get; init; }

    /// <summary>
    /// SHA of the head (source) commit.
    /// </summary>
    public string HeadSha { get; init; } = string.Empty;

    /// <summary>
    /// SHA of the base (target) commit.
    /// </summary>
    public string BaseSha { get; init; } = string.Empty;
}
