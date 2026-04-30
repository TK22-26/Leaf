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
    /// Approximate number of commits left to test. Mirrors the
    /// <c>~N steps</c> hint git itself prints. <c>0</c> when the bisect
    /// has converged and <see cref="FirstBadSha"/> is set.
    /// </summary>
    public int StepsRemaining { get; init; }

    /// <summary>
    /// Full SHA of the first bad commit when bisect has converged, else null.
    /// </summary>
    public string? FirstBadSha { get; init; }
}
