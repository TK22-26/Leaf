namespace Leaf.Models;

/// <summary>
/// Represents a contiguous chunk of lines from the same commit in blame view.
/// </summary>
public class FileBlameChunk
{
    public string Sha { get; set; } = string.Empty;

    public string ShortSha => Sha.Length >= 7 ? Sha[..7] : Sha;

    public string Author { get; set; } = string.Empty;

    public DateTimeOffset Date { get; set; }

    public int LineCount { get; set; }

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
