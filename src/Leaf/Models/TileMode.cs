namespace Leaf.Models;

/// <summary>
/// Per-tile UI state for the workspace grid. Drives the
/// <see cref="Leaf.Views.SubmoduleTile"/> body's content switch — the
/// graph in <see cref="Normal"/>, the inline commit composer in
/// <see cref="Composing"/>.
/// </summary>
/// <remarks>
/// Set by <see cref="Leaf.ViewModels.WorkspaceViewModel.CommitAllAsync"/>
/// when it kicks off review mode (every dirty tile transitions to
/// <see cref="Composing"/>; clean tiles stay <see cref="Normal"/>),
/// and by the per-tile Commit / Cancel buttons on the way back to
/// <see cref="Normal"/>.
/// </remarks>
public enum TileMode
{
    /// <summary>Graph + working changes (the default).</summary>
    Normal,

    /// <summary>Inline commit composer is open with an AI-generated draft.</summary>
    Composing,
}
