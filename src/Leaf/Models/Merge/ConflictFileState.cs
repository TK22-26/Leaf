namespace Leaf.Models.Merge;

/// <summary>
/// Represents the resolution state of a single conflicted file.
/// </summary>
public enum ConflictFileState
{
    Unresolved,
    PartiallyResolved,
    FullyResolved,
    ManuallyEdited,
    Saved
}
