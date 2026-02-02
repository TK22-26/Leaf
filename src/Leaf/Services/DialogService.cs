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

    public DialogService(IDispatcherService dispatcher, IWindowService windowService)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _windowService = windowService ?? throw new ArgumentNullException(nameof(windowService));
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
    public async Task ShowErrorAsync(string message, string title)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            var owner = _windowService.GetMainWindow();
            MessageBox.Show(
                owner,
                message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        });
    }

    /// <inheritdoc />
    public async Task<T?> ShowDialogAsync<T>(object viewModel) where T : class
    {
        // Note: This is a simplified implementation.
        // A full implementation would need a dialog type registry or convention-based resolution.
        // For now, we return null - this method will be enhanced as dialogs are migrated.
        return await _dispatcher.InvokeAsync<T?>(() =>
        {
            // TODO: Implement dialog window creation based on ViewModel type
            // This will be enhanced in Phase 3 when dialogs are refactored
            return null;
        });
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
    public Task ShowErrorAsync(string message, string title)
    {
        ShownMessages.Add((message, title));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<T?> ShowDialogAsync<T>(object viewModel) where T : class
    {
        return Task.FromResult<T?>(null);
    }

    /// <inheritdoc />
    public Task<string?> ShowInputAsync(string prompt, string title, string? defaultValue = null)
    {
        ShownMessages.Add((prompt, title));
        return Task.FromResult(InputResult);
    }
}
