namespace Leaf.Models;

/// <summary>
/// Represents the result of comparing two versions of a file.
/// </summary>
public class FileDiffResult
{
    /// <summary>
    /// The file name (without path).
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// The full file path relative to repository root.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// All diff lines for inline display.
    /// </summary>
    public List<DiffLine> Lines { get; set; } = [];

    /// <summary>
    /// The combined inline content for display.
    /// </summary>
    public string InlineContent { get; set; } = string.Empty;

    /// <summary>
    /// The old file content as a single string.
    /// </summary>
    public string OldContent { get; set; } = string.Empty;

    /// <summary>
    /// The new file content as a single string.
    /// </summary>
    public string NewContent { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is a binary file (cannot show diff).
    /// </summary>
    public bool IsBinary { get; set; }

    /// <summary>
    /// Number of lines added.
    /// </summary>
    public int LinesAddedCount { get; set; }

    /// <summary>
    /// Number of lines deleted.
    /// </summary>
    public int LinesDeletedCount { get; set; }
}
