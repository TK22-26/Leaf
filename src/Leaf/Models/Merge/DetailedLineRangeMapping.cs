namespace Leaf.Models.Merge;

/// <summary>
/// Maps a line range in the base document to a corresponding line range in a modified version.
/// Describes how one side (Ours or Theirs) differs from the common ancestor at line granularity.
/// </summary>
/// <param name="BaseRange">Affected region in the base document.</param>
/// <param name="ModifiedRange">Affected region in the modified document.</param>
/// <param name="InnerMappings">
/// Optional word-/token-level mappings inside the line range pair.
/// Populated by Phase 3's word-diff service; <c>null</c> or empty until then.
/// </param>
public sealed record DetailedLineRangeMapping(
    LineRange BaseRange,
    LineRange ModifiedRange,
    IReadOnlyList<WordLevelMapping>? InnerMappings = null);

/// <summary>
/// Token-level mapping within a line-range pair. Columns are 1-based character positions;
/// ranges are half-open (<c>[Start, EndExclusive)</c>).
/// </summary>
public sealed record WordLevelMapping(
    int BaseLine,
    int BaseStartColumn,
    int BaseEndColumnExclusive,
    int ModifiedLine,
    int ModifiedStartColumn,
    int ModifiedEndColumnExclusive);
