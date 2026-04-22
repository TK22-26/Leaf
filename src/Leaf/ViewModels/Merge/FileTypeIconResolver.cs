#nullable enable
using System.IO;
using FluentIcons.Common;

namespace Leaf.ViewModels.Merge;

/// <summary>
/// Maps a file path or folder node to the <see cref="Symbol"/> that
/// <see cref="Leaf.Controls.Merge.ConflictFileTree"/> shows next to it.
/// Single source of truth for per-file-type icon selection — the grouped
/// tree, the command palette's pending file-browser, and any future
/// file-list surfaces all resolve through this one helper so the icon
/// vocabulary stays consistent.
/// </summary>
/// <remarks>
/// Extension lookup is ordinal-case-insensitive. Unknown extensions fall
/// back to <see cref="Symbol.Document"/> — this is not a silent fallback for
/// missing data (the file path is required and <see cref="ResolveForFile"/>
/// throws on empty input), but a real design decision: an unknown extension
/// still gets a document-shaped glyph instead of no glyph at all.
/// </remarks>
public static class FileTypeIconResolver
{
    /// <summary>
    /// Return the <see cref="Symbol"/> for a file leaf. Throws on null or
    /// empty path — every <see cref="Leaf.Models.ConflictInfo"/> built by the
    /// git-plumbing path carries a non-empty FilePath; a missing path is
    /// always an upstream bug.
    /// </summary>
    public static Symbol ResolveForFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext)) return Symbol.Document;

        return ext.ToLowerInvariant() switch
        {
            // Source / markup — single Code glyph reads as "this file compiles
            // or is interpreted by something".
            ".cs" or ".fs" or ".fsx" or ".vb" or ".fsproj" or ".csproj" or ".sln"
                or ".js" or ".jsx" or ".mjs" or ".ts" or ".tsx"
                or ".py" or ".rb" or ".go" or ".rs" or ".swift" or ".kt" or ".kts"
                or ".java" or ".scala" or ".c" or ".cc" or ".cpp" or ".cxx"
                or ".h" or ".hpp" or ".m" or ".mm"
                or ".xaml" or ".xml" or ".xsd" or ".xslt"
                or ".html" or ".htm" or ".xhtml" or ".vue" or ".svelte"
                or ".sh" or ".bash" or ".ps1" or ".psm1"
                => Symbol.Code,

            // Structured data — Braces is the Fluent convention.
            ".json" or ".json5" or ".yaml" or ".yml" or ".toml" or ".ini"
                or ".proto" or ".graphql" or ".sql"
                => Symbol.Braces,

            // Images — Image glyph.
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg"
                or ".webp" or ".ico" or ".tif" or ".tiff"
                => Symbol.Image,

            _ => Symbol.Document,
        };
    }
}
