using System.Windows.Input;

namespace Leaf.Services;

public class NotificationService : INotificationService
{
    private readonly IDispatcherService _dispatcher;

    public event Action<NotificationMessage>? NotificationRequested;

    public NotificationService(IDispatcherService dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public void Show(string title, string description, NotificationType type = NotificationType.Error)
    {
        var message = new NotificationMessage { Title = title, Description = description, Type = type };
        _dispatcher.InvokeAsync(() => NotificationRequested?.Invoke(message));
    }

    public void Show(string title, string description, NotificationType type, params NotificationAction[] actions)
    {
        var message = new NotificationMessage
        {
            Title = title,
            Description = description,
            Type = type,
            Actions = actions
        };
        _dispatcher.InvokeAsync(() => NotificationRequested?.Invoke(message));
    }

    public void Show(string title, string description, NotificationType type, ICommand clickCommand, object? clickCommandParameter = null, params NotificationAction[] actions)
    {
        var message = new NotificationMessage
        {
            Title = title,
            Description = description,
            Type = type,
            ClickCommand = clickCommand,
            ClickCommandParameter = clickCommandParameter,
            Actions = actions
        };
        _dispatcher.InvokeAsync(() => NotificationRequested?.Invoke(message));
    }
}
