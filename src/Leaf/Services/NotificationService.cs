using System.Windows.Input;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Default <see cref="INotificationService"/>. Marshals to the UI
/// dispatcher and consults <see cref="SettingsService"/> when a category
/// is supplied so the user's "show / hide" preferences are honoured.
/// Calls with a <c>null</c> category bypass the filter — that's the
/// always-show path used by error toasts.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly IDispatcherService _dispatcher;
    private readonly SettingsService _settings;

    public event Action<NotificationMessage>? NotificationRequested;

    public NotificationService(IDispatcherService dispatcher, SettingsService settings)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void Show(string title, string description, NotificationType type = NotificationType.Error, NotificationCategory? category = null)
    {
        if (!ShouldShow(category)) return;
        var message = new NotificationMessage { Title = title, Description = description, Type = type };
        _dispatcher.InvokeAsync(() => NotificationRequested?.Invoke(message));
    }

    public void Show(string title, string description, NotificationType type, NotificationCategory? category, params NotificationAction[] actions)
    {
        if (!ShouldShow(category)) return;
        var message = new NotificationMessage
        {
            Title = title,
            Description = description,
            Type = type,
            Actions = actions
        };
        _dispatcher.InvokeAsync(() => NotificationRequested?.Invoke(message));
    }

    public void Show(string title, string description, NotificationType type, NotificationCategory? category, ICommand clickCommand, object? clickCommandParameter = null, params NotificationAction[] actions)
    {
        if (!ShouldShow(category)) return;
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

    /// <summary>
    /// Returns <c>true</c> when the toast should be rendered. A
    /// <c>null</c> category always passes (errors, legacy callers), a
    /// non-null category reads the corresponding flag on
    /// <see cref="AppSettings"/>.
    /// </summary>
    private bool ShouldShow(NotificationCategory? category)
    {
        if (category is null) return true;
        var s = _settings.LoadSettings();
        return category.Value switch
        {
            NotificationCategory.SyncOperations => s.NotifySyncOperations,
            NotificationCategory.BranchCheckout => s.NotifyBranchCheckout,
            NotificationCategory.BranchAdmin => s.NotifyBranchAdmin,
            NotificationCategory.MergeAndRebase => s.NotifyMergeAndRebase,
            NotificationCategory.GitFlow => s.NotifyGitFlow,
            NotificationCategory.Worktree => s.NotifyWorktree,
            NotificationCategory.Submodule => s.NotifySubmodule,
            NotificationCategory.Stash => s.NotifyStash,
            NotificationCategory.PullRequest => s.NotifyPullRequest,
            NotificationCategory.Patch => s.NotifyPatch,
            NotificationCategory.Repository => s.NotifyRepository,
            NotificationCategory.RemoteConfig => s.NotifyRemoteConfig,
            NotificationCategory.CancelledOperations => s.NotifyCancelledOperations,
            _ => true,
        };
    }
}
