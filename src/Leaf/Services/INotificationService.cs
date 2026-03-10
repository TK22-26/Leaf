namespace Leaf.Services;

public enum NotificationType { Error, Warning, Information, Success }

public class NotificationMessage
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public NotificationType Type { get; init; } = NotificationType.Error;
}

public interface INotificationService
{
    event Action<NotificationMessage>? NotificationRequested;
    void Show(string title, string description, NotificationType type = NotificationType.Error);
}
