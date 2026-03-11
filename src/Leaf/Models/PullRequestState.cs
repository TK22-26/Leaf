namespace Leaf.Models;

/// <summary>
/// State of a pull request. <see cref="All"/> is used as a filter value only.
/// </summary>
public enum PullRequestState
{
    Open,
    Closed,
    Merged,
    Draft,
    All
}
