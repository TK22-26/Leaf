using System.Diagnostics;
using Leaf.Models;

namespace Leaf.Services.Merge;

/// <summary>
/// Debug.WriteLine-based implementation of merge UI logging.
/// </summary>
public sealed class MergeUiLogger : IMergeUiLogger
{
    public void RegionResolved(int index, ConflictResolution choice)
        => Debug.WriteLine($"[MERGE][UI] RegionResolved: region={index} resolution={choice}");

    public void FileTabSelected(string fileName, bool isResolved)
        => Debug.WriteLine($"[MERGE][UI] FileTabSelected: file={fileName} isResolved={isResolved}");

    public void ProgressUpdate(int filesResolved, int filesTotal, int regionsResolved, int regionsTotal)
        => Debug.WriteLine($"[MERGE][UI] ProgressUpdate: files={filesResolved}/{filesTotal} regions={regionsResolved}/{regionsTotal}");

    public void AutoAdvance(string from, string to)
        => Debug.WriteLine($"[MERGE][UI] AutoAdvance: from={from} to={to}");

    public void ScrollSync(string direction, double offset)
        => Debug.WriteLine($"[MERGE][UI] ScrollSync: {direction} offset={offset:F0}");

    public void WindowOpened(int fileCount, string source, string target)
        => Debug.WriteLine($"[MERGE][UI] WindowOpened: files={fileCount} source={source} target={target}");

    public void WindowClosed(int resolved, int total)
        => Debug.WriteLine($"[MERGE][UI] WindowClosed: filesResolved={resolved}/{total}");

    public void BinaryFile(string filePath)
        => Debug.WriteLine($"[MERGE][UI] BinaryFile: {filePath}");

    public void LargeFile(string filePath, int lineCount)
        => Debug.WriteLine($"[MERGE][UI] LargeFile: {filePath} totalLines={lineCount}");

    public void ContentDivergence(int editorLen, int modelLen)
        => Debug.WriteLine($"[MERGE][UI] ContentDivergence: editorLen={editorLen} modelLen={modelLen}");

    public void UndoAction(string description)
        => Debug.WriteLine($"[MERGE][UI] UndoAction: {description}");

    public void RedoAction(string description)
        => Debug.WriteLine($"[MERGE][UI] RedoAction: {description}");

    public void TakeBothHunk(int regionIndex, int oursLines, int theirsLines)
        => Debug.WriteLine($"[MERGE][UI] TakeBothHunk: region={regionIndex} oursLines={oursLines} theirsLines={theirsLines}");
}
