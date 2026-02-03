using System.Text;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for parsing diffs into hunks and generating patch content.
/// </summary>
public class HunkService : IHunkService
{
    /// <inheritdoc />
    public IReadOnlyList<DiffHunk> ParseHunks(FileDiffResult diffResult, int contextLines = 3)
    {
        if (diffResult.Lines.Count == 0)
            return [];

        // Find all change indices (lines that are added, deleted, or modified)
        var changeIndices = new List<int>();
        for (int i = 0; i < diffResult.Lines.Count; i++)
        {
            var line = diffResult.Lines[i];
            if (line.Type != DiffLineType.Unchanged && line.Type != DiffLineType.Imaginary)
            {
                changeIndices.Add(i);
            }
        }

        if (changeIndices.Count == 0)
            return [];

        // Group changes into hunks based on proximity
        var hunkRanges = GroupChangesIntoHunks(changeIndices, contextLines, diffResult.Lines.Count);

        // Build hunks from ranges
        var hunks = new List<DiffHunk>();
        int hunkIndex = 0;

        foreach (var (startIndex, endIndex) in hunkRanges)
        {
            var hunk = BuildHunk(diffResult.Lines, startIndex, endIndex, hunkIndex);
            hunks.Add(hunk);
            hunkIndex++;
        }

        return hunks;
    }

    /// <inheritdoc />
    public string GenerateHunkPatch(string filePath, DiffHunk hunk)
    {
        var sb = new StringBuilder();

        // Unified diff header
        sb.AppendLine($"--- a/{filePath}");
        sb.AppendLine($"+++ b/{filePath}");

        // Hunk header
        sb.AppendLine(hunk.Header);

        // Lines with proper prefixes
        foreach (var line in hunk.Lines)
        {
            var prefix = line.Type switch
            {
                DiffLineType.Added => "+",
                DiffLineType.Deleted => "-",
                _ => " "
            };
            sb.AppendLine($"{prefix}{line.Text}");
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public string GenerateReversePatch(string filePath, DiffHunk hunk)
    {
        var sb = new StringBuilder();

        // Unified diff header (same for reverse)
        sb.AppendLine($"--- a/{filePath}");
        sb.AppendLine($"+++ b/{filePath}");

        // Reverse hunk header (swap old and new counts)
        sb.AppendLine($"@@ -{hunk.NewStartLine},{hunk.NewLineCount} +{hunk.OldStartLine},{hunk.OldLineCount} @@");

        // Lines with swapped prefixes (added becomes deleted, deleted becomes added)
        foreach (var line in hunk.Lines)
        {
            var prefix = line.Type switch
            {
                DiffLineType.Added => "-",     // Added lines become deletions in reverse
                DiffLineType.Deleted => "+",   // Deleted lines become additions in reverse
                _ => " "
            };
            sb.AppendLine($"{prefix}{line.Text}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Group change indices into hunk ranges based on context line proximity.
    /// </summary>
    private static List<(int start, int end)> GroupChangesIntoHunks(
        List<int> changeIndices,
        int contextLines,
        int totalLines)
    {
        var ranges = new List<(int start, int end)>();

        int currentStart = Math.Max(0, changeIndices[0] - contextLines);
        int currentEnd = Math.Min(totalLines - 1, changeIndices[0] + contextLines);

        for (int i = 1; i < changeIndices.Count; i++)
        {
            int changeStart = Math.Max(0, changeIndices[i] - contextLines);
            int changeEnd = Math.Min(totalLines - 1, changeIndices[i] + contextLines);

            // If this change's context overlaps with or is adjacent to current range, merge them
            if (changeStart <= currentEnd + 1)
            {
                currentEnd = Math.Max(currentEnd, changeEnd);
            }
            else
            {
                // No overlap, finalize current range and start new one
                ranges.Add((currentStart, currentEnd));
                currentStart = changeStart;
                currentEnd = changeEnd;
            }
        }

        // Add the last range
        ranges.Add((currentStart, currentEnd));

        return ranges;
    }

    /// <summary>
    /// Build a DiffHunk from a range of lines.
    /// </summary>
    private static DiffHunk BuildHunk(List<DiffLine> allLines, int startIndex, int endIndex, int hunkIndex)
    {
        var hunkLines = new List<DiffLine>();
        int? oldStart = null;
        int? newStart = null;
        int oldCount = 0;
        int newCount = 0;

        for (int i = startIndex; i <= endIndex; i++)
        {
            var line = allLines[i];
            hunkLines.Add(line);

            // Track line counts for header
            switch (line.Type)
            {
                case DiffLineType.Unchanged:
                    oldCount++;
                    newCount++;
                    oldStart ??= line.OldLineNumber;
                    newStart ??= line.NewLineNumber;
                    break;
                case DiffLineType.Deleted:
                    oldCount++;
                    oldStart ??= line.OldLineNumber;
                    break;
                case DiffLineType.Added:
                    newCount++;
                    newStart ??= line.NewLineNumber;
                    break;
            }
        }

        return new DiffHunk
        {
            Index = hunkIndex,
            OldStartLine = oldStart ?? 1,
            OldLineCount = oldCount,
            NewStartLine = newStart ?? 1,
            NewLineCount = newCount,
            Lines = hunkLines
        };
    }
}
