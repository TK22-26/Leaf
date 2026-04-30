namespace Leaf.Models;

/// <summary>
/// Outcome of <c>git am</c> or <c>git apply</c>. The shape mirrors
/// <see cref="MergeResult"/> on purpose so the merge-editor conflict
/// flow can consume <c>am</c> conflicts the same way it consumes rebase
/// and merge conflicts.
/// </summary>
public sealed class ApplyPatchResult
{
    /// <summary>True when every patch applied cleanly.</summary>
    public bool Success { get; init; }

    /// <summary>True when <c>git am</c> stopped at a conflicting hunk; user resolves via the merge editor and continues.</summary>
    public bool HasConflicts { get; init; }

    /// <summary>SHA of the first patch that conflicted (when <see cref="HasConflicts"/> is true), or empty.</summary>
    public string ConflictAtSha { get; init; } = string.Empty;

    /// <summary>git's stderr text on hard failure (no rebase / am state on disk).</summary>
    public string? ErrorMessage { get; init; }
}
