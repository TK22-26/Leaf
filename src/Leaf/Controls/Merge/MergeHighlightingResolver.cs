#nullable enable
using System.IO;
using Leaf.TextEdit.Highlighting;

namespace Leaf.Controls.Merge;

/// <summary>
/// Single source of truth for "given a conflict file path, pick an AvalonEdit
/// highlighting definition." Consumed by both <see cref="ReadOnlyMergePane"/>
/// and <see cref="ResultPane"/>.
/// </summary>
/// <remarks>
/// Returns <c>null</c> when the path has no extension or the extension is
/// unregistered. Callers treat null as "no highlighting available" and leave
/// the editor's default colouring in place.
/// </remarks>
internal static class MergeHighlightingResolver
{
    /// <summary>
    /// Registration name of the plain markdown definition.
    /// </summary>
    private const string MarkdownNoFontSizeName = "MarkDown";

    /// <summary>
    /// Per-extension overrides for definitions whose preview-style variants
    /// can change glyph metrics in ways that break the merge editor's
    /// uniform-line-height requirement.
    /// </summary>
    /// <remarks>
    /// Markdown keeps two definitions available:
    /// <list type="bullet">
    /// <item><c>MarkDownWithFontSize</c> changes heading font sizes and
    /// inline code font family.</item>
    /// <item><c>MarkDown</c> uses the same token matching, but only
    /// colour-level presentation.</item>
    /// </list>
    /// Keep the named override here as a defensive guard in case registration
    /// order changes again.
    /// </remarks>
    private static readonly Dictionary<string, string> NamedOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        [".md"] = MarkdownNoFontSizeName,
        [".markdown"] = MarkdownNoFontSizeName,
    };

    public static IHighlightingDefinition? ByFilePath(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext)) return null;
        if (NamedOverrides.TryGetValue(ext, out var name))
        {
            var named = HighlightingManager.Instance.GetDefinition(name);
            if (named is not null) return named;
        }
        return HighlightingManager.Instance.GetDefinitionByExtension(ext);
    }
}
