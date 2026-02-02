namespace Leaf.Services;

/// <summary>
/// Centralized event bus for repository state changes.
/// Services call Notify* methods after mutations; ViewModels subscribe to RefreshRequested.
/// </summary>
/// <remarks>
/// <para>
/// Design decisions:
/// - Single event type only (RefreshRequested with RefreshScope) - no per-scope events
/// - Synchronous event - handlers must NOT be async (avoids async-void crash risk)
/// - Single orchestrated refresh pipeline - MainViewModel owns async refresh logic
/// - Exception isolation - hub catches and logs subscriber exceptions
/// </para>
/// <para>
/// Threading: Notify* methods can be called from any thread. The event is always
/// raised on the UI thread (via IDispatcherService).
/// </para>
/// </remarks>
public interface IRepositoryEventHub
{
    /// <summary>
    /// Event raised when a refresh is requested.
    /// Subscribers filter by scope flags.
    /// WARNING: Handlers must NOT be async - all async work should be kicked off synchronously.
    /// </summary>
    event Action<RefreshScope>? RefreshRequested;

    /// <summary>
    /// Notify that branches have changed (created, deleted, renamed, checked out).
    /// </summary>
    void NotifyBranchesChanged();

    /// <summary>
    /// Notify that the working directory has changed (staged, unstaged, discarded).
    /// </summary>
    void NotifyWorkingDirectoryChanged();

    /// <summary>
    /// Notify that commit history has changed (new commits, resets, rebases).
    /// </summary>
    void NotifyCommitHistoryChanged();

    /// <summary>
    /// Notify that stashes have changed (created, popped, deleted).
    /// </summary>
    void NotifyStashesChanged();

    /// <summary>
    /// Notify that conflict state has changed (merge started, conflicts resolved).
    /// </summary>
    void NotifyConflictStateChanged();

    /// <summary>
    /// Request a refresh with specific scope flags.
    /// Use this for custom combinations or to trigger refresh programmatically.
    /// </summary>
    /// <param name="scope">Scope flags indicating what needs to be refreshed.</param>
    void RequestRefresh(RefreshScope scope);
}

/// <summary>
/// Flags indicating which parts of the repository state need to be refreshed.
/// </summary>
[Flags]
public enum RefreshScope
{
    /// <summary>No refresh needed.</summary>
    None = 0,

    /// <summary>Refresh branch list.</summary>
    Branches = 1,

    /// <summary>Refresh working directory (staged/unstaged files).</summary>
    WorkingDirectory = 2,

    /// <summary>Refresh commit history/graph.</summary>
    CommitHistory = 4,

    /// <summary>Refresh stash list.</summary>
    Stashes = 8,

    /// <summary>Refresh conflict state.</summary>
    Conflicts = 16,

    /// <summary>Refresh everything.</summary>
    All = Branches | WorkingDirectory | CommitHistory | Stashes | Conflicts
}
