namespace Leaf.Models;

/// <summary>
/// Snapshot of an in-progress <c>git bisect</c> session. Built from
/// <c>BISECT_LOG</c> + the current HEAD, surfaced to the bisect banner
/// so the user can see where they are without parsing raw git output.
/// </summary>
public sealed class BisectState
{
    /// <summary>True when a bisect is currently in progress for this repo.</summary>
    public bool IsActive { get; init; }

    /// <summary>Full SHA of the commit git has currently checked out for testing. Empty when inactive.</summary>
    public string CurrentSha { get; init; } = string.Empty;

    /// <summary>Short SHA of <see cref="CurrentSha"/> for display.</summary>
    public string CurrentShortSha { get; init; } = string.Empty;

    /// <summary>Subject of the current commit (first line of the message). Empty when inactive.</summary>
    public string CurrentSubject { get; init; } = string.Empty;

    /// <summary>
    /// Approximate number of commits left to test, as parsed from the
    /// "(roughly K steps)" hint git emits in its <c>Bisecting:</c> line.
    /// <c>null</c> when no hint is available — for example on the first
    /// step (git omits it while it works out the search range), on a
    /// cold open of an in-progress bisect (we read state from disk, not
    /// from a prior command's stdout), or when git's output is in a
    /// locale our regex doesn't recognise. The banner hides the
    /// parenthetical entirely when this is <c>null</c>; showing
    /// <c>(0 steps left)</c> would be misleading.
    /// </summary>
    public int? StepsRemaining { get; init; }

    /// <summary>
    /// Full SHA of the first bad commit when bisect has converged, else null.
    /// </summary>
    public string? FirstBadSha { get; init; }
}
