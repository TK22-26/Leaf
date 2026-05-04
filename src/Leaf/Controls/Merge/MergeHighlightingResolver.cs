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
    /// <summary>
    /// Registration name (in <c>HighlightingManager.Instance</c>) of the
    /// no-font-size markdown definition. Must match the first argument of
    /// the <c>RegisterHighlighting</c> call for <c>MarkDown-Mode.xshd</c>
    /// in <c>TextEdit/Highlighting/Resources/Resources.cs</c>. If that
    /// registration name changes, this falls back to whichever definition
    /// <c>GetDefinitionByExtension</c> returns — which is the
    /// <c>MarkDownWithFontSize</c> definition (the one that breaks the
    /// merge editor's same-glyph-height invariant).
    /// </summary>
    private const string MarkdownNoFontSizeName = "MarkDown";

    /// <summary>
    /// Per-extension overrides for definitions whose <em>default</em>
    /// registration changes glyph metrics in ways that break the merge
    /// editor's "every line at the same weight" requirement.
    /// </summary>
    /// <remarks>
    /// Markdown's two registrations both bind to <c>.md</c>:
    /// <list type="bullet">
    /// <item><c>MarkDownWithFontSize</c> — sets <c>fontSize="30..15"</c>
    /// on H1–H6 headings; reading order in the registration list makes
    /// this the one <see cref="HighlightingManager.GetDefinitionByExtension"/>
    /// returns for <c>.md</c>.</item>
    /// <item><c>MarkDown</c> — same colours, no font-size deltas. Headings
    /// still hit <c>Heading</c> (Maroon foreground), emphasis still
    /// italic, strong-emphasis still bold inside the same line — all
    /// fine; the per-line height stays uniform.</item>
    /// </list>
    /// In the merge editor we want the colours but not the size changes,
    /// so we explicitly look up the no-font-size definition by name.
    /// Other consumers (DiffViewer, BlameEditor) keep the default
    /// extension lookup and so still see whichever the manager hands them.
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
