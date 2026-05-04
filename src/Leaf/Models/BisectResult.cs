namespace Leaf.Models;

/// <summary>
/// Outcome of a bisect mutation (<c>start</c> / <c>good</c> / <c>bad</c> /
/// <c>skip</c> / <c>reset</c>). When <see cref="IsTerminating"/> is true
/// the bisect has converged and <see cref="FirstBadSha"/> identifies the
/// first commit where the regression appeared; otherwise the user
/// continues testing with the new <see cref="State"/>.
/// </summary>
public sealed class BisectResult
{
    /// <summary>True when the underlying git command exited cleanly.</summary>
    public bool Success { get; init; }

    /// <summary>
    /// True when this verdict ended the bisect — git printed
    /// "&lt;sha&gt; is the first bad commit" and there is nothing more to test.
    /// </summary>
    public bool IsTerminating { get; init; }

    /// <summary>
    /// Full SHA of the first bad commit when <see cref="IsTerminating"/>
    /// is true; null otherwise.
    /// </summary>
    public string? FirstBadSha { get; init; }

    /// <summary>
    /// Snapshot of the bisect state after the mutation. When
    /// <see cref="IsTerminating"/> is true this is the converged state
    /// (steps remaining = 0) so the UI banner can transition smoothly.
    /// </summary>
    public BisectState? State { get; init; }

    /// <summary>git's stderr text on hard failure.</summary>
    public string? ErrorMessage { get; init; }
}
