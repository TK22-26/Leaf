using Leaf.Models;
using Leaf.Services;

namespace Leaf.Services.Merge;

/// <summary>
/// Log-based implementation of merge UI logging.
/// </summary>
public sealed class MergeUiLogger : IMergeUiLogger
{
    public void RegionResolved(int index, ConflictResolution choice)
        => Log.Info("MergeUI", $"RegionResolved: region={index} resolution={choice}");

    public void FileTabSelected(string fileName, bool isResolved)
        => Log.Info("MergeUI", $"FileTabSelected: file={fileName} isResolved={isResolved}");

    public void ProgressUpdate(int filesResolved, int filesTotal, int regionsResolved, int regionsTotal)
        => Log.Info("MergeUI", $"ProgressUpdate: files={filesResolved}/{filesTotal} regions={regionsResolved}/{regionsTotal}");

    public void AutoAdvance(string from, string to)
        => Log.Info("MergeUI", $"AutoAdvance: from={from} to={to}");

    public void ScrollSync(string direction, double offset)
        => Log.Info("MergeUI", $"ScrollSync: {direction} offset={offset:F0}");

    public void WindowOpened(int fileCount, string source, string target)
        => Log.Info("MergeUI", $"WindowOpened: files={fileCount} source={source} target={target}");

    public void WindowClosed(int resolved, int total)
        => Log.Info("MergeUI", $"WindowClosed: filesResolved={resolved}/{total}");

    public void BinaryFile(string filePath)
        => Log.Info("MergeUI", $"BinaryFile: {filePath}");

    public void LargeFile(string filePath, int lineCount)
        => Log.Info("MergeUI", $"LargeFile: {filePath} totalLines={lineCount}");

    public void ContentDivergence(int editorLen, int modelLen)
        => Log.Warn("MergeUI", $"ContentDivergence: editorLen={editorLen} modelLen={modelLen}");

    public void UndoAction(string description)
        => Log.Info("MergeUI", $"UndoAction: {description}");

    public void RedoAction(string description)
        => Log.Info("MergeUI", $"RedoAction: {description}");

    public void TakeBothHunk(int regionIndex, int oursLines, int theirsLines)
        => Log.Info("MergeUI", $"TakeBothHunk: region={regionIndex} oursLines={oursLines} theirsLines={theirsLines}");
}
