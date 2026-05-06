using System.Windows;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Abstraction for showing dialog boxes, toasts, and modal windows.
/// Enables testability by allowing mock implementations.
/// </summary>
/// <remarks>
/// <para>All methods marshal to the UI thread via IDispatcherService.
/// Safe to call from any thread.</para>
///
/// <para><b>Feedback policy:</b></para>
/// <list type="bullet">
/// <item><description><b>Toast</b> (<see cref="ShowErrorToastAsync"/>
/// for failures, <see cref="INotificationService.Show"/> with
/// <see cref="NotificationType.Success"/>/<see cref="NotificationType.Information"/>/<see cref="NotificationType.Warning"/>
/// for everything else) — terminal feedback for user-initiated and
/// background operations alike. Successful completions, recoverable
/// failures, and informational outcomes all surface as toasts so they
/// don't get lost. The <c>IsBusy</c> spinner covers in-flight
/// progress; toasts cover terminal state.</description></item>
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
    /// <param name="suppressionKey">
    /// Optional stable key (e.g. <c>"branch.forceDelete"</c>). When set, the
    /// dialog surfaces a "Don't show this again" checkbox; if the user has
    /// previously checked it, the call returns the remembered answer
    /// without rendering UI. Persisted via
    /// <see cref="SettingsService.SetSuppressedAnswer"/>.
    /// </param>
    /// <param name="icon">
    /// Severity glyph for the new Fluent-styled box. Defaults to
    /// <see cref="FluentMessageBoxIcon.Question"/> for a Yes/No prompt.
    /// </param>
    /// <returns>True if user clicked Yes, false otherwise.</returns>
    Task<bool> ShowConfirmationAsync(
        string message,
        string title,
        string? suppressionKey = null,
        FluentMessageBoxIcon icon = FluentMessageBoxIcon.Question);

    /// <summary>
    /// Shows a message dialog with custom buttons.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="title">The dialog title.</param>
    /// <param name="buttons">The buttons to show.</param>
    /// <param name="icon">Severity glyph; defaults to <see cref="FluentMessageBoxIcon.Information"/>.</param>
    /// <param name="suppressionKey">See <see cref="ShowConfirmationAsync"/>.</param>
    /// <returns>The result indicating which button was clicked.</returns>
    Task<MessageBoxResult> ShowMessageAsync(
        string message,
        string title,
        MessageBoxButton buttons,
        FluentMessageBoxIcon icon = FluentMessageBoxIcon.Information,
        string? suppressionKey = null);

    /// <summary>
    /// Shows an informational message dialog.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="title">The dialog title.</param>
    /// <param name="suppressionKey">See <see cref="ShowConfirmationAsync"/>.</param>
    Task ShowInformationAsync(string message, string title, string? suppressionKey = null);

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
