namespace Leaf.Models.Merge;

/// <summary>
/// Per-displayed-line classification for the merge editor's result pane.
/// Distinguishes context lines, the four zdiff3 marker rows, content lines
/// inside an unresolved conflict (sub-classified by section), and content
/// lines inside a resolved conflict (sub-classified by which side they came
/// from). Drives gutter numbering, background tinting, and inline-element
/// range-index lookups from a single shared classification.
/// </summary>
public enum MergeLineKind
{
    /// <summary>
    /// Line outside any conflict — file context that flows through unchanged.
    /// </summary>
    Context,

    /// <summary><c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> opener row of an unresolved conflict.</summary>
    OpenMarker,

    /// <summary><c>|||||||</c> base separator of an unresolved zdiff3 conflict.</summary>
    BaseMarker,

    /// <summary><c>=======</c> separator of an unresolved conflict.</summary>
    EqualsMarker,

    /// <summary><c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c> closer row of an unresolved conflict.</summary>
    CloseMarker,

    /// <summary>Content line inside the ours-section of an unresolved conflict.</summary>
    UnresolvedOurs,

    /// <summary>Content line inside the base-section of an unresolved conflict.</summary>
    UnresolvedBase,

    /// <summary>Content line inside the theirs-section of an unresolved conflict.</summary>
    UnresolvedTheirs,

    /// <summary>
    /// Body line of a resolved conflict that came from the ours side
    /// (AcceptOurs or AcceptBoth's ours portion).
    /// </summary>
    ResolvedOurs,

    /// <summary>
    /// Body line of a resolved conflict that came from the theirs side
    /// (AcceptTheirs or AcceptBoth's theirs portion).
    /// </summary>
    ResolvedTheirs,

    /// <summary>
    /// Body line of a Manual resolution. No canonical file-side mapping.
    /// </summary>
    ResolvedManual,
}

/// <summary>
/// Classification + numbering for a single displayed line in the result pane.
/// </summary>
/// <param name="Kind">Structural role of this line.</param>
/// <param name="RangeIndex">
/// <see cref="ModifiedBaseRange.Index"/> for any line inside a conflict (markers
/// or content); <c>-1</c> for <see cref="MergeLineKind.Context"/> lines.
/// </param>
/// <param name="FileLineNumber">
/// 1-based number to render in the gutter, or <c>null</c> for marker rows
/// and Manual-resolution rows that have no canonical file-side mapping.
/// </param>
public readonly record struct MergeDisplayLine(
    MergeLineKind Kind,
    int RangeIndex,
    int? FileLineNumber);

/// <summary>
/// Immutable per-line classification for a result-pane document.
/// Built by <see cref="MergeDocument.BuildDisplayMap"/>; consumed by the
/// margin (gutter numbers), background renderer (per-line tint), and
/// inline element generator (marker → range-index lookup) — three views
/// of one structure, eliminating the three duplicated walkers that each
/// re-derived the same per-line data with subtly different bugs.
/// </summary>
public sealed class MergeDisplayMap
{
    /// <summary>1-indexed; index 0 is unused and always returns the default.</summary>
    private readonly MergeDisplayLine[] _lines;

    /// <summary>Line count of the displayed result-pane document this map was built against.</summary>
    public int LineCount { get; }

    internal MergeDisplayMap(MergeDisplayLine[] lines, int lineCount)
    {
        _lines = lines ?? throw new ArgumentNullException(nameof(lines));
        LineCount = lineCount;
    }

    /// <summary>
    /// Look up the classification for a 1-based document line number.
    /// Out-of-range lookups return <see cref="MergeLineKind.Context"/> with
    /// <c>RangeIndex=-1</c> and <c>FileLineNumber=null</c> — defensive default
    /// matches the natural "outside any conflict" semantics.
    /// </summary>
    public MergeDisplayLine GetLine(int lineNumber1Based)
    {
        if (lineNumber1Based < 1 || lineNumber1Based >= _lines.Length)
        {
            return new MergeDisplayLine(MergeLineKind.Context, -1, null);
        }
        return _lines[lineNumber1Based];
    }
}
