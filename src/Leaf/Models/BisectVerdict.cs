namespace Leaf.Models;

/// <summary>
/// The verdict the user assigns to the current bisect commit. Maps 1:1
/// to <c>git bisect good</c> / <c>git bisect bad</c> / <c>git bisect skip</c>
/// so the user's mental model carries straight through to git.
/// </summary>
public enum BisectVerdict
{
    /// <summary>Commit is known good — the regression is not present here.</summary>
    Good,

    /// <summary>Commit is known bad — the regression is present here.</summary>
    Bad,

    /// <summary>
    /// Commit is untestable (build broken, fixture missing, etc.). git
    /// reroutes the binary search around skipped commits and reports
    /// them in the final result.
    /// </summary>
    Skip,
}
