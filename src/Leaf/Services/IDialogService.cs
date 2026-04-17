using System.Windows;

namespace Leaf.Services;

/// <summary>
/// Abstraction for showing dialog boxes, toasts, and modal windows.
/// Enables testability by allowing mock implementations.
/// </summary>
/// <remarks>
/// <para>All methods marshal to the UI thread via IDispatcherService.
/// Safe to call from any thread.</para>
///
/// <para><b>Error-feedback policy (plan §3.4):</b></para>
/// <list type="bullet">
/// <item><description><b>Status bar</b> (<c>StatusMessage</c> on the
/// ViewModel, not this service) — progress and completion for a
/// user-initiated operation that is clearly in flight: "Pushing...",
/// "Push complete", "Pushed to 3 of 3 remotes".</description></item>
/// <item><description><b>Toast</b> (<see cref="ShowErrorToastAsync"/>,
/// or <see cref="INotificationService.Show"/> directly from VMs that
/// hold it) — recoverable failures and background-operation errors
/// that don't need immediate acknowledgement: "Push failed: network
/// timeout", "Auto-fetch skipped — host unreachable".</description></item>
/// <item><description><b>Modal</b> (<see cref="ShowMessageAsync"/>
/// with <see cref="MessageBoxButton.OK"/>, or
/// <see cref="ShowConfirmationAsync"/> for yes/no) — errors or prompts
/// that block further action until the user acknowledges, and
/// destructive-action confirmations.</description></item>
/// </list>
/// <para>Historical note: <c>ShowErrorAsync</c> was renamed to
/// <see cref="ShowErrorToastAsync"/> so the name matches the non-blocking
/// toast UX it actually delivers. Modal errors go through
/// <see cref="ShowMessageAsync"/>.</para>
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
    /// Shows an error as a non-blocking toast notification. Use for
    /// recoverable failures where the user doesn't need to acknowledge
    /// before continuing (failed push, failed auto-fetch). For errors
    /// that must block further action, use
    /// <see cref="ShowMessageAsync"/> with <see cref="MessageBoxButton.OK"/>.
    /// </summary>
    /// <param name="message">The error message to display.</param>
    /// <param name="title">The toast title.</param>
    Task ShowErrorToastAsync(string message, string title);

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
