using Leaf.Models;

namespace Leaf.Services.PullRequests;

/// <summary>
/// Internal provider interface implemented by each hosting platform adapter.
/// Methods are parameterized by (owner, repo) rather than local repo path.
/// </summary>
internal interface IPullRequestProvider
{
    PullRequestCapabilities Capabilities { get; }

    Task<List<PullRequestInfo>> ListPullRequestsAsync(string owner, string repo, PullRequestState filter);
    Task<PullRequestDetails?> GetPullRequestAsync(string owner, string repo, int number);
    Task<PullRequestInfo> CreatePullRequestAsync(string owner, string repo, string title, string body, string sourceBranch, string targetBranch, bool isDraft);
    Task<PullRequestInfo> UpdatePullRequestAsync(string owner, string repo, int number, string? title, string? body);
    Task<PullRequestMergeResult> MergePullRequestAsync(string owner, string repo, int number, MergeMethod method, string? commitTitle);
    Task ClosePullRequestAsync(string owner, string repo, int number);
    Task<List<PullRequestFileInfo>> GetPullRequestFilesAsync(string owner, string repo, int number);
    Task<List<ReviewerInfo>> SearchReviewersAsync(string owner, string repo, string searchTerm);
    Task RequestReviewersAsync(string owner, string repo, int number, IEnumerable<ReviewerInfo> reviewers);
    Task<List<PullRequestStatusCheckInfo>> GetStatusChecksAsync(string owner, string repo, int number);
    Task<PullRequestInfo?> FindPullRequestForCommitAsync(string owner, string repo, string sha);
}
