using CommunityToolkit.Mvvm.ComponentModel;

namespace Leaf.Models;

/// <summary>
/// Summary-level pull request data displayed in the tree view.
/// </summary>
public partial class PullRequestInfo : ObservableObject
{
    /// <summary>
    /// Whether this PR is selected in the tree view.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Expand state for tree views (silences TreeView binding warnings).
    /// </summary>
    public bool IsExpanded { get; set; }

    /// <summary>
    /// PRs are leaf items and never "current" (silences TreeView binding warnings).
    /// </summary>
    public bool IsCurrent => false;

    /// <summary>
    /// PR number (e.g., #42).
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// PR title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Login/username of the PR author.
    /// </summary>
    public string AuthorLogin { get; set; } = string.Empty;

    /// <summary>
    /// Avatar URL for the PR author.
    /// </summary>
    public string? AuthorAvatarUrl { get; set; }

    /// <summary>
    /// Source (head) branch name.
    /// </summary>
    public string SourceBranch { get; set; } = string.Empty;

    /// <summary>
    /// Target (base) branch name.
    /// </summary>
    public string TargetBranch { get; set; } = string.Empty;

    /// <summary>
    /// Current state of the pull request.
    /// </summary>
    public PullRequestState State { get; set; }

    /// <summary>
    /// Whether this is a draft pull request.
    /// </summary>
    public bool IsDraft { get; set; }

    /// <summary>
    /// When the PR was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the PR was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Web URL for "Open in browser".
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Number of comments on the PR.
    /// </summary>
    public int CommentCount { get; set; }

    /// <summary>
    /// Number of changed files.
    /// </summary>
    public int ChangedFilesCount { get; set; }

    /// <summary>
    /// Total lines added.
    /// </summary>
    public int Additions { get; set; }

    /// <summary>
    /// Total lines deleted.
    /// </summary>
    public int Deletions { get; set; }
}
