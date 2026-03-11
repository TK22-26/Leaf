namespace Leaf.Models;

/// <summary>
/// A comment on a pull request (file-level or inline).
/// </summary>
public class PullRequestCommentInfo
{
    /// <summary>
    /// Provider-specific comment ID.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Login/username of the comment author.
    /// </summary>
    public string AuthorLogin { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the comment author.
    /// </summary>
    public string? AuthorDisplayName { get; set; }

    /// <summary>
    /// Avatar URL for the comment author.
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Comment body text.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// When the comment was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the comment was last updated.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// File path this comment is attached to (null for PR-level comments).
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Line number in the diff (null for PR-level comments).
    /// </summary>
    public int? Line { get; set; }

    /// <summary>
    /// Whether this comment thread is resolved.
    /// </summary>
    public bool IsResolved { get; set; }

    /// <summary>
    /// Whether this entry is provider-generated activity rather than a user comment.
    /// </summary>
    public bool IsSystemActivity { get; set; }
}
