using CommunityToolkit.Mvvm.ComponentModel;

namespace Leaf.Models;

/// <summary>
/// Represents a Git stash entry.
/// </summary>
public partial class StashInfo : ObservableObject
{
    /// <summary>
    /// The commit SHA of the stash.
    /// </summary>
    public string Sha { get; set; } = string.Empty;

    /// <summary>
    /// True if this stash is currently selected.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Short SHA (first 7 characters).
    /// </summary>
    public string ShortSha => Sha.Length >= 7 ? Sha[..7] : Sha;

    /// <summary>
    /// The stash index (0 = most recent).
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// The stash reference (e.g., "stash@{0}").
    /// </summary>
    public string Reference => $"stash@{{{Index}}}";

    /// <summary>
    /// The stash message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Short message (first line only).
    /// </summary>
    public string MessageShort
    {
        get
        {
            if (string.IsNullOrEmpty(Message))
                return string.Empty;
            var newlineIndex = Message.IndexOf('\n');
            return newlineIndex > 0 ? Message[..newlineIndex] : Message;
        }
    }

    /// <summary>
    /// The branch the stash was created on.
    /// </summary>
    public string BranchName { get; set; } = string.Empty;

    /// <summary>
    /// Author of the stash.
    /// </summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>
    /// When the stash was created.
    /// </summary>
    public DateTimeOffset Date { get; set; }

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
}
