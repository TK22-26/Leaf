using System.Windows;
using Leaf.Services;

namespace Leaf.Tests.Fakes;

/// <summary>
/// Fake implementation of IDialogService for testing.
/// Allows configuring responses and tracking method calls.
/// </summary>
public class FakeDialogService : IDialogService
{
    // Track method calls
    public List<(string Message, string Title)> ConfirmationCalls { get; } = [];
    public List<(string Message, string Title, MessageBoxButton Buttons)> MessageCalls { get; } = [];
    public List<(string Message, string Title)> InformationCalls { get; } = [];
    public List<(string Message, string Title)> ErrorCalls { get; } = [];
    public List<(string Prompt, string Title, string? DefaultValue)> InputCalls { get; } = [];

    // Configure responses
    public bool ConfirmationResult { get; set; } = true;
    public MessageBoxResult MessageResult { get; set; } = MessageBoxResult.OK;
    public string? InputResult { get; set; } = null;

    public Task<bool> ShowConfirmationAsync(string message, string title)
    {
        ConfirmationCalls.Add((message, title));
        return Task.FromResult(ConfirmationResult);
    }

    public Task<MessageBoxResult> ShowMessageAsync(string message, string title, MessageBoxButton buttons)
    {
        MessageCalls.Add((message, title, buttons));
        return Task.FromResult(MessageResult);
    }

    public Task ShowInformationAsync(string message, string title)
    {
        InformationCalls.Add((message, title));
        return Task.CompletedTask;
    }

    public Task ShowErrorAsync(string message, string title)
    {
        ErrorCalls.Add((message, title));
        return Task.CompletedTask;
    }

    public Task<T?> ShowDialogAsync<T>(object viewModel) where T : class
    {
        return Task.FromResult<T?>(null);
    }

    public Task<string?> ShowInputAsync(string prompt, string title, string? defaultValue = null)
    {
        InputCalls.Add((prompt, title, defaultValue));
        return Task.FromResult(InputResult);
    }
}
