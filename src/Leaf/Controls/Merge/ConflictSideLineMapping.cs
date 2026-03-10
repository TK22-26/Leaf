using System.Diagnostics;
using Leaf.Models;

namespace Leaf.Controls.Merge;

public enum ConflictSide { Ours, Theirs }

public enum ConflictViewLineKind { Content, Header, Spacer }

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
    private readonly ConflictViewLineKind[] _lineKinds;

    public int TotalLines { get; }
    public IReadOnlyList<ConflictRegionRange> AllConflictRanges => _conflictRanges;

    public ConflictViewLineKind GetLineKind(int line)
        => line >= 1 && line <= TotalLines ? _lineKinds[line - 1] : ConflictViewLineKind.Content;

    public bool IsHeaderLine(int line) => GetLineKind(line) == ConflictViewLineKind.Header;
    public bool IsSpacerLine(int line) => GetLineKind(line) == ConflictViewLineKind.Spacer;
    public bool IsHiddenMarginLine(int line) => GetLineKind(line) != ConflictViewLineKind.Content;

    private ConflictSideLineMapping(
        MergeRegion?[] lineToRegion,
        SelectableLine?[] lineToSelectable,
        IReadOnlyList<ConflictRegionRange> conflictRanges,
        ConflictViewLineKind[] lineKinds,
        int totalLines)
    {
        _lineToRegion = lineToRegion;
        _lineToSelectable = lineToSelectable;
        _conflictRanges = conflictRanges;
        _lineKinds = lineKinds;
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
    /// Returns the display line number for the given editor line, skipping non-content lines.
    /// Returns -1 for header and spacer lines.
    /// </summary>
    public int GetDisplayLineNumber(int editorLine)
    {
        if (editorLine < 1 || editorLine > TotalLines) return -1;
        if (IsHiddenMarginLine(editorLine)) return -1;

        int hiddenCount = 0;
        for (int i = 0; i < editorLine; i++)
            if (_lineKinds[i] != ConflictViewLineKind.Content)
                hiddenCount++;
        return editorLine - hiddenCount;
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
    /// Builds aligned line mappings for both sides in a single pass, inserting spacer lines
    /// so that both sides have identical line counts and structural alignment.
    /// </summary>
    public static (ConflictSideLineMapping OursMapping, string OursContent,
                   ConflictSideLineMapping TheirsMapping, string TheirsContent)
        BuildAligned(FileMergeResult result)
    {
        var oursRegions = new List<MergeRegion?>();
        var oursSelectables = new List<SelectableLine?>();
        var oursConflictRanges = new List<ConflictRegionRange>();
        var oursContent = new List<string>();
        var oursKinds = new List<ConflictViewLineKind>();

        var theirsRegions = new List<MergeRegion?>();
        var theirsSelectables = new List<SelectableLine?>();
        var theirsConflictRanges = new List<ConflictRegionRange>();
        var theirsContent = new List<string>();
        var theirsKinds = new List<ConflictViewLineKind>();

        foreach (var region in result.Regions)
        {
            switch (region.Type)
            {
                case MergeRegionType.Unchanged:
                    AddContentLines(region, oursContent, oursRegions, oursSelectables, oursKinds);
                    AddContentLines(region, theirsContent, theirsRegions, theirsSelectables, theirsKinds);
                    break;

                case MergeRegionType.OursOnly:
                {
                    int lineCount = GetRenderedLineCount(region);
                    AddContentLines(region, oursContent, oursRegions, oursSelectables, oursKinds);
                    AddSpacerLines(region, lineCount, theirsContent, theirsRegions, theirsSelectables, theirsKinds);
                    break;
                }

                case MergeRegionType.TheirsOnly:
                {
                    int lineCount = GetRenderedLineCount(region);
                    AddSpacerLines(region, lineCount, oursContent, oursRegions, oursSelectables, oursKinds);
                    AddContentLines(region, theirsContent, theirsRegions, theirsSelectables, theirsKinds);
                    break;
                }

                case MergeRegionType.Conflict:
                {
                    region.InitializeSelectableLines();

                    // Header line on both sides
                    oursContent.Add(string.Empty);
                    oursRegions.Add(region);
                    oursSelectables.Add(null);
                    oursKinds.Add(ConflictViewLineKind.Header);
                    int oursHeaderLine = oursContent.Count;

                    theirsContent.Add(string.Empty);
                    theirsRegions.Add(region);
                    theirsSelectables.Add(null);
                    theirsKinds.Add(ConflictViewLineKind.Header);
                    int theirsHeaderLine = theirsContent.Count;

                    Debug.Assert(oursHeaderLine == theirsHeaderLine,
                        $"Header line mismatch: ours={oursHeaderLine} theirs={theirsHeaderLine}");

                    var oursLines = region.OursLines;
                    var theirsLines = region.TheirsLines;
                    var oursSelectableLines = region.OursSelectableLines;
                    var theirsSelectableLines = region.TheirsSelectableLines;

                    int oursCount = oursLines.Count;
                    int theirsCount = theirsLines.Count;
                    int maxCount = Math.Max(oursCount, theirsCount);

                    if (maxCount == 0)
                    {
                        // Both sides empty — record zero-width marker
                        int emptyLine = oursContent.Count + 1;
                        oursConflictRanges.Add(new ConflictRegionRange(region, oursHeaderLine, emptyLine, emptyLine - 1));
                        theirsConflictRanges.Add(new ConflictRegionRange(region, theirsHeaderLine, emptyLine, emptyLine - 1));
                        break;
                    }

                    int startLine = oursContent.Count + 1;

                    // Emit content lines for each side, padding the shorter side with spacers
                    for (int i = 0; i < maxCount; i++)
                    {
                        if (i < oursCount)
                        {
                            oursContent.Add(oursLines[i]);
                            oursRegions.Add(region);
                            oursSelectables.Add(oursSelectableLines != null && i < oursSelectableLines.Count
                                ? oursSelectableLines[i] : null);
                            oursKinds.Add(ConflictViewLineKind.Content);
                        }
                        else
                        {
                            oursContent.Add(string.Empty);
                            oursRegions.Add(region);
                            oursSelectables.Add(null);
                            oursKinds.Add(ConflictViewLineKind.Spacer);
                        }

                        if (i < theirsCount)
                        {
                            theirsContent.Add(theirsLines[i]);
                            theirsRegions.Add(region);
                            theirsSelectables.Add(theirsSelectableLines != null && i < theirsSelectableLines.Count
                                ? theirsSelectableLines[i] : null);
                            theirsKinds.Add(ConflictViewLineKind.Content);
                        }
                        else
                        {
                            theirsContent.Add(string.Empty);
                            theirsRegions.Add(region);
                            theirsSelectables.Add(null);
                            theirsKinds.Add(ConflictViewLineKind.Spacer);
                        }
                    }

                    int endLine = oursContent.Count;

                    // Record conflict ranges — use actual content boundaries for each side
                    int oursEndContent = startLine + oursCount - 1;
                    int theirsEndContent = startLine + theirsCount - 1;

                    oursConflictRanges.Add(new ConflictRegionRange(region, oursHeaderLine,
                        oursCount > 0 ? startLine : startLine,
                        oursCount > 0 ? oursEndContent : startLine - 1));

                    theirsConflictRanges.Add(new ConflictRegionRange(region, theirsHeaderLine,
                        theirsCount > 0 ? startLine : startLine,
                        theirsCount > 0 ? theirsEndContent : startLine - 1));

                    break;
                }
            }
        }

        Debug.Assert(oursContent.Count == theirsContent.Count,
            $"BuildAligned content count mismatch: ours={oursContent.Count} theirs={theirsContent.Count}");

        var oursTotalLines = oursContent.Count;
        var theirsTotalLines = theirsContent.Count;
        var oursText = string.Join("\n", oursContent);
        var theirsText = string.Join("\n", theirsContent);

        var oursMapping = new ConflictSideLineMapping(
            oursRegions.ToArray(),
            oursSelectables.ToArray(),
            oursConflictRanges,
            oursKinds.ToArray(),
            oursTotalLines);

        var theirsMapping = new ConflictSideLineMapping(
            theirsRegions.ToArray(),
            theirsSelectables.ToArray(),
            theirsConflictRanges,
            theirsKinds.ToArray(),
            theirsTotalLines);

        return (oursMapping, oursText, theirsMapping, theirsText);
    }

    private static int GetRenderedLineCount(MergeRegion region) => SplitLines(region.Content).Count;

    private static void AddContentLines(
        MergeRegion region,
        List<string> contentLines,
        List<MergeRegion?> regionList,
        List<SelectableLine?> selectableList,
        List<ConflictViewLineKind> lineKinds)
    {
        var lines = SplitLines(region.Content);
        foreach (var line in lines)
        {
            contentLines.Add(line);
            regionList.Add(region);
            selectableList.Add(null);
            lineKinds.Add(ConflictViewLineKind.Content);
        }
    }

    private static void AddSpacerLines(
        MergeRegion region,
        int count,
        List<string> contentLines,
        List<MergeRegion?> regionList,
        List<SelectableLine?> selectableList,
        List<ConflictViewLineKind> lineKinds)
    {
        for (int i = 0; i < count; i++)
        {
            contentLines.Add(string.Empty);
            regionList.Add(region);
            selectableList.Add(null);
            lineKinds.Add(ConflictViewLineKind.Spacer);
        }
    }

    private static List<string> SplitLines(string content)
    {
        if (content == null) return [];
        if (content.Length == 0) return [string.Empty];
        return content.Split('\n').ToList();
    }
}
