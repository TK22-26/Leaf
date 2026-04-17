using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Detects which of Leaf's preset external tools are installed on the
/// current machine. Used by the Settings UI to show/hide dropdown entries
/// and by the launcher to resolve a preset <see cref="ExternalTool.Command"/>
/// to an absolute path when it isn't on PATH.
/// </summary>
public interface IExternalToolDetectorService
{
    /// <summary>
    /// Returns the distinct preset names (e.g. <c>"vscode"</c>, <c>"bcomp"</c>)
    /// that resolve to an executable either on PATH or at one of the
    /// preset's known install paths. Results are cached until
    /// <see cref="InvalidateCache"/> is called.
    /// </summary>
    Task<IReadOnlySet<string>> GetInstalledToolNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a tool's <see cref="ExternalTool.Command"/> to an absolute
    /// path. Returns null if nothing matched — caller decides whether to
    /// fall back or report an error.
    /// </summary>
    Task<string?> ResolveCommandPathAsync(ExternalTool tool, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops cached detection state so the next call re-scans. Call this
    /// after the user installs a tool or edits PATH while Leaf is running.
    /// </summary>
    void InvalidateCache();
}
