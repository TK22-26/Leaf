namespace Leaf.Models;

/// <summary>
/// Represents the type of a merge region in a three-way merge.
/// </summary>
public enum MergeRegionType
{
    /// <summary>
    /// Content is the same in all three versions, or both sides made identical changes.
    /// </summary>
    Unchanged,

    /// <summary>
    /// Content was changed only in the "ours" (current) branch - auto-merge.
    /// </summary>
    OursOnly,

    /// <summary>
    /// Content was changed only in the "theirs" (incoming) branch - auto-merge.
    /// </summary>
    TheirsOnly,

    /// <summary>
    /// Content was changed differently in both branches - requires user resolution.
    /// </summary>
    Conflict
}

/// <summary>
/// Represents how a conflict region has been resolved.
/// </summary>
public enum ConflictResolution
{
    /// <summary>
    /// Conflict has not been resolved yet.
    /// </summary>
    Unresolved,

    /// <summary>
    /// Use the entire "ours" version.
    /// </summary>
    UseOurs,

    /// <summary>
    /// Use the entire "theirs" version.
    /// </summary>
    UseTheirs,

    /// <summary>
    /// Use custom per-line selection from both versions.
    /// </summary>
    UseCustom,

    /// <summary>
    /// Use manually edited content.
    /// </summary>
    UseManual
}
