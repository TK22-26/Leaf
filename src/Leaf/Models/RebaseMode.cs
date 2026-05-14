namespace Leaf.Models;

/// <summary>
/// User-selected rebase strategy from <see cref="Leaf.Views.RebaseDialog"/>.
/// </summary>
public enum RebaseMode
{
    /// <summary>
    /// Non-interactive rebase: replay current branch onto target with no plan editor.
    /// </summary>
    Standard,

    /// <summary>
    /// Interactive rebase: open the plan editor against <c>merge-base(HEAD, onto)</c>
    /// so the user can pick / squash / reword commits before they replay.
    /// </summary>
    Interactive
}
