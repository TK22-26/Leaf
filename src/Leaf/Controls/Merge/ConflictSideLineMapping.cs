using System.Diagnostics;
using Leaf.Models;

namespace Leaf.Controls.Merge;

public enum ConflictSide { Ours, Theirs }

public record ConflictRegionRange(MergeRegion Region, int HeaderLine, int StartLine, int EndLine); // 1-based inclusive

/// <summary>
/// Maps 1-based editor line numbers to MergeRegion/SelectableLine for one side of a conflict.
/// Immutable after build for simpler reasoning and fewer sync bugs.
/// </summary>
public sealed class ConflictSideLineMapping
{
    private readonly MergeRegion?[] _lineToRegion;
    private readonly SelectableLine?[] _lineToSelectable;
    private readonly IReadOnlyList<ConflictRegionRange> _conflictRanges;
    private readonly HashSet<int> _headerLines;

    public int TotalLines { get; }
    public IReadOnlyList<ConflictRegionRange> AllConflictRanges => _conflictRanges;

    /// <summary>
    /// Returns true if this line is a blank header line inserted before a conflict region.
    /// </summary>
    public bool IsHeaderLine(int line) => _headerLines.Contains(line);

    private ConflictSideLineMapping(
        MergeRegion?[] lineToRegion,
        SelectableLine?[] lineToSelectable,
        IReadOnlyList<ConflictRegionRange> conflictRanges,
        HashSet<int> headerLines,
        int totalLines)
    {
        _lineToRegion = lineToRegion;
        _lineToSelectable = lineToSelectable;
        _conflictRanges = conflictRanges;
        _headerLines = headerLines;
        TotalLines = totalLines;
    }

    public MergeRegion? GetRegionForLine(int line)
    {
        if (line < 1 || line > TotalLines) return null;
        return _lineToRegion[line - 1];
    }

    public SelectableLine? GetSelectableLineForLine(int line)
    {
        if (line < 1 || line > TotalLines) return null;
        return _lineToSelectable[line - 1];
    }

    public ConflictRegionRange? GetConflictRange(int regionIndex)
    {
        return _conflictRanges.FirstOrDefault(r => r.Region.Index == regionIndex);
    }

    /// <summary>
    /// Returns the display line number for the given editor line, skipping header lines.
    /// Returns -1 for header lines themselves.
    /// </summary>
    public int GetDisplayLineNumber(int editorLine)
    {
        if (editorLine < 1 || editorLine > TotalLines) return -1;
        if (_headerLines.Contains(editorLine)) return -1;

        int headerCount = 0;
        foreach (var h in _headerLines)
        {
            if (h <= editorLine)
                headerCount++;
        }
        return editorLine - headerCount;
    }

    public ConflictRegionRange? GetNextConflictRange(int afterLine)
    {
        foreach (var range in _conflictRanges)
        {
            if (range.StartLine > afterLine)
                return range;
        }
        // Wrap
        return _conflictRanges.Count > 0 ? _conflictRanges[0] : null;
    }

    public ConflictRegionRange? GetPreviousConflictRange(int beforeLine)
    {
        for (int i = _conflictRanges.Count - 1; i >= 0; i--)
        {
            if (_conflictRanges[i].StartLine < beforeLine)
                return _conflictRanges[i];
        }
        // Wrap
        return _conflictRanges.Count > 0 ? _conflictRanges[^1] : null;
    }

    public int GetFirstSelectableLineInRegion(int regionIndex)
    {
        var range = GetConflictRange(regionIndex);
        if (range == null) return -1;

        for (int line = range.StartLine; line <= range.EndLine; line++)
        {
            if (_lineToSelectable[line - 1] != null)
                return line;
        }
        return range.StartLine;
    }

    /// <summary>
    /// Builds the full-file content for one side and the line mapping.
    /// Returns (mapping, fullFileContent) where fullFileContent is the joined text for the editor.
    /// </summary>
    public static (ConflictSideLineMapping Mapping, string Content) Build(FileMergeResult result, ConflictSide side)
    {
        var regionList = new List<MergeRegion?>();
        var selectableList = new List<SelectableLine?>();
        var conflictRanges = new List<ConflictRegionRange>();
        var contentLines = new List<string>();
        var headerLines = new HashSet<int>();

        foreach (var region in result.Regions)
        {
            switch (region.Type)
            {
                case MergeRegionType.Unchanged:
                    AddContentLines(region, contentLines, regionList, selectableList);
                    break;

                case MergeRegionType.OursOnly:
                    if (side == ConflictSide.Ours)
                        AddContentLines(region, contentLines, regionList, selectableList);
                    break;

                case MergeRegionType.TheirsOnly:
                    if (side == ConflictSide.Theirs)
                        AddContentLines(region, contentLines, regionList, selectableList);
                    break;

                case MergeRegionType.Conflict:
                    region.InitializeSelectableLines();
                    var lines = side == ConflictSide.Ours ? region.OursLines : region.TheirsLines;
                    var selectableLines = side == ConflictSide.Ours
                        ? region.OursSelectableLines
                        : region.TheirsSelectableLines;

                    // Insert blank header line before the conflict region
                    int headerLine = contentLines.Count + 1;
                    contentLines.Add(string.Empty);
                    regionList.Add(region);
                    selectableList.Add(null);
                    headerLines.Add(headerLine);

                    if (lines.Count == 0)
                    {
                        // Empty side — record the range as a zero-width marker at next line position
                        int emptyLine = contentLines.Count + 1;
                        conflictRanges.Add(new ConflictRegionRange(region, headerLine, emptyLine, emptyLine - 1));
                        break;
                    }

                    int startLine = contentLines.Count + 1;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        contentLines.Add(lines[i]);
                        regionList.Add(region);
                        selectableList.Add(selectableLines != null && i < selectableLines.Count
                            ? selectableLines[i]
                            : null);
                    }
                    int endLine = contentLines.Count;
                    conflictRanges.Add(new ConflictRegionRange(region, headerLine, startLine, endLine));
                    break;
            }
        }

        var totalLines = contentLines.Count;
        var content = string.Join("\n", contentLines);

        // Build-time invariant check
        var expectedLineCount = content.Length == 0 ? 0 : content.Split('\n').Length;
        if (totalLines != expectedLineCount && totalLines > 0)
        {
            Debug.WriteLine($"[MERGE][WARN] ConflictSideLineMapping: TotalLines={totalLines} != content line count={expectedLineCount} for side={side}");
        }

        var mapping = new ConflictSideLineMapping(
            regionList.ToArray(),
            selectableList.ToArray(),
            conflictRanges,
            headerLines,
            totalLines);

        return (mapping, content);
    }

    private static void AddContentLines(
        MergeRegion region,
        List<string> contentLines,
        List<MergeRegion?> regionList,
        List<SelectableLine?> selectableList)
    {
        var lines = SplitLines(region.Content);
        foreach (var line in lines)
        {
            contentLines.Add(line);
            regionList.Add(region);
            selectableList.Add(null); // Non-conflict lines are not selectable
        }
    }

    private static List<string> SplitLines(string content)
    {
        if (content == null) return [];
        if (content.Length == 0) return [string.Empty];
        return content.Split('\n').ToList();
    }
}
