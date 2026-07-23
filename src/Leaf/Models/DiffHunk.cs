namespace Leaf.Models;

/// <summary>
/// Represents a single hunk (contiguous block of changes) in a diff.
/// </summary>
public class DiffHunk
{
    /// <summary>
    /// Zero-based index of this hunk in the file's diff.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Zero-based index into the parsed file's inline Lines list where
    /// this hunk starts. The inline AvalonEdit document is built 1:1
    /// from those lines, so document line = InlineStartLineIndex + 1 —
    /// the exact scroll target for next/previous-difference navigation
    /// (#35). Unlike <see cref="OldStartLine"/>/<see cref="NewStartLine"/>
    /// (file line numbers), this indexes the combined inline view.
    /// </summary>
    public int InlineStartLineIndex { get; set; }

    /// <summary>
    /// Starting line number in the old file.
    /// </summary>
    public int OldStartLine { get; set; }

    /// <summary>
    /// Number of lines from the old file in this hunk.
    /// </summary>
    public int OldLineCount { get; set; }

    /// <summary>
    /// Starting line number in the new file.
    /// </summary>
    public int NewStartLine { get; set; }

    /// <summary>
    /// Number of lines from the new file in this hunk.
    /// </summary>
    public int NewLineCount { get; set; }

    /// <summary>
    /// Optional context string (e.g., function name) that appears after the @@ markers.
    /// </summary>
    public string? Context { get; set; }

    /// <summary>
    /// All lines in this hunk (context, added, and deleted).
    /// </summary>
    public List<DiffLine> Lines { get; set; } = [];

    /// <summary>
    /// Count of lines added in this hunk.
    /// </summary>
    public int LinesAdded => Lines.Count(l => l.Type == DiffLineType.Added);

    /// <summary>
    /// Count of lines deleted in this hunk.
    /// </summary>
    public int LinesDeleted => Lines.Count(l => l.Type == DiffLineType.Deleted);

    /// <summary>
    /// The unified diff header for this hunk (e.g., "@@ -10,5 +12,8 @@").
    /// </summary>
    public string Header => $"@@ -{OldStartLine},{OldLineCount} +{NewStartLine},{NewLineCount} @@";
}
