using Leaf.Models;

namespace Leaf.Services.Merge;

/// <summary>
/// Structured logging interface for merge UI operations.
/// </summary>
public interface IMergeUiLogger
{
    void RegionResolved(int index, ConflictResolution choice);
    void FileTabSelected(string fileName, bool isResolved);
    void ProgressUpdate(int filesResolved, int filesTotal, int regionsResolved, int regionsTotal);
    void AutoAdvance(string from, string to);
    void ScrollSync(string direction, double offset);
    void WindowOpened(int fileCount, string source, string target);
    void WindowClosed(int resolved, int total);
    void BinaryFile(string filePath);
    void LargeFile(string filePath, int lineCount);
    void ContentDivergence(int editorLen, int modelLen);
    void UndoAction(string description);
    void RedoAction(string description);
    void TakeBothHunk(int regionIndex, int oursLines, int theirsLines);
}
