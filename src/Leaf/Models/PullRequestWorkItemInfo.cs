namespace Leaf.Models;

/// <summary>
/// A work item linked to a pull request.
/// </summary>
public class PullRequestWorkItemInfo
{
    /// <summary>
    /// Work item identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Work item title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Work item type, when available.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Work item state, when available.
    /// </summary>
    public string? State { get; set; }

    /// <summary>
    /// Browser URL for the work item.
    /// </summary>
    public string? Url { get; set; }
}
