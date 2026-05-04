using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Reads and writes external-tool configuration in <c>.git/config</c> /
/// <c>~/.gitconfig</c>. Uses Git's own keys
/// (<c>diff.tool</c>/<c>merge.tool</c> for the selected tool name,
/// <c>difftool.&lt;name&gt;.cmd</c>/<c>mergetool.&lt;name&gt;.cmd</c> for
/// the command template) so Leaf's selection also works for
/// <c>git difftool</c> / <c>git mergetool</c>.
/// </summary>
public interface IExternalToolConfigService
{
    /// <summary>
    /// Returns the full <see cref="ExternalTool"/> currently active for the
    /// repo (which uses inherited global config when a repo-local value
    /// isn't set). <c>null</c> means no tool has been picked; callers fall
    /// back to Leaf's built-in viewer.
    /// </summary>
    Task<ExternalTool?> GetCurrentToolAsync(string repoPath, ExternalToolKind kind, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the raw tool name recorded in <c>diff.tool</c> or
    /// <c>merge.tool</c>, or <c>null</c> if unset.
    /// </summary>
    Task<string?> GetSelectedToolNameAsync(string repoPath, ExternalToolKind kind, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the tool definition (<c>difftool.&lt;name&gt;.cmd</c> or
    /// <c>mergetool.&lt;name&gt;.cmd</c>) AND sets <c>diff.tool</c> /
    /// <c>merge.tool</c> to the tool's name, in one call.
    /// Passing <see cref="ExternalTool.BuiltInName"/> unsets the selection
    /// without touching the cmd registration (so other Git-based clients
    /// keep their own preferences intact).
    /// </summary>
    Task SetSelectedToolAsync(string repoPath, ExternalTool tool, GitConfigScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the Leaf-selected tool and its <c>diff.tool</c> /
    /// <c>merge.tool</c> key so the user's inherited global value wins
    /// again. Does not touch the <c>cmd</c> registration.
    /// </summary>
    Task ClearSelectedToolAsync(string repoPath, ExternalToolKind kind, GitConfigScope scope, CancellationToken cancellationToken = default);
}
