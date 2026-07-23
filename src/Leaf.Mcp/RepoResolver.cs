using System.IO;
using Leaf.Services.RepoTree;

namespace Leaf.Mcp;

/// <summary>
/// Turns a tool's optional <c>path</c> parameter into the outermost
/// enclosing repository root. An explicit path wins; otherwise the
/// process working directory is used (Claude Code spawns stdio servers
/// in the project directory). Either way the superproject chain is
/// walked up, so a caller sitting inside a submodule still operates on
/// the whole tree. Not being inside a git working tree is an error —
/// never a fallback.
/// </summary>
public sealed class RepoResolver
{
    private readonly IRepoTreeService _tree;

    public RepoResolver(IRepoTreeService tree)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
    }

    public Task<string> ResolveRootAsync(string? path, CancellationToken cancellationToken)
    {
        var start = string.IsNullOrWhiteSpace(path)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(path);
        return _tree.ResolveTreeRootAsync(start, cancellationToken);
    }
}
