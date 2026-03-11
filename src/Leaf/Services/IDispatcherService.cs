using System.Windows.Threading;

namespace Leaf.Services;

/// <summary>
/// Abstraction for UI thread dispatching.
/// Enables unit testing without WPF runtime.
/// </summary>
public interface IDispatcherService
{
    /// <summary>
    /// Checks whether the calling thread is the UI thread.
    /// </summary>
    /// <returns>True if called from the UI thread.</returns>
    bool CheckAccess();

    /// <summary>
    /// Executes an action synchronously on the UI thread.
    /// If already on UI thread, executes immediately.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    void Invoke(Action action);

    /// <summary>
    /// Executes an action asynchronously on the UI thread.
    /// If already on UI thread, executes immediately.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <returns>A task that completes when the action has executed.</returns>
    Task InvokeAsync(Action action);

    /// <summary>
    /// Executes an action asynchronously on the UI thread at the specified dispatcher priority.
    /// If already on the UI thread, executes immediately.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="priority">The dispatcher priority to use.</param>
    /// <returns>A task that completes when the action has executed.</returns>
    Task InvokeAsync(Action action, DispatcherPriority priority);

    /// <summary>
    /// Executes a function asynchronously on the UI thread and returns the result.
    /// If already on UI thread, executes immediately.
    /// </summary>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="func">The function to execute.</param>
    /// <returns>A task containing the result of the function.</returns>
    Task<T> InvokeAsync<T>(Func<T> func);

    /// <summary>
    /// Executes a function asynchronously on the UI thread at the specified dispatcher priority and returns the result.
    /// If already on the UI thread, executes immediately.
    /// </summary>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="func">The function to execute.</param>
    /// <param name="priority">The dispatcher priority to use.</param>
    /// <returns>A task containing the result of the function.</returns>
    Task<T> InvokeAsync<T>(Func<T> func, DispatcherPriority priority);
}
