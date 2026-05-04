using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Launches configured external diff/merge tools. The caller is
/// responsible for producing the input files (temp copies of
/// base/local/remote/merged content) and reading back the merged
/// output — the launcher only expands templates and starts the process.
/// </summary>
public interface IExternalToolLauncherService
{
    /// <summary>
    /// Run a two-way diff. <paramref name="localPath"/> is shown on the
    /// left, <paramref name="remotePath"/> on the right. Returns the
    /// process's exit code (0 == tool exited cleanly).
    /// </summary>
    /// <exception cref="InvalidOperationException">The tool's command could not be resolved on disk.</exception>
    Task<int> LaunchDiffAsync(ExternalTool tool, string localPath, string remotePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Run a three-way merge. On return, <paramref name="mergedPath"/>
    /// contains whatever the tool wrote (or didn't — exit-code-aware
    /// callers can tell the user picked cancel).
    /// </summary>
    /// <exception cref="InvalidOperationException">The tool's command could not be resolved on disk.</exception>
    Task<int> LaunchMergeAsync(ExternalTool tool, string basePath, string localPath, string remotePath, string mergedPath, CancellationToken cancellationToken = default);
}
