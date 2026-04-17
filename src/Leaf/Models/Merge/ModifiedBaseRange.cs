namespace Leaf.Models.Merge;

/// <summary>
/// A single modified region discovered during a three-way merge. Each range spans
/// a run of lines that differs from base on at least one side. Mirrors VS Code's
/// <c>ModifiedBaseRange</c> (MIT) so that the composable <see cref="ResolutionState"/>
/// model maps cleanly onto the data.
/// </summary>
/// <param name="Index">Zero-based position in the parent <see cref="MergeDocument"/>'s range list.</param>
/// <param name="Base">The range in the base document this region corresponds to.</param>
/// <param name="Ours">The range in the ours document this region corresponds to.</param>
/// <param name="Theirs">The range in the theirs document this region corresponds to.</param>
/// <param name="ResultMarkedRange">
/// The range in the initial merged output (with zdiff3 markers still present) this
/// region occupies. Used by the composer to substitute resolved content back in.
/// </param>
/// <param name="BaseLines">Lines from the base document in <see cref="Base"/>.</param>
/// <param name="OursLines">Lines from the ours document in <see cref="Ours"/>.</param>
/// <param name="TheirsLines">Lines from the theirs document in <see cref="Theirs"/>.</param>
/// <param name="OursDiffs">Per-chunk diffs base→ours inside this range.</param>
/// <param name="TheirsDiffs">Per-chunk diffs base→theirs inside this range.</param>
/// <param name="IsConflicting">
/// <c>true</c> if both sides modified the region in ways Git could not auto-merge.
/// <c>false</c> for ranges where only one side changed (auto-resolved by Git; still surfaced
/// so the UI can draw informational connection lines).
/// </param>
/// <param name="IsOrderRelevant">
/// <c>true</c> if <see cref="ResolutionState.AcceptBoth"/> must pick an order
/// (i.e. both sides inserted overlapping content). <c>false</c> when the sides can
/// be combined commutatively.
/// </param>
public sealed record ModifiedBaseRange(
    int Index,
    LineRange Base,
    LineRange Ours,
    LineRange Theirs,
    LineRange ResultMarkedRange,
    IReadOnlyList<string> BaseLines,
    IReadOnlyList<string> OursLines,
    IReadOnlyList<string> TheirsLines,
    IReadOnlyList<DetailedLineRangeMapping> OursDiffs,
    IReadOnlyList<DetailedLineRangeMapping> TheirsDiffs,
    bool IsConflicting,
    bool IsOrderRelevant);
