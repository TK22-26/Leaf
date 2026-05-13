namespace Leaf.Models;

/// <summary>
/// Coarse-grained grouping for toast notifications so the user can switch
/// whole classes of toast on or off from Settings rather than hunting for
/// an individual message. Each value maps to one toggle in the
/// Notifications settings page; the corresponding default lives on
/// <see cref="Leaf.Services.AppSettings"/>.
/// </summary>
/// <remarks>
/// Error toasts have no category — they bypass the filter entirely (the
/// <see cref="Leaf.Services.INotificationService"/> signature accepts a
/// <c>null</c> category, which means "always show"). That keeps the user
/// from accidentally hiding failures while pruning routine confirmations.
/// </remarks>
public enum NotificationCategory
{
    /// <summary>Pull / push / fetch completion summaries.</summary>
    SyncOperations,

    /// <summary>Branch / tag checkout success and checkout-conflict warnings.</summary>
    BranchCheckout,

    /// <summary>Branch admin: create / delete / rename / set-upstream / branch-level push or pull.</summary>
    BranchAdmin,

    /// <summary>Merge, rebase, cherry-pick, revert completions and conflict warnings.</summary>
    MergeAndRebase,

    /// <summary>GitFlow start / finish / publish.</summary>
    GitFlow,

    /// <summary>Worktree create / remove / switch / lock / unlock / prune.</summary>
    Worktree,

    /// <summary>Submodule init / update / add / remove / sync.</summary>
    Submodule,

    /// <summary>Stash save / pop / drop.</summary>
    Stash,

    /// <summary>Pull request created / closed / not-available notices.</summary>
    PullRequest,

    /// <summary>Patch created / applied / copied.</summary>
    Patch,

    /// <summary>Repository management — repository added / cloned / scan results / watch folder events.</summary>
    Repository,

    /// <summary>Remote configuration changes — add / edit / remove / set-default / URL copied.</summary>
    RemoteConfig,

    /// <summary>"Aborted" / "cancelled" confirmations and similar minor info messages.</summary>
    CancelledOperations,
}
