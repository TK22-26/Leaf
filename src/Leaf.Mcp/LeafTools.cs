using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Leaf.Services;
using Leaf.Services.RepoTree;
using ModelContextProtocol.Server;

namespace Leaf.Mcp;

/// <summary>
/// The MCP tool surface: whole-tree git operations over a repository
/// and all of its (nested) submodules, backed by the same
/// <see cref="IRepoTreeService"/> the Leaf workspace grid uses. All
/// writes go through the git CLI, so a concurrently running Leaf GUI
/// is coordinated by git's own <c>index.lock</c> — collisions fail
/// loudly with git's message and are safe to retry.
/// </summary>
[McpServerToolType]
public sealed class LeafTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly IRepoTreeService _tree;
    private readonly RepoResolver _resolver;
    private readonly SettingsService _settings;

    public LeafTools(IRepoTreeService tree, RepoResolver resolver, SettingsService settings)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    [McpServerTool(Name = "leaf_repos")]
    [Description("List the repositories registered in the Leaf git client (name, path, parent repository for submodules). Use this to discover local repository paths; registration in Leaf is NOT required for the other leaf_* tools.")]
    public string ListRepositories()
    {
        var data = _settings.LoadRepositories();
        var repos = data.Repositories.Select(r => new
        {
            r.Name,
            r.Path,
            r.ParentRepositoryPath,
            r.GroupId,
        });
        return JsonSerializer.Serialize(repos, JsonOptions);
    }

    [McpServerTool(Name = "leaf_status")]
    [Description("Whole-tree git status: the repository and EVERY (nested) submodule in one call — use this instead of `git status` in repositories with submodules so no dirty submodule is missed. Reports per repo: branch, ahead/behind, staged/unstaged files, merge-in-progress, and submodule pointer drift, plus dirty/unpushed summary counts. Resolves the outermost enclosing repository for `path` (or the current directory), so it works from inside any submodule.")]
    public async Task<string> Status(
        [Description("Any path inside the repository tree. Defaults to the current working directory.")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        var root = await _resolver.ResolveRootAsync(path, cancellationToken).ConfigureAwait(false);
        var status = await _tree.GetTreeStatusAsync(root, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(status, JsonOptions);
    }

    [McpServerTool(Name = "leaf_commit")]
    [Description("Commit changes across the whole repository tree in dependency order: submodules commit first, then each parent stages the updated submodule pointers and commits — so parent commits never record SHAs that don't exist. Provide a commit message per dirty repo via `messages` (keys are root-relative paths as reported by leaf_status, \".\" for the root repo); `message` is the fallback for dirty repos without a specific entry. A dirty repo with no message fails loudly and its ancestors are skipped (outcome skipped_child_failed). Returns the per-repo outcome list with new commit SHAs.")]
    public async Task<string> Commit(
        [Description("Any path inside the repository tree. Defaults to the current working directory.")] string? path = null,
        [Description("Per-repo commit messages keyed by root-relative path (\".\" = the root repo).")] Dictionary<string, string>? messages = null,
        [Description("Fallback commit message for dirty repos without an entry in `messages`.")] string? message = null,
        [Description("Stage all unstaged changes before committing (default true). When false, only what is already staged — plus submodule pointer bumps from this run — is committed.")] bool stageAll = true,
        CancellationToken cancellationToken = default)
    {
        var root = await _resolver.ResolveRootAsync(path, cancellationToken).ConfigureAwait(false);
        var options = new TreeCommitOptions
        {
            StageAll = stageAll,
            MessageProvider = (node, _) =>
            {
                string? chosen = null;
                if (messages is not null
                    && messages.TryGetValue(node.RelativePath, out var specific)
                    && !string.IsNullOrWhiteSpace(specific))
                {
                    chosen = specific;
                }
                else if (!string.IsNullOrWhiteSpace(message))
                {
                    chosen = message;
                }
                return Task.FromResult<(string, string?)?>(chosen is null ? null : (chosen, null));
            },
        };

        var result = await _tree.CommitTreeAsync(root, options, progress: null, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "leaf_push")]
    [Description("Push every repository in the tree, submodules first. If a submodule push fails, its ancestors are skipped (outcome skipped_child_failed) — pushing a parent whose submodule didn't land would publish dangling gitlink references. Repos without a remote are skipped without failing. Always push through this tool after leaf_commit so no submodule push is missed.")]
    public async Task<string> Push(
        [Description("Any path inside the repository tree. Defaults to the current working directory.")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        var root = await _resolver.ResolveRootAsync(path, cancellationToken).ConfigureAwait(false);
        var result = await _tree.PushTreeAsync(root, progress: null, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "leaf_pull")]
    [Description("Pull every repository in the tree in parallel. A conflicted pull surfaces as a failed entry with git's error text and leaves that repo in its merge state for manual resolution; other repos are unaffected.")]
    public async Task<string> Pull(
        [Description("Any path inside the repository tree. Defaults to the current working directory.")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        var root = await _resolver.ResolveRootAsync(path, cancellationToken).ConfigureAwait(false);
        var result = await _tree.PullTreeAsync(root, progress: null, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "leaf_fetch")]
    [Description("Fetch every repository in the tree in parallel. Never modifies working trees; safe to run any time to refresh ahead/behind information.")]
    public async Task<string> Fetch(
        [Description("Any path inside the repository tree. Defaults to the current working directory.")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        var root = await _resolver.ResolveRootAsync(path, cancellationToken).ConfigureAwait(false);
        var result = await _tree.FetchTreeAsync(root, progress: null, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, JsonOptions);
    }
}
