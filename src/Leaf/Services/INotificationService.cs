using System.Windows.Input;
using Leaf.Models;

namespace Leaf.Services;

public enum NotificationType { Error, Warning, Information, Success }

/// <summary>
/// A labeled action button that can be displayed alongside a notification.
/// </summary>
public class NotificationAction(string label, Action callback)
{
    public string Label { get; } = label;
    public Action Callback { get; } = callback;
}

public class NotificationMessage
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public NotificationType Type { get; init; } = NotificationType.Error;
    public ICommand? ClickCommand { get; init; }
    public object? ClickCommandParameter { get; init; }
    public IReadOnlyList<NotificationAction> Actions { get; init; } = [];
}

/// <summary>
/// Fires toast notifications. A <c>category</c> argument lets the user
/// filter classes of toast from Settings; passing <c>null</c> (the
/// default) bypasses the filter and always shows the message — used by
/// error toasts so failures can never be muted accidentally.
/// </summary>
public interface INotificationService
{
    event Action<NotificationMessage>? NotificationRequested;

    /// <param name="category">
    /// When non-null, the user's Notifications settings decide whether the
    /// toast is rendered. When <c>null</c>, the toast always shows
    /// (errors and other un-mutable messages take this path).
    /// </param>
    void Show(string title, string description, NotificationType type = NotificationType.Error, NotificationCategory? category = null);

    void Show(string title, string description, NotificationType type, NotificationCategory? category, params NotificationAction[] actions);

    void Show(string title, string description, NotificationType type, NotificationCategory? category, ICommand clickCommand, object? clickCommandParameter = null, params NotificationAction[] actions);
}
