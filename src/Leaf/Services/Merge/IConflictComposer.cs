using Leaf.Models;

namespace Leaf.Services.Merge;

/// <summary>
/// Composes merged content from resolution choices.
/// Source of truth for the mapping from resolution choices to merged text.
/// </summary>
public interface IConflictComposer
{
    string ComposeMergedContent(FileMergeResult result);
    void ApplyManualEdit(FileMergeResult result, int regionIndex, string editedContent);
}
