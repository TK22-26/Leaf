using System.Windows.Threading;

namespace Leaf.Services;

/// <summary>
/// WPF implementation of <see cref="IDispatcherService"/>.
/// Wraps a WPF Dispatcher for UI thread marshalling.
/// </summary>
/// <remarks>
/// The Dispatcher is passed via constructor (NOT accessed via Application.Current).
/// This makes the service testable - tests can provide a mock or test dispatcher.
/// </remarks>
public class DispatcherService : IDispatcherService
{
    private readonly Dispatcher _dispatcher;

    /// <summary>
    /// Creates a new DispatcherService wrapping the provided dispatcher.
    /// </summary>
    /// <param name="dispatcher">The WPF dispatcher to wrap.</param>
    public DispatcherService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <inheritdoc />
    public bool CheckAccess() => _dispatcher.CheckAccess();

    /// <inheritdoc />
    public void Invoke(Action action)
    {
        if (CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.Invoke(action);
        }
    }

    /// <inheritdoc />
    public async Task InvokeAsync(Action action)
    {
        if (CheckAccess())
        {
            action();
        }
        else
        {
            await _dispatcher.InvokeAsync(action);
        }
    }

    /// <inheritdoc />
    public async Task<T> InvokeAsync<T>(Func<T> func)
    {
        if (CheckAccess())
        {
            return func();
        }
        else
        {
            return await _dispatcher.InvokeAsync(func);
        }
    }
}

/// <summary>
/// Test implementation of <see cref="IDispatcherService"/>.
/// Executes all actions synchronously on the calling thread.
/// </summary>
public class TestDispatcherService : IDispatcherService
{
    /// <inheritdoc />
    public bool CheckAccess() => true;

    /// <inheritdoc />
    public void Invoke(Action action) => action();

    /// <inheritdoc />
    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
}
