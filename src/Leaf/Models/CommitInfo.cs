using CommunityToolkit.Mvvm.ComponentModel;

namespace Leaf.Models;

/// <summary>
/// POCO representing a Git commit, mapped from LibGit2Sharp.Commit.
/// Thread-safe and serializable - no LibGit2Sharp types exposed.
/// </summary>
public partial class CommitInfo : ObservableObject
{
    public string Sha { get; set; } = string.Empty;

    public string ShortSha => Sha.Length >= 7 ? Sha[..7] : Sha;

    public string Message { get; set; } = string.Empty;

    public string MessageShort { get; set; } = string.Empty;

    /// <summary>
    /// The commit description (full body after the first line, if any).
    /// </summary>
    public string Description
    {
        get
        {
            if (string.IsNullOrEmpty(Message))
                return string.Empty;

            // Find the first newline
            var firstNewline = Message.IndexOf('\n');
            if (firstNewline < 0)
                return string.Empty;

            // Skip the first line and any blank lines after it, return full body
            return Message[(firstNewline + 1)..].TrimStart('\r', '\n').Trim();
        }
    }

    public string Author { get; set; } = string.Empty;

    public string AuthorEmail { get; set; } = string.Empty;

    public string AvatarKey
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AuthorEmail))
            {
                return AuthorEmail.Trim();
            }

            if (!string.IsNullOrWhiteSpace(Author))
            {
                return Author.Trim();
            }

            return Sha;
        }
    }

    public DateTimeOffset Date { get; set; }

    public List<string> ParentShas { get; set; } = [];

    public bool IsMerge => ParentShas.Count > 1;

    /// <summary>
    /// Branch names that point to this commit (for branch labels).
    /// </summary>
    public List<string> BranchNames { get; set; } = [];

    /// <summary>
    /// Branch labels with local/remote info for display on graph.
    /// </summary>
    public List<BranchLabel> BranchLabels { get; set; } = [];

    /// <summary>
    /// Tag names that point to this commit.
    /// </summary>
    public List<string> TagNames { get; set; } = [];

    /// <summary>
    /// True if this commit is the current HEAD.
    /// </summary>
    public bool IsHead { get; set; }

    /// <summary>
    /// Friendly date string for display.
    /// </summary>
    public string DateDisplay
    {
        get
        {
            var now = DateTimeOffset.Now;
            var diff = now - Date;

            if (diff.TotalMinutes < 1)
                return "Just now";
            if (diff.TotalHours < 1)
                return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalDays < 1)
                return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays}d ago";
            if (Date.Year == now.Year)
                return Date.ToString("MMM d");
            return Date.ToString("MMM d, yyyy");
        }
    }

    /// <summary>
    /// True if this commit is currently selected.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHighlighted))]
    private bool _isSelected;

    /// <summary>
    /// True if this commit matches the current search (when search is active).
    /// False when no search is active or doesn't match.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHighlighted))]
    private bool _isSearchHighlighted;

    /// <summary>
    /// True if this commit should be dimmed (search active but doesn't match).
    /// </summary>
    [ObservableProperty]
    private bool _isDimmed;

    /// <summary>
    /// True if this commit should have highlighted background (selected OR search match).
    /// </summary>
    public bool IsHighlighted => IsSelected || IsSearchHighlighted;
}
