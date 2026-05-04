using System;
using System.Threading.Tasks;

namespace Leaf.Services;

/// <summary>
/// Centralised exception handling for <c>async void</c> event handlers and
/// fire-and-forget <see cref="Task"/> sites.
///
/// <para>
/// WPF event handlers must be <c>async void</c> (the event signature requires
/// <c>void</c>). An unhandled exception in such a method escapes to the
/// dispatcher and crashes the app. This helper turns that hard failure into
/// a logged diagnostic and — for user-visible actions — a toast notification,
/// matching plan §1.3 / §1.4 (Option B with the settings toggle).
/// </para>
///
/// <para>
/// <b>Usage:</b>
/// <code>
/// private async void SomeButton_Click(object sender, RoutedEventArgs e)
/// {
///     try
///     {
///         await DoWorkAsync();
///     }
///     catch (Exception ex)
///     {
///         AsyncErrorHandler.Handle(ex, nameof(SomeButton_Click), isUserAction: true);
///     }
/// }
///
/// DoWorkAsync().FireAndForget(nameof(DoWorkAsync), isUserAction: true);
/// </code>
/// </para>
/// </summary>
public static class AsyncErrorHandler
{
    private static INotificationService? _notifications;
    private static Func<bool>? _showBackgroundErrors;

    /// <summary>
    /// Wires the handler at app startup. Both callbacks must survive for the
    /// lifetime of the process — the notification service is kept for toast
    /// delivery; <paramref name="showBackgroundErrors"/> is evaluated per
    /// failure so toggling the setting at runtime takes effect immediately.
    /// </summary>
    public static void Init(INotificationService notifications, Func<bool> showBackgroundErrors)
    {
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _showBackgroundErrors = showBackgroundErrors ?? throw new ArgumentNullException(nameof(showBackgroundErrors));
    }

    /// <summary>
    /// Reports an exception caught in an <c>async void</c> handler or similar
    /// context. <see cref="OperationCanceledException"/> is intentionally
    /// swallowed — cancellation is not a failure.
    /// </summary>
    /// <param name="ex">The exception to handle.</param>
    /// <param name="context">Short identifier for the originating site (e.g. <c>nameof(Button_Click)</c>).</param>
    /// <param name="isUserAction">
    /// <c>true</c> for button clicks, menu items, dialog confirmations and
    /// other user-initiated flows — a toast will always be shown.
    /// <c>false</c> for passive background work (tooltips, mouse-move handlers,
    /// auto-fetch, lazy loading) — a toast is shown only if the
    /// <c>ShowBackgroundOperationErrors</c> setting is enabled.
    /// </param>
    public static void Handle(Exception ex, string context, bool isUserAction)
    {
        if (ex is OperationCanceledException) return;

        // Log first; any failure inside the logger is already swallowed by
        // Log.Flush so no extra guard is needed here.
        Log.Error(context, $"Unhandled exception: {ex.Message}", ex);

        if (!ShouldShowToast(isUserAction)) return;

        try
        {
            _notifications?.Show(
                title: isUserAction ? "Operation failed" : "Background task failed",
                description: FormatUserMessage(ex, context),
                type: NotificationType.Error);
        }
        catch (Exception notifyEx)
        {
            // The error handler itself must never crash the app. Log the
            // failure and move on — the original exception is already logged.
            Log.Error("AsyncErrorHandler", $"Notification failed while reporting {context}: {notifyEx.Message}", notifyEx);
        }
    }

    /// <summary>
    /// Attaches a fault handler to a fire-and-forget task. Equivalent to
    /// wrapping the task with a try/catch that calls <see cref="Handle"/>.
    /// Safe to call on a task that has already faulted.
    /// </summary>
    /// <param name="task">The task to observe.</param>
    /// <param name="context">Short identifier for the originating site.</param>
    /// <param name="isUserAction">See <see cref="Handle"/>.</param>
    public static void FireAndForget(this Task task, string context, bool isUserAction = false)
    {
        if (task == null) throw new ArgumentNullException(nameof(task));

        // ExecuteSynchronously avoids a thread-pool hop for the common case
        // where the continuation just logs. OnlyOnFaulted means the success
        // path has no continuation overhead at all.
        task.ContinueWith(
            t =>
            {
                // Exception is non-null on faulted tasks by definition.
                var aggregate = t.Exception;
                if (aggregate == null) return;

                foreach (var inner in aggregate.Flatten().InnerExceptions)
                {
                    Handle(inner, context, isUserAction);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static bool ShouldShowToast(bool isUserAction)
    {
        if (isUserAction) return true;
        try
        {
            return _showBackgroundErrors?.Invoke() == true;
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                or NullReferenceException
                                or ObjectDisposedException)
        {
            // Callback raced shutdown — default to silent for background errors.
            System.Diagnostics.Debug.WriteLine($"[AsyncErrorHandler] ShouldShowToast callback failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static string FormatUserMessage(Exception ex, string context)
    {
        // Prefer the exception message for user display; fall back to the
        // context identifier when a message isn't available.
        var message = string.IsNullOrWhiteSpace(ex.Message) ? context : ex.Message;
        return message.Length <= 300 ? message : message[..300] + "…";
    }
}
