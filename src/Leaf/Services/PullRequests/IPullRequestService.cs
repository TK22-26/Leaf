using Leaf.Models;

namespace Leaf.Services.PullRequests;

/// <summary>
/// Facade interface for pull request operations.
/// All methods are scoped by repository path; the router resolves the correct provider.
/// </summary>
public interface IPullRequestService
{
    /// <summary>
    /// Lists pull requests for a repository.
    /// </summary>
    Task<List<PullRequestInfo>> ListPullRequestsAsync(string repoPath, PullRequestState filter = PullRequestState.Open);

    /// <summary>
    /// Gets full details for a single pull request.
    /// </summary>
    Task<PullRequestDetails?> GetPullRequestAsync(string repoPath, int number);

    /// <summary>
    /// Creates a new pull request.
    /// </summary>
    Task<PullRequestInfo> CreatePullRequestAsync(string repoPath, string title, string body, string sourceBranch, string targetBranch, bool isDraft = false);

    /// <summary>
    /// Updates an existing pull request's title and/or body.
    /// </summary>
    Task<PullRequestInfo> UpdatePullRequestAsync(string repoPath, int number, string? title = null, string? body = null);

    /// <summary>
    /// Merges a pull request.
    /// </summary>
    Task<PullRequestMergeResult> MergePullRequestAsync(string repoPath, int number, MergeMethod method = MergeMethod.Merge, string? commitTitle = null);

    /// <summary>
    /// Closes a pull request without merging.
    /// </summary>
    Task ClosePullRequestAsync(string repoPath, int number);

    /// <summary>
    /// Gets the changed files for a pull request.
    /// </summary>
    Task<List<PullRequestFileInfo>> GetPullRequestFilesAsync(string repoPath, int number);

    /// <summary>
    /// Searches for reviewers (users/teams/groups) matching a search term.
    /// </summary>
    Task<List<ReviewerInfo>> SearchReviewersAsync(string repoPath, string searchTerm);

    /// <summary>
    /// Requests reviewers on a pull request.
    /// </summary>
    Task RequestReviewersAsync(string repoPath, int number, IEnumerable<ReviewerInfo> reviewers);

    /// <summary>
    /// Gets CI/CD status checks for a pull request's head commit.
    /// </summary>
    Task<List<PullRequestStatusCheckInfo>> GetStatusChecksAsync(string repoPath, int number);

    /// <summary>
    /// Finds a pull request associated with a specific commit SHA.
    /// </summary>
    Task<PullRequestInfo?> FindPullRequestForCommitAsync(string repoPath, string sha);

    /// <summary>
    /// Gets the capabilities supported by the provider for this repository.
    /// Returns <see cref="PullRequestCapabilities.None"/> if not yet resolved.
    /// </summary>
    PullRequestCapabilities GetCapabilities(string repoPath);

    /// <summary>
    /// Returns true if the repository's provider has been resolved and is supported.
    /// </summary>
    bool IsSupported(string repoPath);

    /// <summary>
    /// Warm-up: resolves the provider for a repository path so that
    /// <see cref="IsSupported"/> and <see cref="GetCapabilities"/> return correct values.
    /// </summary>
    Task TryResolveAsync(string repoPath);

    /// <summary>
    /// Returns the web URL for creating a new pull request on the provider's site,
    /// or null if not supported. Requires prior <see cref="TryResolveAsync"/> call.
    /// </summary>
    string? GetCreatePullRequestUrl(string repoPath, string sourceBranch);
}
