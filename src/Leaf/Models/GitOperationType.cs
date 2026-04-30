namespace Leaf.Models;

/// <summary>
/// The type of git operation currently in progress (detected from .git/ sentinel files).
/// </summary>
public enum GitOperationType
{
    /// <summary>
    /// No operation in progress.
    /// </summary>
    None,

    /// <summary>
    /// A merge is in progress (.git/MERGE_HEAD exists).
    /// </summary>
    Merge,

    /// <summary>
    /// A cherry-pick is in progress (.git/CHERRY_PICK_HEAD exists).
    /// </summary>
    CherryPick,

    /// <summary>
    /// A revert is in progress (.git/REVERT_HEAD exists).
    /// </summary>
    Revert,

    /// <summary>
    /// A rebase is in progress (.git/rebase-merge or .git/rebase-apply exists).
    /// </summary>
    Rebase,

    /// <summary>
    /// A <c>git am</c> is paused on a conflict (.git/rebase-apply/applying exists).
    /// Distinct from <see cref="Rebase"/> because the control verbs are
    /// different (<c>git am --continue/--skip/--abort</c> vs the rebase set).
    /// </summary>
    Am
}
