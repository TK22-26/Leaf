using System.Diagnostics;

namespace Leaf.Services;

/// <summary>
/// Centralized event bus for repository state changes.
/// Coalesces rapid notifications and marshals to UI thread.
/// </summary>
public class RepositoryEventHub : IRepositoryEventHub
{
    private readonly IDispatcherService _dispatcher;
    private readonly object _lock = new();
    private RefreshScope _pendingRefresh = RefreshScope.None;
    private bool _dispatchScheduled;

    public event Action<RefreshScope>? RefreshRequested;

    public RepositoryEventHub(IDispatcherService dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <inheritdoc />
    public void NotifyBranchesChanged() => QueueRefresh(RefreshScope.Branches);

    /// <inheritdoc />
    public void NotifyWorkingDirectoryChanged() => QueueRefresh(RefreshScope.WorkingDirectory);

    /// <inheritdoc />
    public void NotifyCommitHistoryChanged() => QueueRefresh(RefreshScope.CommitHistory);

    /// <inheritdoc />
    public void NotifyStashesChanged() => QueueRefresh(RefreshScope.Stashes);

    /// <inheritdoc />
    public void NotifyConflictStateChanged() => QueueRefresh(RefreshScope.Conflicts);

    /// <inheritdoc />
    public void RequestRefresh(RefreshScope scope) => QueueRefresh(scope);

    private void QueueRefresh(RefreshScope scope)
    {
        if (scope == RefreshScope.None)
            return;

        lock (_lock)
        {
            _pendingRefresh |= scope;
            if (_dispatchScheduled)
                return;
            _dispatchScheduled = true;
        }

        // Marshal to UI thread - use InvokeAsync (non-blocking)
        try
        {
            var task = _dispatcher.InvokeAsync(() =>
            {
                RefreshScope toRaise;
                lock (_lock)
                {
                    toRaise = _pendingRefresh;
                    _pendingRefresh = RefreshScope.None;
                    _dispatchScheduled = false;
                }

                // Raise with exception isolation per subscriber
                var handler = RefreshRequested;
                if (handler != null)
                {
                    foreach (var subscriber in handler.GetInvocationList())
                    {
                        try
                        {
                            ((Action<RefreshScope>)subscriber)(toRaise);
                        }
                        catch (Exception ex)
                        {
                            // Log to debug output - subscriber exceptions shouldn't crash the app
                            Debug.WriteLine($"Subscriber threw during RefreshRequested: {ex.Message}");
                            // Continue to other subscribers
                        }
                    }
                }
            });

            // Observe task faults to prevent unobserved task exceptions
            task.ContinueWith(
                t =>
                {
                    if (t.IsFaulted && t.Exception != null)
                    {
                        Debug.WriteLine($"Dispatcher invocation faulted: {t.Exception.Message}");
                    }
                },
                TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (Exception ex) when (ex is TaskCanceledException or ObjectDisposedException)
        {
            // Dispatcher shutting down - drop ALL pending refreshes
            Debug.WriteLine("Dispatcher unavailable, dropping all pending refresh notifications");
            lock (_lock)
            {
                _pendingRefresh = RefreshScope.None;
                _dispatchScheduled = false;
            }
        }
    }
}
