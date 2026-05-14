using System.Windows;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.Tests.Fakes;

/// <summary>
/// Fake implementation of IDialogService for testing.
/// Allows configuring responses and tracking method calls.
/// </summary>
public class FakeDialogService : IDialogService
{
    // Track method calls. Confirmation/Message/Information now also record
    // the optional suppressionKey + icon so suppression-aware tests can
    // assert which key the production code passed.
    public List<(string Message, string Title, string? SuppressionKey, FluentMessageBoxIcon Icon)> ConfirmationCalls { get; } = [];
    public List<(string Message, string Title, MessageBoxButton Buttons, FluentMessageBoxIcon Icon, string? SuppressionKey)> MessageCalls { get; } = [];
    public List<(string Message, string Title, string? SuppressionKey)> InformationCalls { get; } = [];
    public List<(string Message, string Title)> ErrorCalls { get; } = [];
    public List<(string Prompt, string Title, string? DefaultValue)> InputCalls { get; } = [];

    // Track modal dialogs shown via ShowDialogAsync(Window).
    public List<Window> ShownDialogs { get; } = [];

    // Configure responses
    public bool ConfirmationResult { get; set; } = true;
    public MessageBoxResult MessageResult { get; set; } = MessageBoxResult.OK;
    public string? InputResult { get; set; } = null;
    public bool DialogResult { get; set; } = true;

    public Task<bool> ShowConfirmationAsync(
        string message,
        string title,
        string? suppressionKey = null,
        FluentMessageBoxIcon icon = FluentMessageBoxIcon.Question)
    {
        ConfirmationCalls.Add((message, title, suppressionKey, icon));
        return Task.FromResult(ConfirmationResult);
    }

    public Task<MessageBoxResult> ShowMessageAsync(
        string message,
        string title,
        MessageBoxButton buttons,
        FluentMessageBoxIcon icon = FluentMessageBoxIcon.Information,
        string? suppressionKey = null)
    {
        MessageCalls.Add((message, title, buttons, icon, suppressionKey));
        return Task.FromResult(MessageResult);
    }

    public Task ShowInformationAsync(string message, string title, string? suppressionKey = null)
    {
        InformationCalls.Add((message, title, suppressionKey));
        return Task.CompletedTask;
    }

    public Task ShowErrorToastAsync(string message, string title)
    {
        ErrorCalls.Add((message, title));
        return Task.CompletedTask;
    }

    public Task<string?> ShowInputAsync(string prompt, string title, string? defaultValue = null)
    {
        InputCalls.Add((prompt, title, defaultValue));
        return Task.FromResult(InputResult);
    }

    public Task<bool> ShowDialogAsync(Window dialog)
    {
        ShownDialogs.Add(dialog);
        return Task.FromResult(DialogResult);
    }
}
