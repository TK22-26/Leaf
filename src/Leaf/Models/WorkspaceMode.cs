namespace Leaf.Models;

/// <summary>
/// View mode for a repository's main pane. <see cref="Single"/> is
/// today's Leaf — one repo at a time with full sidebars and a right
/// detail pane. <see cref="Grid"/> is the submodule-workspace dashboard:
/// the parent repo and every submodule tile side-by-side as a
/// tiled grid of mini git-graphs.
/// </summary>
/// <remarks>
/// Persisted per-repo in <c>.git/config</c> under
/// <c>[leaf "workspace"] mode = single|grid</c> so a user who prefers
/// the grid for a particular monorepo gets it back automatically next
/// time. Repos with no submodules can only be in <see cref="Single"/>
/// — the toggle is hidden.
/// </remarks>
public enum WorkspaceMode
{
    /// <summary>The default — one active repo, full sidebars and right detail pane.</summary>
    Single,

    /// <summary>Tiled grid of the parent plus every submodule, full-bleed.</summary>
    Grid,
}
