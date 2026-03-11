namespace Leaf.Models;

/// <summary>
/// Feature flags indicating what a pull request provider supports.
/// </summary>
[Flags]
public enum PullRequestCapabilities
{
    None = 0,
    DraftPullRequests = 1,
    SquashMerge = 2,
    RebaseMerge = 4,
    MergeCommit = 8,
    StatusChecks = 16,
    Reviews = 32,
    TeamReviewers = 64,
    AutoComplete = 128,
    RequiredReviewers = 256,
    Labels = 512,
    Assignees = 1024
}
