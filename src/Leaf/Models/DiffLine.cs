namespace Leaf.Models;

/// <summary>
/// Represents the type of change for a diff line.
/// </summary>
public enum DiffLineType
{
    Unchanged,
    Added,
    Deleted,
    Modified,
    Imaginary // Placeholder line for alignment in side-by-side view
}

/// <summary>
/// Represents a single line in a diff view.
/// </summary>
public class DiffLine
{
    /// <summary>
    /// Line number in the old file (null for added lines).
    /// </summary>
    public int? OldLineNumber { get; set; }

    /// <summary>
    /// Line number in the new file (null for deleted lines).
    /// </summary>
    public int? NewLineNumber { get; set; }

    /// <summary>
    /// The text content of the line.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// The type of change this line represents.
    /// </summary>
    public DiffLineType Type { get; set; } = DiffLineType.Unchanged;
}
