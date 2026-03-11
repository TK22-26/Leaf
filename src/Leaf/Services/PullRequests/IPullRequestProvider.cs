using Leaf.Models;

namespace Leaf.Services.PullRequests;

/// <summary>
/// Internal provider interface implemented by each hosting platform adapter.
/// Methods are parameterized by (owner, repo) rather than local repo path.
/// </summary>
internal interface IPullRequestProvider
{
    PullRequestCapabilities Capabilities { get; }

    Task<List<PullRequestInfo>> ListPullRequestsAsync(string owner, string? project, string repo, PullRequestState filter);
    Task<PullRequestDetails?> GetPullRequestAsync(string owner, string? project, string repo, int number);
    Task<PullRequestInfo> CreatePullRequestAsync(string owner, string? project, string repo, string title, string body, string sourceBranch, string targetBranch, bool isDraft);
    Task<PullRequestInfo> UpdatePullRequestAsync(string owner, string? project, string repo, int number, string? title, string? body);
    Task<PullRequestMergeResult> MergePullRequestAsync(string owner, string? project, string repo, int number, MergeMethod method, string? commitTitle);
    Task ClosePullRequestAsync(string owner, string? project, string repo, int number);
    Task SubmitReviewAsync(string owner, string? project, string repo, int number, PullRequestReviewState state, string? body);
    Task AddCommentAsync(string owner, string? project, string repo, int number, string body);
    Task<List<PullRequestFileInfo>> GetPullRequestFilesAsync(string owner, string? project, string repo, int number);
    Task<List<ReviewerInfo>> SearchReviewersAsync(string owner, string? project, string repo, string searchTerm);
    Task RequestReviewersAsync(string owner, string? project, string repo, int number, IEnumerable<ReviewerInfo> reviewers);
    Task AddLabelsAsync(string owner, string? project, string repo, int number, IEnumerable<string> labels);
    Task RemoveLabelAsync(string owner, string? project, string repo, int number, string label);
    Task<List<PullRequestStatusCheckInfo>> GetStatusChecksAsync(string owner, string? project, string repo, int number);
    Task<PullRequestInfo?> FindPullRequestForCommitAsync(string owner, string? project, string repo, string sha);
}
