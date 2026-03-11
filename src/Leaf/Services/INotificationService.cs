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
    public IReadOnlyList<NotificationAction> Actions { get; init; } = [];
}

public interface INotificationService
{
    event Action<NotificationMessage>? NotificationRequested;
    void Show(string title, string description, NotificationType type = NotificationType.Error);
    void Show(string title, string description, NotificationType type, params NotificationAction[] actions);
}
