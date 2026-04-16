namespace Leaf.Utils;

/// <summary>
/// Helpers for the <see cref="CancellationTokenSource"/> lifecycle pattern that
/// otherwise gets re-implemented (and leaked) at every cancel-and-replace site.
///
/// <para>
/// A <see cref="CancellationTokenSource"/> wraps unmanaged resources via its
/// internal kernel timer / handle; the original bug flagged in plan §1.5 was
/// the sequence
/// <code>
/// _cts?.Cancel();
/// _cts = new CancellationTokenSource();
/// </code>
/// which cancels the old one but never disposes it — the handle leaks for the
/// lifetime of the process. These helpers perform the correct sequence in one
/// place.
/// </para>
/// </summary>
public static class CancellationTokenSourceExtensions
{
    /// <summary>
    /// Atomically replaces <paramref name="field"/> with a fresh
    /// <see cref="CancellationTokenSource"/>, cancelling and disposing the
    /// previous instance (if any). Safe to call concurrently — uses
    /// <see cref="Interlocked.Exchange{T}(ref T, T)"/> to avoid double-disposal
    /// races between two callers.
    /// </summary>
    /// <param name="field">The CTS field being replaced.</param>
    /// <returns>The newly-installed <see cref="CancellationTokenSource"/>.</returns>
    public static CancellationTokenSource ReplaceAndCancel(ref CancellationTokenSource? field)
    {
        var fresh = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref field, fresh);
        CancelAndDispose(previous);
        return fresh;
    }

    /// <summary>
    /// Cancels the CTS stored in <paramref name="field"/> (if any), disposes
    /// it, and nulls the field. Used by <c>Dispose</c> / <c>Cleanup</c> paths
    /// so the final in-flight CTS is not leaked when the owning object dies.
    /// </summary>
    /// <param name="field">The CTS field being cleared.</param>
    public static void DisposeAndClear(ref CancellationTokenSource? field)
    {
        var previous = Interlocked.Exchange(ref field, null);
        CancelAndDispose(previous);
    }

    private static void CancelAndDispose(CancellationTokenSource? cts)
    {
        if (cts == null) return;
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Another thread won the race and already disposed it — fine.
        }
        cts.Dispose();
    }
}
