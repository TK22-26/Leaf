namespace Leaf.Models;

/// <summary>
/// A file changed in a pull request.
/// </summary>
public class PullRequestFileInfo
{
    /// <summary>
    /// Full file path relative to the repository root.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// File name only (derived from <see cref="Path"/>).
    /// </summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>
    /// How this file was changed.
    /// </summary>
    public PullRequestFileStatus Status { get; set; }

    /// <summary>
    /// Lines added in this file.
    /// </summary>
    public int Additions { get; set; }

    /// <summary>
    /// Lines deleted in this file.
    /// </summary>
    public int Deletions { get; set; }

    /// <summary>
    /// Unified diff patch content (may be null for binary files).
    /// </summary>
    public string? PatchContent { get; set; }
}

/// <summary>
/// How a file was changed in a pull request.
/// </summary>
public enum PullRequestFileStatus
{
    Added,
    Modified,
    Deleted,
    Renamed,
    Copied
}
