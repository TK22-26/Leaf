namespace Leaf.Models;

/// <summary>
/// Type of merge operation to perform.
/// </summary>
public enum MergeType
{
    /// <summary>
    /// Standard merge that preserves commit history.
    /// </summary>
    Normal,

    /// <summary>
    /// Squash merge that combines all commits into one.
    /// </summary>
    Squash,

    /// <summary>
    /// Fast-forward only - fails if not possible.
    /// </summary>
    FastForwardOnly
}
