using Leaf.Services;

namespace Leaf.Tests.Fakes;

/// <summary>
/// Fake implementation of IDispatcherService for testing.
/// Executes actions synchronously on the calling thread.
/// </summary>
public class FakeDispatcherService : IDispatcherService
{
    // Track method calls
    public int InvokeCallCount { get; private set; }
    public int InvokeAsyncCallCount { get; private set; }

    public bool CheckAccess() => true;

    public void Invoke(Action action)
    {
        InvokeCallCount++;
        action();
    }

    public Task InvokeAsync(Action action)
    {
        InvokeAsyncCallCount++;
        action();
        return Task.CompletedTask;
    }

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        InvokeAsyncCallCount++;
        return Task.FromResult(func());
    }
}
