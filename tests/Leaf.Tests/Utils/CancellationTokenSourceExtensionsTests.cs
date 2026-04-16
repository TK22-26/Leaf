using FluentAssertions;
using Leaf.Utils;
using Xunit;

namespace Leaf.Tests.Utils;

public class CancellationTokenSourceExtensionsTests
{
    [Fact]
    public void ReplaceAndCancel_NullField_InstallsFreshAndReturnsIt()
    {
        CancellationTokenSource? field = null;

        var result = CancellationTokenSourceExtensions.ReplaceAndCancel(ref field);

        field.Should().BeSameAs(result);
        result.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void ReplaceAndCancel_ExistingField_CancelsAndDisposesPrevious()
    {
        var previous = new CancellationTokenSource();
        var field = previous;
        var previousToken = previous.Token;

        var fresh = CancellationTokenSourceExtensions.ReplaceAndCancel(ref field);

        field.Should().BeSameAs(fresh);
        field.Should().NotBeSameAs(previous);
        previousToken.IsCancellationRequested.Should().BeTrue(
            "the prior CTS must have been cancelled before being replaced");

        // Disposed CTSs throw ObjectDisposedException from Cancel(). If the
        // previous instance is NOT disposed, this call would succeed silently;
        // the throw proves the dispose happened.
        var disposalHappened = false;
        try { previous.Cancel(); }
        catch (ObjectDisposedException) { disposalHappened = true; }
        disposalHappened.Should().BeTrue("the previous CTS must have been disposed");
    }

    [Fact]
    public void ReplaceAndCancel_PreviouslyDisposed_DoesNotThrow()
    {
        // Simulate a race where another caller already disposed the CTS.
        var previous = new CancellationTokenSource();
        previous.Dispose();
        var field = previous;

        var act = () => CancellationTokenSourceExtensions.ReplaceAndCancel(ref field);

        act.Should().NotThrow("the helper must swallow ObjectDisposedException from a racing caller");
        field.Should().NotBeSameAs(previous);
    }

    [Fact]
    public void DisposeAndClear_NullField_DoesNothing()
    {
        CancellationTokenSource? field = null;

        var act = () => CancellationTokenSourceExtensions.DisposeAndClear(ref field);

        act.Should().NotThrow();
        field.Should().BeNull();
    }

    [Fact]
    public void DisposeAndClear_ExistingField_CancelsDisposesAndNulls()
    {
        var cts = new CancellationTokenSource();
        var field = cts;
        var token = cts.Token;

        CancellationTokenSourceExtensions.DisposeAndClear(ref field);

        field.Should().BeNull("the field must be cleared for repeated calls to be no-ops");
        token.IsCancellationRequested.Should().BeTrue();

        var disposalHappened = false;
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { disposalHappened = true; }
        disposalHappened.Should().BeTrue();
    }

    [Fact]
    public void DisposeAndClear_CalledTwice_IsIdempotent()
    {
        CancellationTokenSource? field = new CancellationTokenSource();

        CancellationTokenSourceExtensions.DisposeAndClear(ref field);
        var act = () => CancellationTokenSourceExtensions.DisposeAndClear(ref field);

        act.Should().NotThrow();
        field.Should().BeNull();
    }

    [Fact]
    public void ReplaceAndCancel_RepeatedSerialCalls_DoNotLeak()
    {
        // We can't observe `Dispose()` on CTS directly (no event, no subclass
        // override), but we can prove no leak by asserting every previously
        // returned instance is cancelled after the next replace — which
        // implies it was touched again and therefore the helper did reach it.
        // Combined with the code inspection that Cancel and Dispose happen
        // together inside the helper, this covers the leak contract.
        CancellationTokenSource? field = null;
        var previousSources = new List<CancellationTokenSource>();

        for (int i = 0; i < 100; i++)
        {
            var fresh = CancellationTokenSourceExtensions.ReplaceAndCancel(ref field);
            previousSources.Add(fresh);
        }

        // Every previously installed CTS (except the current one) must have
        // been cancelled by a later ReplaceAndCancel call.
        for (int i = 0; i < previousSources.Count - 1; i++)
        {
            // IsCancellationRequested remains readable even after disposal of
            // the owning CTS, so this assertion is safe.
            previousSources[i].IsCancellationRequested.Should().BeTrue(
                $"CTS at index {i} should have been cancelled when the {i + 1}th ReplaceAndCancel ran");
        }
        // The most recent one is still live and shouldn't be cancelled yet.
        previousSources[^1].IsCancellationRequested.Should().BeFalse();

        CancellationTokenSourceExtensions.DisposeAndClear(ref field);
        previousSources[^1].IsCancellationRequested.Should().BeTrue(
            "DisposeAndClear must also cancel before disposing");
    }

    [Fact]
    public async Task ReplaceAndCancel_ConcurrentCallers_FieldEndsUpWithOnlyFinalInstanceLive()
    {
        // Under concurrent replacement, only the final winner must remain
        // un-cancelled. Every loser gets cancelled+disposed. We don't try to
        // read the Token of a racing-losing instance (it may be disposed from
        // underneath us — that's the whole reason the helper exists).
        const int iterations = 500;
        const int threads = 8;
        CancellationTokenSource? field = null;

        var barrier = new Barrier(threads);
        var tasks = new Task[threads];
        for (int t = 0; t < threads; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < iterations; i++)
                {
                    CancellationTokenSourceExtensions.ReplaceAndCancel(ref field);
                }
            });
        }

        await Task.WhenAll(tasks);

        // Field must be non-null (every call set it to a fresh instance) and
        // the final installed CTS must still be live (no one has cancelled it
        // after the race ended).
        field.Should().NotBeNull();
        field!.IsCancellationRequested.Should().BeFalse();

        // Cleanup for the test — final CTS must dispose cleanly with no throw.
        var finalCleanup = () => CancellationTokenSourceExtensions.DisposeAndClear(ref field);
        finalCleanup.Should().NotThrow();
    }
}
