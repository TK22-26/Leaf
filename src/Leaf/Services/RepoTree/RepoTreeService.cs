using System.IO;
using Leaf.Models;
using Leaf.Utils;

namespace Leaf.Services.RepoTree;

/// <summary>
/// Default <see cref="IRepoTreeService"/> implementation. Pure service
/// layer: depends only on <see cref="IGitService"/> (git CLI underneath)
/// and <see cref="ICredentialService"/>, so it runs identically under
/// the WPF app and the headless MCP host.
/// </summary>
public sealed class RepoTreeService : IRepoTreeService
{
    /// <summary>
    /// Nesting depth ceiling for tree enumeration. Real trees are 1–2
    /// levels; anything past this is a wiring error (or a cycle the
    /// visited-set missed via junctions), so we fail loudly instead of
    /// walking forever.
    /// </summary>
    private const int MaxDepth = 8;

    /// <summary>
    /// Cap on concurrent per-repo git operations during parallel tree
    /// ops. Each op spins up a git process; without a cap a
    /// 20-submodule monorepo would fork 20 gits at once and hammer
    /// disk + creds. Mirrors the workspace grid's historical default.
    /// </summary>
    internal const int MaxParallelOps = 4;

    /// <summary>
    /// Per-repo cap on file lists in a tree status snapshot. Status is
    /// a summary for callers like MCP agents; a 10k-file refactor
    /// should not produce a 10k-entry JSON payload. Truncation is
    /// always flagged via <see cref="RepoStatusEntry.FilesTruncated"/>.
    /// </summary>
    private const int MaxFilesPerRepo = 200;

    private readonly IGitService _git;
    private readonly ICredentialService _credentials;

    public RepoTreeService(IGitService gitService, ICredentialService credentialService)
    {
        _git = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _credentials = credentialService ?? throw new ArgumentNullException(nameof(credentialService));
    }

    // ─── Enumeration ────────────────────────────────────────────────────

    public async Task<IReadOnlyList<RepoNode>> GetTreeAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path must be provided.", nameof(rootPath));

        var fullRoot = Path.GetFullPath(rootPath);
        if (!await _git.IsValidRepositoryAsync(fullRoot, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException($"'{fullRoot}' is not a git repository.");

        var nodes = new List<RepoNode>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await CollectPostOrderAsync(fullRoot, fullRoot, parentPath: null, depth: 0, visited, nodes, cancellationToken).ConfigureAwait(false);
        return nodes;
    }

    private async Task CollectPostOrderAsync(
        string rootPath,
        string repoPath,
        string? parentPath,
        int depth,
        HashSet<string> visited,
        List<RepoNode> output,
        CancellationToken cancellationToken)
    {
        if (depth > MaxDepth)
            throw new InvalidOperationException(
                $"Submodule nesting under '{rootPath}' exceeds {MaxDepth} levels at '{repoPath}' — aborting tree enumeration.");
        if (!visited.Add(repoPath))
            throw new InvalidOperationException($"Submodule cycle detected at '{repoPath}'.");

        var submodules = await _git.GetSubmodulesAsync(repoPath, cancellationToken).ConfigureAwait(false);
        foreach (var sub in submodules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var subPath = Path.GetFullPath(Path.Combine(repoPath, sub.Path));
            if (sub.IsInitialized)
            {
                await CollectPostOrderAsync(rootPath, subPath, repoPath, depth + 1, visited, output, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Registered but not cloned: surface the node so status
                // can report it, but never recurse into or write to it.
                output.Add(new RepoNode(subPath, ToRootRelative(rootPath, subPath), repoPath, depth + 1, IsInitialized: false));
            }
        }

        output.Add(new RepoNode(repoPath, ToRootRelative(rootPath, repoPath), parentPath, depth, IsInitialized: true));
    }

    public async Task<string> ResolveTreeRootAsync(string anyPathInsideTree, CancellationToken cancellationToken = default)
    {
        var current = await _git.GetRepositoryRootAsync(anyPathInsideTree, cancellationToken).ConfigureAwait(false);
        for (var hops = 0; ; hops++)
        {
            if (hops > MaxDepth)
                throw new InvalidOperationException(
                    $"Superproject chain above '{anyPathInsideTree}' exceeds {MaxDepth} levels — aborting root resolution.");

            var super = await _git.GetSuperprojectWorkingTreeAsync(current, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(super))
                return current;
            current = await _git.GetRepositoryRootAsync(super, cancellationToken).ConfigureAwait(false);
        }
    }

    // ─── Status ─────────────────────────────────────────────────────────

    public async Task<RepoTreeStatus> GetTreeStatusAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        var tree = await GetTreeAsync(rootPath, cancellationToken).ConfigureAwait(false);

        var slots = new RepoStatusEntry[tree.Count];
        await RunThrottledAsync(
            tree.Select((node, index) => (node, index)),
            async item => slots[item.index] = await GetRepoStatusAsync(item.node, cancellationToken).ConfigureAwait(false),
            MaxParallelOps).ConfigureAwait(false);

        return new RepoTreeStatus(Path.GetFullPath(rootPath), slots);
    }

    private async Task<RepoStatusEntry> GetRepoStatusAsync(RepoNode node, CancellationToken cancellationToken)
    {
        if (!node.IsInitialized)
        {
            return new RepoStatusEntry(
                node.RelativePath, IsInitialized: false,
                Branch: null, IsDetachedHead: false,
                AheadBy: 0, BehindBy: 0,
                StagedFiles: [], UnstagedFiles: [], FilesTruncated: false,
                MergeInProgress: false, SubmodulePointerChanges: []);
        }

        var changesTask = _git.GetWorkingChangesAsync(node.Path, cancellationToken);
        var infoTask = _git.GetRepositoryInfoFastAsync(node.Path, cancellationToken);
        var submodulesTask = _git.GetSubmodulesAsync(node.Path, cancellationToken);
        await Task.WhenAll(changesTask, infoTask, submodulesTask).ConfigureAwait(false);

        var changes = changesTask.Result;
        var info = infoTask.Result;
        var submodules = submodulesTask.Result;

        var staged = ToFileEntries(changes.StagedFiles, out var stagedTruncated);
        var unstaged = ToFileEntries(changes.UnstagedFiles, out var unstagedTruncated);

        var pointerChanges = submodules
            .Where(s => s.Status != SubmoduleStatus.UpToDate
                || (s.WorkingSha is not null && !string.Equals(s.WorkingSha, s.RecordedSha, StringComparison.OrdinalIgnoreCase)))
            .Select(s => new SubmodulePointerChange(s.Path, s.RecordedSha, s.WorkingSha, s.Status.ToString()))
            .ToList();

        return new RepoStatusEntry(
            node.RelativePath, IsInitialized: true,
            Branch: changes.IsDetachedHead ? null : changes.BranchName,
            IsDetachedHead: changes.IsDetachedHead,
            AheadBy: info.AheadBy, BehindBy: info.BehindBy,
            StagedFiles: staged, UnstagedFiles: unstaged,
            FilesTruncated: stagedTruncated || unstagedTruncated,
            MergeInProgress: HasMergeInProgress(node.Path),
            SubmodulePointerChanges: pointerChanges);
    }

    private static List<RepoFileEntry> ToFileEntries(IEnumerable<FileStatusInfo> files, out bool truncated)
    {
        var all = files.Select(f => new RepoFileEntry(f.Path.Replace('\\', '/'), f.Status.ToString())).ToList();
        truncated = all.Count > MaxFilesPerRepo;
        return truncated ? all.Take(MaxFilesPerRepo).ToList() : all;
    }

    // ─── Commit ─────────────────────────────────────────────────────────

    public async Task<TreeOpResult> CommitTreeAsync(
        string rootPath,
        TreeCommitOptions options,
        IProgress<TreeOpProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var tree = await GetTreeAsync(rootPath, cancellationToken).ConfigureAwait(false);
        var entryByPath = new Dictionary<string, TreeOpEntry>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<TreeOpEntry>(tree.Count);

        foreach (var node in tree)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!node.IsInitialized)
            {
                Record(node, TreeOpOutcome.SkippedUninitialized, "submodule is not initialized", null);
                continue;
            }

            if (HasFailedDirectChild(node, tree, entryByPath))
            {
                Record(node, TreeOpOutcome.SkippedChildFailed,
                    "a submodule below this repo failed — committing here would record pointers this run could not produce", null);
                continue;
            }

            progress?.Report(new TreeOpProgress(node, "committing"));
            try
            {
                // Stage the gitlink paths of every direct child that
                // committed in this run so this repo's commit records
                // the new SHAs. Children that were clean (or had no
                // remote-relevant change) didn't move — nothing to stage.
                var committedChildren = DirectChildren(node, tree)
                    .Where(child => entryByPath.TryGetValue(child.Path, out var e)
                        && e.Outcome == TreeOpOutcome.Succeeded
                        && e.CommitSha is not null)
                    .ToList();
                if (committedChildren.Count > 0)
                {
                    await StageSubmodulePointersAsync(
                        node.Path,
                        committedChildren.Select(child => ToParentRelative(node.Path, child.Path)),
                        cancellationToken).ConfigureAwait(false);
                }

                var changes = await _git.GetWorkingChangesAsync(node.Path, cancellationToken).ConfigureAwait(false);
                if (!changes.HasChanges)
                {
                    Record(node, TreeOpOutcome.SkippedClean, null, null);
                    continue;
                }

                if (options.StageAll && changes.HasUnstagedChanges)
                {
                    await _git.StageAllAsync(node.Path, cancellationToken).ConfigureAwait(false);
                }

                var message = await options.MessageProvider(node, cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    Record(node, TreeOpOutcome.Failed,
                        $"no commit message provided for dirty repo '{node.RelativePath}'", null);
                    continue;
                }

                var (subject, description) = message.Value;
                await _git.CommitAsync(node.Path, subject, description, cancellationToken: cancellationToken).ConfigureAwait(false);
                var head = await _git.GetHeadCommitAsync(node.Path, cancellationToken).ConfigureAwait(false);
                Record(node, TreeOpOutcome.Succeeded, null, head?.Sha);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Record(node, TreeOpOutcome.Failed, ex.Message, null);
            }
        }

        return Finish(entries);

        void Record(RepoNode node, TreeOpOutcome outcome, string? detail, string? sha)
        {
            var entry = new TreeOpEntry(node.RelativePath, outcome, detail, sha);
            entryByPath[node.Path] = entry;
            entries.Add(entry);
        }
    }

    public async Task StageSubmodulePointersAsync(
        string parentRepoPath,
        IEnumerable<string> submoduleRelativePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submoduleRelativePaths);
        foreach (var relativePath in submoduleRelativePaths)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("Submodule relative paths must be non-empty.", nameof(submoduleRelativePaths));
            await _git.StageFileAsync(parentRepoPath, relativePath.Replace('\\', '/'), cancellationToken).ConfigureAwait(false);
        }
    }

    // ─── Push (ordered) ─────────────────────────────────────────────────

    public async Task<TreeOpResult> PushTreeAsync(
        string rootPath,
        IProgress<TreeOpProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var tree = await GetTreeAsync(rootPath, cancellationToken).ConfigureAwait(false);
        var entryByPath = new Dictionary<string, TreeOpEntry>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<TreeOpEntry>(tree.Count);

        foreach (var node in tree)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!node.IsInitialized)
            {
                Record(node, TreeOpOutcome.SkippedUninitialized, "submodule is not initialized");
                continue;
            }

            if (HasFailedDirectChild(node, tree, entryByPath))
            {
                Record(node, TreeOpOutcome.SkippedChildFailed,
                    "a submodule push below this repo failed — pushing here would dangle its submodule references on the remote");
                continue;
            }

            progress?.Report(new TreeOpProgress(node, "pushing"));
            try
            {
                var remotes = await _git.GetRemotesAsync(node.Path, cancellationToken).ConfigureAwait(false);
                if (remotes.Count == 0)
                {
                    Record(node, TreeOpOutcome.SkippedNoRemote, "no remote configured");
                    continue;
                }

                var remote = remotes.FirstOrDefault(r => r.Name == "origin") ?? remotes[0];
                var credentialKey = _credentials.ResolveActiveCredentialKey(remote.Url);
                await _git.PushAsync(node.Path, credentialKey: credentialKey, cancellationToken: cancellationToken).ConfigureAwait(false);
                Record(node, TreeOpOutcome.Succeeded, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Record(node, TreeOpOutcome.Failed, ex.Message);
            }
        }

        return Finish(entries);

        void Record(RepoNode node, TreeOpOutcome outcome, string? detail)
        {
            var entry = new TreeOpEntry(node.RelativePath, outcome, detail, null);
            entryByPath[node.Path] = entry;
            entries.Add(entry);
        }
    }

    // ─── Pull / Fetch (parallel) ────────────────────────────────────────

    public Task<TreeOpResult> PullTreeAsync(
        string rootPath,
        IProgress<TreeOpProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => RunParallelRemoteOpAsync(rootPath, "pulling",
            (node, remote, credentialKey, ct) => _git.PullAsync(node.Path, credentialKey, cancellationToken: ct),
            progress, cancellationToken);

    public Task<TreeOpResult> FetchTreeAsync(
        string rootPath,
        IProgress<TreeOpProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => RunParallelRemoteOpAsync(rootPath, "fetching",
            (node, remote, credentialKey, ct) => _git.FetchAsync(node.Path, remote.Name, credentialKey, cancellationToken: ct),
            progress, cancellationToken);

    private async Task<TreeOpResult> RunParallelRemoteOpAsync(
        string rootPath,
        string phase,
        Func<RepoNode, RemoteInfo, string?, CancellationToken, Task> operation,
        IProgress<TreeOpProgress>? progress,
        CancellationToken cancellationToken)
    {
        var tree = await GetTreeAsync(rootPath, cancellationToken).ConfigureAwait(false);
        var slots = new TreeOpEntry[tree.Count];

        await RunThrottledAsync(tree.Select((node, index) => (node, index)), async item =>
        {
            var (node, index) = item;
            if (!node.IsInitialized)
            {
                slots[index] = new TreeOpEntry(node.RelativePath, TreeOpOutcome.SkippedUninitialized, "submodule is not initialized", null);
                return;
            }

            progress?.Report(new TreeOpProgress(node, phase));
            try
            {
                var remotes = await _git.GetRemotesAsync(node.Path, cancellationToken).ConfigureAwait(false);
                if (remotes.Count == 0)
                {
                    slots[index] = new TreeOpEntry(node.RelativePath, TreeOpOutcome.SkippedNoRemote, "no remote configured", null);
                    return;
                }

                var remote = remotes.FirstOrDefault(r => r.Name == "origin") ?? remotes[0];
                var credentialKey = _credentials.ResolveActiveCredentialKey(remote.Url);
                await operation(node, remote, credentialKey, cancellationToken).ConfigureAwait(false);
                slots[index] = new TreeOpEntry(node.RelativePath, TreeOpOutcome.Succeeded, null, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                slots[index] = new TreeOpEntry(node.RelativePath, TreeOpOutcome.Failed, ex.Message, null);
            }
        }, MaxParallelOps).ConfigureAwait(false);

        return Finish(slots);
    }

    // ─── Shared helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Run <paramref name="operation"/> over every item with a
    /// parallelism cap. Shared by the tree ops here and the workspace
    /// grid's bulk tile commands.
    /// </summary>
    public static async Task RunThrottledAsync<T>(
        IEnumerable<T> items,
        Func<T, Task> operation,
        int maxParallel = MaxParallelOps)
    {
        using var gate = new SemaphoreSlim(maxParallel, maxParallel);
        var tasks = items.Select(async item =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try { await operation(item).ConfigureAwait(false); }
            finally { gate.Release(); }
        }).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// True when <paramref name="repoPath"/> has a MERGE_HEAD entry —
    /// an in-progress merge the user has not finished resolving.
    /// Handles standalone repos (.git directory) and submodules /
    /// linked worktrees (.git is a file pointing to the real gitdir).
    /// </summary>
    public static bool HasMergeInProgress(string repoPath)
    {
        if (string.IsNullOrEmpty(repoPath)) return false;
        var dotGit = Path.Combine(repoPath, ".git");
        if (Directory.Exists(dotGit))
        {
            return File.Exists(Path.Combine(dotGit, "MERGE_HEAD"));
        }
        if (File.Exists(dotGit))
        {
            try
            {
                // .git file format: "gitdir: <relative-or-absolute path>"
                var line = File.ReadAllText(dotGit).Trim();
                const string prefix = "gitdir:";
                if (!line.StartsWith(prefix, StringComparison.Ordinal)) return false;
                var target = line.Substring(prefix.Length).Trim();
                if (!Path.IsPathRooted(target))
                    target = Path.GetFullPath(Path.Combine(repoPath, target));
                return File.Exists(Path.Combine(target, "MERGE_HEAD"));
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="path"/> looks like an uninitialized
    /// submodule checkout — the directory is missing or has no
    /// <c>.git</c> file/dir, so git would refuse to operate on it.
    /// </summary>
    public static bool IsSubmoduleUninitialized(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (!Directory.Exists(path)) return true;
        var dotGit = Path.Combine(path, ".git");
        // .git can be a directory (standalone) or a file (linked / submodule pointing into parent's modules store).
        return !Directory.Exists(dotGit) && !File.Exists(dotGit);
    }

    private static IEnumerable<RepoNode> DirectChildren(RepoNode node, IReadOnlyList<RepoNode> tree)
        => tree.Where(n => n.ParentPath is not null
            && string.Equals(n.ParentPath, node.Path, StringComparison.OrdinalIgnoreCase));

    private static bool HasFailedDirectChild(
        RepoNode node,
        IReadOnlyList<RepoNode> tree,
        Dictionary<string, TreeOpEntry> entryByPath)
        => DirectChildren(node, tree).Any(child =>
            entryByPath.TryGetValue(child.Path, out var entry)
            && entry.Outcome is TreeOpOutcome.Failed or TreeOpOutcome.SkippedChildFailed);

    private static TreeOpResult Finish(IReadOnlyList<TreeOpEntry> entries)
        => new(
            entries.All(e => e.Outcome is not TreeOpOutcome.Failed and not TreeOpOutcome.SkippedChildFailed),
            entries);

    private static string ToRootRelative(string rootPath, string fullPath)
    {
        var relative = Path.GetRelativePath(rootPath, fullPath);
        return relative == "." ? "." : relative.Replace('\\', '/');
    }

    private static string ToParentRelative(string parentPath, string childPath)
        => Path.GetRelativePath(parentPath, childPath).Replace('\\', '/');
}
