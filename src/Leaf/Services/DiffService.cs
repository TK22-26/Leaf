using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for computing textual differences using DiffPlex.
/// DiffPlex is licensed under Apache 2.0 - see THIRD_PARTY_LICENSES.txt
/// </summary>
public class DiffService : IDiffService
{
    /// <inheritdoc />
    public FileDiffResult ComputeDiff(string oldContent, string newContent, string fileName, string filePath)
    {
        var result = new FileDiffResult
        {
            FileName = fileName,
            FilePath = filePath,
            OldContent = oldContent,
            NewContent = newContent,
            IsBinary = IsBinaryContent(oldContent) || IsBinaryContent(newContent)
        };

        if (result.IsBinary)
        {
            return result;
        }

        // Use inline diff builder for unified view
        var diffModel = InlineDiffBuilder.Diff(oldContent, newContent);

        // Build inline content and line info
        var contentBuilder = new System.Text.StringBuilder();
        int linesAdded = 0;
        int linesDeleted = 0;

        foreach (var line in diffModel.Lines)
        {
            var diffLine = new DiffLine
            {
                Text = line.Text ?? string.Empty,
                Type = MapChangeType(line.Type)
            };

            result.Lines.Add(diffLine);
            contentBuilder.AppendLine(line.Text ?? string.Empty);

            if (line.Type == ChangeType.Inserted)
                linesAdded++;
            else if (line.Type == ChangeType.Deleted)
                linesDeleted++;
        }

        result.InlineContent = contentBuilder.ToString();
        result.LinesAddedCount = linesAdded;
        result.LinesDeletedCount = linesDeleted;

        return result;
    }

    private static DiffLineType MapChangeType(ChangeType changeType)
    {
        return changeType switch
        {
            ChangeType.Unchanged => DiffLineType.Unchanged,
            ChangeType.Deleted => DiffLineType.Deleted,
            ChangeType.Inserted => DiffLineType.Added,
            ChangeType.Modified => DiffLineType.Modified,
            ChangeType.Imaginary => DiffLineType.Imaginary,
            _ => DiffLineType.Unchanged
        };
    }

    private static bool IsBinaryContent(string content)
    {
        if (string.IsNullOrEmpty(content))
            return false;

        // Check for null bytes which indicate binary content
        // Only check first 8KB for performance
        var checkLength = Math.Min(content.Length, 8192);
        for (int i = 0; i < checkLength; i++)
        {
            if (content[i] == '\0')
                return true;
        }

        return false;
    }
}
