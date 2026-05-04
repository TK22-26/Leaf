namespace Leaf.Models;

/// <summary>
/// Determines which view occupies the main content area (center panel).
/// </summary>
public enum ContentMode
{
    Graph,
    PullRequestDetail,
    PullRequestCreate,
    /// <summary>
    /// A <c>git bisect</c> session is active. The center column hosts
    /// the full bisect detail view (banner + verdict buttons + diff +
    /// verdict log). Branch tree stays visible; right pane hides
    /// because the bisect view uses the full content area for the
    /// diff that drives the verdict decision.
    /// </summary>
    Bisect,
}
