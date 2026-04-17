using System.Windows;

namespace Leaf.Services;

/// <summary>
/// WPF implementation of <see cref="IDialogService"/>.
/// Uses IDispatcherService and IWindowService for testability.
/// </summary>
public class DialogService : IDialogService
{
    private readonly IDispatcherService _dispatcher;
    private readonly IWindowService _windowService;
    private readonly INotificationService _notificationService;

    public DialogService(IDispatcherService dispatcher, IWindowService windowService, INotificationService notificationService)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _windowService = windowService ?? throw new ArgumentNullException(nameof(windowService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    }

    /// <inheritdoc />
    public async Task<bool> ShowConfirmationAsync(string message, string title)
    {
        return await _dispatcher.InvokeAsync(() =>
        {
            var owner = _windowService.GetMainWindow();
            var result = MessageBox.Show(
                owner,
                message,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        });
    }

    /// <inheritdoc />
    public async Task<MessageBoxResult> ShowMessageAsync(string message, string title, MessageBoxButton buttons)
    {
        return await _dispatcher.InvokeAsync(() =>
        {
            var owner = _windowService.GetMainWindow();
            return MessageBox.Show(
                owner,
                message,
                title,
                buttons,
                MessageBoxImage.Information);
        });
    }

    /// <inheritdoc />
    public async Task ShowInformationAsync(string message, string title)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            var owner = _windowService.GetMainWindow();
            MessageBox.Show(
                owner,
                message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        });
    }

    /// <inheritdoc />
    public Task ShowErrorToastAsync(string message, string title)
    {
        _notificationService.Show(title, message, NotificationType.Error);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<string?> ShowInputAsync(string prompt, string title, string? defaultValue = null)
    {
        // Note: WPF doesn't have a built-in input dialog.
        // This is a simplified implementation using MessageBox.
        // A proper implementation would use a custom InputDialog.
        return await _dispatcher.InvokeAsync<string?>(() =>
        {
            // TODO: Implement custom input dialog
            // For now, return null to indicate cancellation
            return null;
        });
    }

    /// <inheritdoc />
    public async Task<bool> ShowDialogAsync(Window dialog)
    {
        return await _dispatcher.InvokeAsync(() =>
        {
            dialog.Owner = _windowService.GetMainWindow();
            return dialog.ShowDialog() == true;
        });
    }
}

/// <summary>
/// Test implementation of <see cref="IDialogService"/>.
/// Returns configurable responses without showing UI.
/// </summary>
public class TestDialogService : IDialogService
{
    /// <summary>
    /// The result to return for ShowConfirmationAsync calls.
    /// </summary>
    public bool ConfirmationResult { get; set; } = true;

    /// <summary>
    /// The result to return for ShowMessageAsync calls.
    /// </summary>
    public MessageBoxResult MessageResult { get; set; } = MessageBoxResult.OK;

    /// <summary>
    /// The result to return for ShowInputAsync calls.
    /// </summary>
    public string? InputResult { get; set; } = null;

    /// <summary>
    /// Record of all messages shown (for test assertions).
    /// </summary>
    public List<(string Message, string Title)> ShownMessages { get; } = new();

    /// <inheritdoc />
    public Task<bool> ShowConfirmationAsync(string message, string title)
    {
        ShownMessages.Add((message, title));
        return Task.FromResult(ConfirmationResult);
    }

    /// <inheritdoc />
    public Task<MessageBoxResult> ShowMessageAsync(string message, string title, MessageBoxButton buttons)
    {
        ShownMessages.Add((message, title));
        return Task.FromResult(MessageResult);
    }

    /// <inheritdoc />
    public Task ShowInformationAsync(string message, string title)
    {
        ShownMessages.Add((message, title));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ShowErrorToastAsync(string message, string title)
    {
        ShownMessages.Add((message, title));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> ShowInputAsync(string prompt, string title, string? defaultValue = null)
    {
        ShownMessages.Add((prompt, title));
        return Task.FromResult(InputResult);
    }

    /// <summary>
    /// The result to return for ShowDialogAsync calls.
    /// </summary>
    public bool DialogResult { get; set; } = true;

    /// <summary>
    /// Record of the dialog windows shown (for test assertions).
    /// </summary>
    public List<Window> ShownDialogs { get; } = new();

    /// <inheritdoc />
    public Task<bool> ShowDialogAsync(Window dialog)
    {
        ShownDialogs.Add(dialog);
        return Task.FromResult(DialogResult);
    }
}
