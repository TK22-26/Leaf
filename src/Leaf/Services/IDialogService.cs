using System.Windows;

namespace Leaf.Services;

/// <summary>
/// Abstraction for showing dialog boxes and messages.
/// Enables testability by allowing mock implementations.
/// </summary>
/// <remarks>
/// All methods marshal to the UI thread via IDispatcherService.
/// Safe to call from any thread.
/// </remarks>
public interface IDialogService
{
    /// <summary>
    /// Shows a confirmation dialog with Yes/No buttons.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="title">The dialog title.</param>
    /// <returns>True if user clicked Yes, false otherwise.</returns>
    Task<bool> ShowConfirmationAsync(string message, string title);

    /// <summary>
    /// Shows a message dialog with custom buttons.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="title">The dialog title.</param>
    /// <param name="buttons">The buttons to show.</param>
    /// <returns>The result indicating which button was clicked.</returns>
    Task<MessageBoxResult> ShowMessageAsync(string message, string title, MessageBoxButton buttons);

    /// <summary>
    /// Shows an informational message dialog.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="title">The dialog title.</param>
    Task ShowInformationAsync(string message, string title);

    /// <summary>
    /// Shows an error message dialog.
    /// </summary>
    /// <param name="message">The error message to display.</param>
    /// <param name="title">The dialog title.</param>
    Task ShowErrorAsync(string message, string title);

    /// <summary>
    /// Shows an input dialog for getting text input from the user.
    /// </summary>
    /// <param name="prompt">The prompt message.</param>
    /// <param name="title">The dialog title.</param>
    /// <param name="defaultValue">Optional default value.</param>
    /// <returns>The entered text, or null if cancelled.</returns>
    Task<string?> ShowInputAsync(string prompt, string title, string? defaultValue = null);

    /// <summary>
    /// Shows a constructed <see cref="Window"/> as a modal dialog. Sets
    /// the owner to the main application window before showing so
    /// callers never touch Window/Application directly.
    /// </summary>
    /// <param name="dialog">The dialog window (already populated with DataContext, dimensions, etc.).</param>
    /// <returns>True if <see cref="Window.DialogResult"/> was true, false otherwise (including null / user-closed).</returns>
    Task<bool> ShowDialogAsync(Window dialog);
}
