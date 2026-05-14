using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Per-repo workspace preferences persisted to <c>.git/config</c> under
/// <c>[leaf "workspace"]</c>. Kept in git-config rather than the app
/// settings file so each repo carries its own preferred view (a monorepo
/// might want Grid; a single-repo project never does), and so the
/// "Git way" compatibility policy holds — settings travel with the
/// working copy and survive a Leaf reinstall.
/// </summary>
public interface IWorkspaceConfigService
{
    /// <summary>
    /// Read the saved <see cref="WorkspaceMode"/> for <paramref name="repoPath"/>.
    /// Returns <see cref="WorkspaceMode.Single"/> when nothing is set —
    /// new users / new repos get the existing single-view by default.
    /// </summary>
    Task<WorkspaceMode> GetModeAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persist the active <see cref="WorkspaceMode"/> for <paramref name="repoPath"/>.
    /// Idempotent. Failures (read-only filesystem, etc.) are logged but
    /// not thrown — the UI shouldn't break because settings can't be
    /// saved.
    /// </summary>
    Task SetModeAsync(string repoPath, WorkspaceMode mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read the user's pinned tile order — list of submodule paths in the
    /// order they should render after the parent. Submodules not in the
    /// list fall through to alphabetical at the end. Returns empty when
    /// no order has been pinned.
    /// </summary>
    Task<IReadOnlyList<string>> GetPinnedTileOrderAsync(string repoPath, CancellationToken cancellationToken = default);

    /// <summary>Persist the pinned tile order. Empty list clears the key.</summary>
    Task SetPinnedTileOrderAsync(string repoPath, IReadOnlyList<string> submodulePaths, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read a saved workspace-merge pause state. Returns null when no
    /// merge is paused. The triple is (target branch, merge type as
    /// stored string, paused-at submodule path relative to parent).
    /// </summary>
    Task<(string Target, string MergeType, string PausedAtRelativePath)?> GetPausedMergeAsync(
        string repoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persist a workspace-merge pause snapshot so the Continue merge
    /// affordance survives an app restart.
    /// </summary>
    Task SetPausedMergeAsync(
        string repoPath, string target, string mergeType, string pausedAtRelativePath,
        CancellationToken cancellationToken = default);

    /// <summary>Clear any saved workspace-merge pause state. Idempotent.</summary>
    Task ClearPausedMergeAsync(string repoPath, CancellationToken cancellationToken = default);
}
