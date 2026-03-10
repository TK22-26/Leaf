namespace Leaf.Models.Merge;

/// <summary>
/// Represents the overall state of a merge conflict resolution session.
/// </summary>
public enum MergeSessionState
{
    Loading,
    Ready,
    Dirty,
    Saving,
    Completed,
    Error
}
