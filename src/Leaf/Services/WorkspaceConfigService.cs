using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Reads / writes the per-repo workspace settings (mode + pinned tile
/// order) under <c>[leaf "workspace"]</c> in <c>.git/config</c>. Mirrors
/// the GitFlow service's pattern of touching git's own config rather
/// than a Leaf-private file — the values live with the working copy and
/// any other Git client can read them with <c>git config</c>.
/// </summary>
public class WorkspaceConfigService : IWorkspaceConfigService
{
    // Stable keys. Don't rename without a migration.
    private const string ModeKey = "leaf.workspace.mode";
    private const string PinnedOrderKey = "leaf.workspace.pinnedorder";
    private const string PausedMergeTargetKey = "leaf.workspace.pausedmergetarget";
    private const string PausedMergeTypeKey = "leaf.workspace.pausedmergetype";
    private const string PausedMergePathKey = "leaf.workspace.pausedmergepath";

    private readonly IGitCommandRunner _runner;

    public WorkspaceConfigService(IGitCommandRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    /// <inheritdoc />
    public async Task<WorkspaceMode> GetModeAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        var value = await ReadConfigAsync(repoPath, ModeKey, cancellationToken);
        // Anything other than the explicit "grid" string maps to Single —
        // a corrupt / hand-edited value should fall back safely, not crash.
        return string.Equals(value, "grid", StringComparison.OrdinalIgnoreCase)
            ? WorkspaceMode.Grid
            : WorkspaceMode.Single;
    }

    /// <inheritdoc />
    public async Task SetModeAsync(string repoPath, WorkspaceMode mode, CancellationToken cancellationToken = default)
    {
        var value = mode == WorkspaceMode.Grid ? "grid" : "single";
        await WriteConfigAsync(repoPath, ModeKey, value, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetPinnedTileOrderAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        var value = await ReadConfigAsync(repoPath, PinnedOrderKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(value)) return [];
        // CSV is plenty for tile order — submodule paths can't contain
        // commas under any sensible workflow, but we still trim each
        // entry so a hand-edited config with spaces around the commas
        // round-trips cleanly.
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    /// <inheritdoc />
    public async Task SetPinnedTileOrderAsync(string repoPath, IReadOnlyList<string> submodulePaths, CancellationToken cancellationToken = default)
    {
        if (submodulePaths is null || submodulePaths.Count == 0)
        {
            await UnsetConfigAsync(repoPath, PinnedOrderKey, cancellationToken);
            return;
        }
        var csv = string.Join(",", submodulePaths);
        await WriteConfigAsync(repoPath, PinnedOrderKey, csv, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(string Target, string MergeType, string PausedAtRelativePath)?> GetPausedMergeAsync(
        string repoPath, CancellationToken cancellationToken = default)
    {
        var target = await ReadConfigAsync(repoPath, PausedMergeTargetKey, cancellationToken);
        if (string.IsNullOrEmpty(target)) return null;
        var mergeType = await ReadConfigAsync(repoPath, PausedMergeTypeKey, cancellationToken) ?? "Normal";
        var path = await ReadConfigAsync(repoPath, PausedMergePathKey, cancellationToken);
        if (string.IsNullOrEmpty(path)) return null;
        return (target, mergeType, path);
    }

    /// <inheritdoc />
    public async Task SetPausedMergeAsync(
        string repoPath, string target, string mergeType, string pausedAtRelativePath,
        CancellationToken cancellationToken = default)
    {
        await WriteConfigAsync(repoPath, PausedMergeTargetKey, target, cancellationToken);
        await WriteConfigAsync(repoPath, PausedMergeTypeKey, mergeType, cancellationToken);
        await WriteConfigAsync(repoPath, PausedMergePathKey, pausedAtRelativePath, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ClearPausedMergeAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        await UnsetConfigAsync(repoPath, PausedMergeTargetKey, cancellationToken);
        await UnsetConfigAsync(repoPath, PausedMergeTypeKey, cancellationToken);
        await UnsetConfigAsync(repoPath, PausedMergePathKey, cancellationToken);
    }

    /// <summary>
    /// <c>git config --local --get &lt;key&gt;</c>. Returns null on any
    /// non-zero exit (key absent, repo not initialised, etc.). git
    /// returns exit code 1 for missing keys, not an error — we treat
    /// that as "not set".
    /// </summary>
    private async Task<string?> ReadConfigAsync(string repoPath, string key, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _runner.RunAsync(
                repoPath,
                ["config", "--local", "--get", key],
                cancellationToken: cancellationToken);
            if (!result.Success) return null;
            return result.StandardOutput.Trim();
        }
        catch (Exception ex)
        {
            Log.Info("Workspace", $"ReadConfig {key} failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// <c>git config --local &lt;key&gt; &lt;value&gt;</c>. Logged-and-swallowed
    /// on failure — the UI shouldn't break because the preference can't
    /// be persisted (read-only checkout, missing .git, etc.). The next
    /// successful write reconciles automatically.
    /// </summary>
    private async Task WriteConfigAsync(string repoPath, string key, string value, CancellationToken cancellationToken)
    {
        try
        {
            await _runner.RunAsync(
                repoPath,
                ["config", "--local", key, value],
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Warn("Workspace", $"WriteConfig {key} failed: {ex.Message}");
        }
    }

    /// <summary>
    /// <c>git config --local --unset &lt;key&gt;</c>. Exits non-zero when
    /// the key was already absent; we ignore that case so the call is
    /// idempotent for the caller.
    /// </summary>
    private async Task UnsetConfigAsync(string repoPath, string key, CancellationToken cancellationToken)
    {
        try
        {
            await _runner.RunAsync(
                repoPath,
                ["config", "--local", "--unset", key],
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Info("Workspace", $"UnsetConfig {key} failed: {ex.Message}");
        }
    }
}
