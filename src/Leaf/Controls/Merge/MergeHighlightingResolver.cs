#nullable enable
using System.IO;
using Leaf.TextEdit.Highlighting;

namespace Leaf.Controls.Merge;

/// <summary>
/// Single source of truth for "given a conflict file path, pick an AvalonEdit
/// highlighting definition." Consumed by both <see cref="ReadOnlyMergePane"/>
/// and <see cref="ResultPane"/>; before C4-closeout each pane kept its own
/// copy of this logic, inviting drift between the three rendering surfaces.
/// </summary>
/// <remarks>
/// Returns <c>null</c> when the path has no extension or the extension is
/// unregistered — that's an explicit "render in a single foreground colour"
/// signal, not a silent fallback for a missing path. Callers treat null as
/// "no highlighting available" and leave the editor's default colouring in
/// place.
/// </remarks>
internal static class MergeHighlightingResolver
{
    public static IHighlightingDefinition? ByFilePath(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext)) return null;
        return HighlightingManager.Instance.GetDefinitionByExtension(ext);
    }
}
