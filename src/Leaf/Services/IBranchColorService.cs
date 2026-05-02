using System.Windows.Media;
using Leaf.Graph;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Per-repository resolver and authority for branch graph colours. Owns
/// three layers of precedence:
/// <list type="number">
/// <item><description>User overrides — explicit colours assigned via the
/// right-click "Change Colour…" picker, persisted in
/// <see cref="RepositoryInfo.BranchColorOverrides"/>.</description></item>
/// <item><description>GitFlow semantic colours — main/develop/feature/release/
/// hotfix/support, supplied by the active GitFlow config when the repo
/// has GitFlow initialised.</description></item>
/// <item><description>Active palette — the user's chosen palette (or the
/// shipped default), indexed by a stable hash of the normalised branch
/// name.</description></item>
/// </list>
///
/// <para>One instance per loaded repository; <see cref="ViewModels.GitGraphViewModel"/>
/// constructs and disposes alongside its <c>GraphBuilder</c>. Implements
/// <see cref="IBranchColorResolver"/> so existing canvas code resolves
/// against this service unchanged.</para>
/// </summary>
public interface IBranchColorService : IBranchColorResolver
{
    /// <summary>
    /// The palette currently in effect (after applying the user's chosen id
    /// from <see cref="AppSettings.DefaultBranchColorPaletteId"/>). Changes
    /// when the user picks a different palette in settings or edits the
    /// active custom palette.
    /// </summary>
    BranchColorPalette ActivePalette { get; }

    /// <summary>
    /// Returns the resolved <see cref="Color"/> (not Brush) for the branch.
    /// Used by the colour-picker UI to seed its initial selection.
    /// </summary>
    Color GetColor(string branchName);

    /// <summary>
    /// True if the user has explicitly overridden the colour for this
    /// branch. Used by the right-click menu to enable/disable the
    /// "Reset to Auto" item.
    /// </summary>
    bool HasOverride(string branchName);

    /// <summary>
    /// True if the repo has any user overrides at all. Used by the
    /// "Reset all colours" menu item and the settings UI.
    /// </summary>
    bool HasAnyOverrides { get; }

    /// <summary>
    /// Set or replace a per-branch colour override. Persists to
    /// <see cref="RepositoryInfo.BranchColorOverrides"/> via
    /// <see cref="SettingsService.SaveRepositories"/> and fires
    /// <see cref="ColorsChanged"/>.
    /// </summary>
    void SetOverride(string branchName, Color color);

    /// <summary>
    /// Remove the override for one branch. Persists and fires
    /// <see cref="ColorsChanged"/>. No-op if no override exists.
    /// </summary>
    void ClearOverride(string branchName);

    /// <summary>
    /// Remove every override on this repo. Persists and fires
    /// <see cref="ColorsChanged"/>. No-op if no overrides exist.
    /// </summary>
    void ClearAllOverrides();

    /// <summary>
    /// Fired whenever the resolved colour for any branch may have changed —
    /// after an override change, after a palette change, or after the
    /// custom-palette set is edited. The graph view subscribes and refreshes
    /// node colours in place; the canvas simply invalidates its visual.
    /// </summary>
    event EventHandler? ColorsChanged;
}
