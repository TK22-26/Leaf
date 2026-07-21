using System.Globalization;
using Leaf.Models;
using Leaf.Services.Git.Core;
using LibGit2Sharp;
using static Leaf.Services.Git.Operations.BranchLabelHelpers;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Operations for commit history retrieval and navigation.
/// </summary>
internal class CommitHistoryOperations
{
    private readonly IGitOperationContext _context;

    public CommitHistoryOperations(IGitOperationContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Get commit history for a repository.
    /// </summary>
    public Task<List<CommitInfo>> GetCommitHistoryAsync(string repoPath, int count = 500, string? branchName = null, int skip = 0, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            var headSha = repo.Head?.Tip?.Sha;
            var isDetachedHead = repo.Info.IsHeadDetached;
            var currentBranchName = isDetachedHead ? null : repo.Head?.FriendlyName;

            var localBranchTips = repo.Branches
                .Where(b => !b.IsRemote)
                .GroupBy(b => b.Tip?.Sha)
                .ToDictionary(g => g.Key ?? "", g => g.Select(b => b.FriendlyName).ToList());

            var remoteUrls = repo.Network.Remotes
                .ToDictionary(r => r.Name, r => r.Url, StringComparer.OrdinalIgnoreCase);

            var remoteBranchTips = repo.Branches
                .Where(b => b.IsRemote && !b.FriendlyName.EndsWith("/HEAD", StringComparison.OrdinalIgnoreCase))
                .GroupBy(b => b.Tip?.Sha)
                .ToDictionary(g => g.Key ?? "", g => g.Select(b =>
                {
                    var remoteNameValue = b.RemoteName ?? "origin";
                    var remoteUrl = remoteUrls.GetValueOrDefault(remoteNameValue, string.Empty);
                    var remoteType = RemoteBranchGroup.GetRemoteTypeFromUrl(remoteUrl);
                    return new RemoteBranchRef(GetBranchNameWithoutRemote(b.FriendlyName), remoteNameValue, remoteType);
                }).ToList());

            var allBranchTips = repo.Branches
                .Where(b => !b.IsRemote)
                .GroupBy(b => b.Tip?.Sha)
                .ToDictionary(g => g.Key ?? "", g => g.Select(b => b.FriendlyName).ToList());

            // Key by the PEELED target: for annotated tags, Target is the
            // tag object itself whose SHA never matches a commit — such
            // decorations silently vanished. PeeledTarget resolves
            // tag→tag chains to the commit; non-commit tags (blobs/trees)
            // can't be graph nodes and are excluded (#40).
            var tagTips = repo.Tags
                .Where(t => t.PeeledTarget is Commit)
                .GroupBy(t => t.PeeledTarget.Sha)
                .ToDictionary(g => g.Key, g => g.Select(t => t.FriendlyName).ToList());

            // Build reverse map: branch name → tip SHA for BranchLabel.TipSha
            var branchNameToTipSha = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (tipSha, names) in localBranchTips)
            {
                if (string.IsNullOrWhiteSpace(tipSha)) continue;
                foreach (var name in names)
                {
                    branchNameToTipSha[name] = tipSha;
                }
            }
            foreach (var (tipSha, refs) in remoteBranchTips)
            {
                if (string.IsNullOrWhiteSpace(tipSha)) continue;
                foreach (var r in refs)
                {
                    var key = $"{r.RemoteName}/{r.Name}";
                    branchNameToTipSha[key] = tipSha;
                }
            }

            ICommitLog commits;
            if (!string.IsNullOrEmpty(branchName))
            {
                var branch = repo.Branches[branchName];
                if (branch == null)
                    return [];
                commits = repo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = branch });
            }
            else
            {
                // Seed the walk from tag targets too: a commit whose only
                // ref is a tag (branch deleted) must still appear in the
                // graph, matching `git log --all --decorate` (#40).
                var tagTipCommits = repo.Tags
                    .Select(t => t.PeeledTarget)
                    .OfType<Commit>();

                var allBranchTipsList = repo.Branches
                    .Where(b => b.Tip != null)
                    .Select(b => b.Tip)
                    .Concat(tagTipCommits)
                    .DistinctBy(c => c.Sha)
                    .ToList();

                commits = repo.Commits.QueryBy(new CommitFilter
                {
                    IncludeReachableFrom = allBranchTipsList,
                    SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time
                });
            }

            var commitList = commits
                .Skip(skip)
                .Take(count)
                .Select(c => new CommitInfo
                {
                    Sha = c.Sha,
                    Message = c.Message,
                    MessageShort = c.MessageShort,
                    Author = c.Author.Name,
                    AuthorEmail = c.Author.Email,
                    Date = c.Author.When,
                    ParentShas = c.Parents.Select(p => p.Sha).ToList(),
                    IsHead = c.Sha == headSha,
                    BranchNames = allBranchTips.TryGetValue(c.Sha, out var branches) ? branches : [],
                    BranchLabels = BuildBranchLabels(c.Sha, localBranchTips, remoteBranchTips, branchNameToTipSha, currentBranchName),
                    TagNames = tagTips.TryGetValue(c.Sha, out var tags) ? tags : []
                })
                .ToList();

            var commitsBySha = commitList.ToDictionary(c => c.Sha, StringComparer.OrdinalIgnoreCase);
            var visibleShas = new HashSet<string>(commitsBySha.Keys, StringComparer.OrdinalIgnoreCase);

            foreach (var tipSha in localBranchTips.Keys.Concat(remoteBranchTips.Keys))
            {
                if (string.IsNullOrWhiteSpace(tipSha) || visibleShas.Contains(tipSha))
                    continue;

                var nearestSha = FindNearestVisibleAncestor(repo, tipSha, visibleShas);
                if (nearestSha == null || !commitsBySha.TryGetValue(nearestSha, out var targetCommit))
                    continue;

                var labels = BuildBranchLabels(tipSha, localBranchTips, remoteBranchTips, branchNameToTipSha, currentBranchName);
                foreach (var label in labels)
                    label.IsAncestorFallback = true;
                AddBranchLabels(targetCommit, labels);
            }

            // Mark existing branch label as current when in detached HEAD state
            if (isDetachedHead && !string.IsNullOrEmpty(headSha) && commitsBySha.TryGetValue(headSha, out var headCommit))
            {
                // Find and mark the first branch label at HEAD as current
                var labelToMark = headCommit.BranchLabels.FirstOrDefault();
                if (labelToMark != null)
                {
                    labelToMark.IsCurrent = true;
                }
                else
                {
                    // No existing label - add a HEAD label as fallback
                    headCommit.BranchLabels.Insert(0, new BranchLabel
                    {
                        Name = "HEAD",
                        IsLocal = true,
                        IsCurrent = true,
                        TipSha = headSha
                    });
                }
            }

            return commitList;
        }, cancellationToken);
    }

    /// <summary>
    /// Get details for a specific commit.
    /// </summary>
    public Task<CommitInfo?> GetCommitAsync(string repoPath, string sha, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var commit = repo.Lookup<Commit>(sha);
            if (commit == null) return null;

            var headSha = repo.Head?.Tip?.Sha;
            var (branchTips, tagTips) = BuildRefTipMaps(repo);
            return ToCommitInfo(commit, headSha, branchTips, tagTips);
        }, cancellationToken);
    }

    /// <summary>
    /// Get HEAD's commit without requiring the caller to resolve its SHA
    /// first. Returns null in an unborn/empty repository (HEAD has no tip).
    /// </summary>
    public Task<CommitInfo?> GetHeadCommitAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var tip = repo.Head?.Tip;
            if (tip == null) return null;
            var (branchTips, tagTips) = BuildRefTipMaps(repo);
            return ToCommitInfo(tip, tip.Sha, branchTips, tagTips);
        }, cancellationToken);
    }

    /// <summary>
    /// Pre-compute SHA → branch-names and SHA → tag-names lookup tables
    /// for the repository. Mirrors the bulk maps the graph builder
    /// constructs (lines 53-60) — extracted so single-commit callers
    /// (<see cref="GetCommitAsync"/>, <see cref="GetHeadCommitAsync"/>)
    /// don't each re-implement the ref-walking logic. Future batch
    /// callers can lift the call out of a loop and pay the O(refs)
    /// cost once.
    /// </summary>
    /// <remarks>
    /// Local-only branches: <see cref="CommitInfo.BranchNames"/> is
    /// rendered as branch chips on commit cards; remote branches show
    /// up via the graph's <see cref="CommitInfo.BranchLabels"/> path,
    /// not as chips. Filtering to <c>!IsRemote</c> here matches the
    /// graph builder's <c>allBranchTips</c> dict (line 53).
    /// </remarks>
    private static (Dictionary<string, List<string>> BranchTips, Dictionary<string, List<string>> TagTips) BuildRefTipMaps(Repository repo)
    {
        var branchTips = repo.Branches
            .Where(b => !b.IsRemote && b.Tip != null)
            .GroupBy(b => b.Tip!.Sha)
            .ToDictionary(g => g.Key, g => g.Select(b => b.FriendlyName).ToList());

        // Peeled target so annotated tags decorate the commit, not the
        // tag object — mirrors the graph builder's tagTips map (#40).
        var tagTips = repo.Tags
            .Where(t => t.PeeledTarget is Commit)
            .GroupBy(t => t.PeeledTarget.Sha)
            .ToDictionary(g => g.Key, g => g.Select(t => t.FriendlyName).ToList());

        return (branchTips, tagTips);
    }

    /// <summary>
    /// Build a <see cref="CommitInfo"/> from a libgit2 commit using
    /// pre-computed ref-tip maps. Single-commit callers build the
    /// maps inline via <see cref="BuildRefTipMaps"/>; batch callers
    /// can build once and reuse. Without the BranchNames/TagNames
    /// the bisect header's branch/tag chips render empty even when
    /// refs do point at the commit.
    /// </summary>
    private static CommitInfo ToCommitInfo(
        Commit commit,
        string? headSha,
        Dictionary<string, List<string>> branchTips,
        Dictionary<string, List<string>> tagTips) => new()
    {
        Sha = commit.Sha,
        Message = commit.Message,
        MessageShort = commit.MessageShort,
        Author = commit.Author.Name,
        AuthorEmail = commit.Author.Email,
        Date = commit.Author.When,
        ParentShas = commit.Parents.Select(p => p.Sha).ToList(),
        BranchNames = branchTips.TryGetValue(commit.Sha, out var branches) ? branches : [],
        TagNames = tagTips.TryGetValue(commit.Sha, out var tags) ? tags : [],
        IsHead = commit.Sha == headSha,
    };

    /// <summary>
    /// Get file changes for a commit.
    /// </summary>
    public Task<List<FileChangeInfo>> GetCommitChangesAsync(string repoPath, string sha, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var commit = repo.Lookup<Commit>(sha);
            if (commit == null) return [];

            var changes = new List<FileChangeInfo>();
            var parent = commit.Parents.FirstOrDefault();

            var tree = commit.Tree;
            var parentTree = parent?.Tree;

            var diff = repo.Diff.Compare<TreeChanges>(parentTree, tree,
                new LibGit2Sharp.CompareOptions { Similarity = SimilarityOptions.Renames });

            // Compute per-file line stats via Diff.Compare<Patch>. The
            // TreeChanges projection above gives us the file metadata
            // (path, status, rename detection, submodule mode) but NOT
            // the line counts — those live on PatchEntryChange. Without
            // this, every consumer (bisect detail view, commit detail
            // view, etc.) sees +0/-0 even on files with real changes.
            // Indexed by Path so we can pair each tree change with its
            // patch stats below.
            var lineStats = new Dictionary<string, (int added, int deleted, bool binary)>(StringComparer.Ordinal);
            try
            {
                var patch = repo.Diff.Compare<Patch>(parentTree, tree,
                    new LibGit2Sharp.CompareOptions { Similarity = SimilarityOptions.Renames });
                foreach (var entry in patch)
                {
                    lineStats[entry.Path] = (entry.LinesAdded, entry.LinesDeleted, entry.IsBinaryComparison);
                }
            }
            catch (Exception ex)
            {
                // Patch generation can fail on huge / pathological diffs;
                // we'd rather lose the line counts than the whole change
                // list, so we swallow and fall through to zeros. Logged
                // for diagnosability.
                Log.Info("CommitHistory", $"Patch line-count probe failed for {sha}: {ex.Message}");
            }

            foreach (var change in diff)
            {
                // libgit2sharp reports submodule pointers as tree entries
                // in git-link mode (0160000). The line-diff machinery
                // doesn't apply to these — we carry the commit SHAs so
                // the commit detail view can render them directly.
                var isSubmodule = change.Mode == Mode.GitLink
                                  || change.OldMode == Mode.GitLink;

                var oldSha = string.Empty;
                var newSha = string.Empty;
                if (isSubmodule)
                {
                    oldSha = change.OldOid?.Sha ?? string.Empty;
                    newSha = change.Oid?.Sha ?? string.Empty;
                    // When a submodule is added or removed the "empty"
                    // side reports an all-zero oid that libgit2sharp
                    // surfaces literally — suppress it for cleaner UI.
                    if (oldSha == new string('0', 40)) oldSha = string.Empty;
                    if (newSha == new string('0', 40)) newSha = string.Empty;
                }

                lineStats.TryGetValue(change.Path, out var stats);
                changes.Add(new FileChangeInfo
                {
                    Path = change.Path,
                    OldPath = change.OldPath != change.Path ? change.OldPath : null,
                    Status = MapChangeStatus(change.Status),
                    LinesAdded = stats.added,
                    LinesDeleted = stats.deleted,
                    IsBinary = stats.binary,
                    IsSubmodule = isSubmodule,
                    SubmoduleOldSha = oldSha,
                    SubmoduleNewSha = newSha,
                });
            }

            return changes;
        }, cancellationToken);
    }

    /// <summary>
    /// Get commits that were merged in a merge commit.
    /// </summary>
    public Task<List<CommitInfo>> GetMergeCommitsAsync(string repoPath, string mergeSha, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var mergeCommit = repo.Lookup<Commit>(mergeSha);
            if (mergeCommit == null)
                return new List<CommitInfo>();

            var parents = mergeCommit.Parents.ToList();
            if (parents.Count < 2)
                return new List<CommitInfo>();

            var mainParent = parents[0];
            var mergedParent = parents[1];
            var mergeBase = repo.ObjectDatabase.FindMergeBase(mainParent, mergedParent);
            if (mergeBase == null)
                return new List<CommitInfo>();

            var filter = new CommitFilter
            {
                IncludeReachableFrom = mergedParent,
                ExcludeReachableFrom = mergeBase,
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time
            };

            return repo.Commits.QueryBy(filter)
                .Select(commit => new CommitInfo
                {
                    Sha = commit.Sha,
                    Message = commit.Message,
                    MessageShort = commit.MessageShort,
                    Author = commit.Author.Name,
                    AuthorEmail = commit.Author.Email,
                    Date = commit.Author.When,
                    ParentShas = commit.Parents.Select(p => p.Sha).ToList()
                })
                .ToList();
        }, cancellationToken);
    }

    /// <summary>
    /// Get commits between two references (for changelog generation).
    /// </summary>
    public Task<List<CommitInfo>> GetCommitsBetweenAsync(string repoPath, string fromRef, string? toRef = null, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var commits = new List<CommitInfo>();

            var fromCommit = repo.Lookup<Commit>(fromRef);
            if (fromCommit == null)
            {
                var tag = repo.Tags[fromRef];
                if (tag != null)
                {
                    fromCommit = tag.Target as Commit;
                    if (fromCommit == null && tag.Target is TagAnnotation annotation)
                        fromCommit = annotation.Target as Commit;
                }
            }

            Commit? toCommit;
            if (string.IsNullOrEmpty(toRef))
            {
                toCommit = repo.Head.Tip;
            }
            else
            {
                toCommit = repo.Lookup<Commit>(toRef);
                if (toCommit == null)
                {
                    var tag = repo.Tags[toRef];
                    if (tag != null)
                    {
                        toCommit = tag.Target as Commit;
                        if (toCommit == null && tag.Target is TagAnnotation annotation)
                            toCommit = annotation.Target as Commit;
                    }
                }
            }

            if (toCommit == null)
                return commits;

            var filter = new CommitFilter
            {
                IncludeReachableFrom = toCommit,
                ExcludeReachableFrom = fromCommit,
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time
            };

            foreach (var commit in repo.Commits.QueryBy(filter))
            {
                commits.Add(new CommitInfo
                {
                    Sha = commit.Sha,
                    Message = commit.Message,
                    MessageShort = commit.MessageShort,
                    Author = commit.Author.Name,
                    AuthorEmail = commit.Author.Email,
                    Date = commit.Author.When
                });
            }

            return commits;
        }, cancellationToken);
    }

    /// <summary>
    /// Search commits by message or SHA.
    /// </summary>
    public Task<List<CommitInfo>> SearchCommitsAsync(string repoPath, string searchText, int maxResults = 100, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var results = new List<CommitInfo>();

            foreach (var commit in repo.Commits.Take(1000))
            {
                if (commit.Sha.StartsWith(searchText, StringComparison.OrdinalIgnoreCase) ||
                    commit.Message.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new CommitInfo
                    {
                        Sha = commit.Sha,
                        Message = commit.Message,
                        MessageShort = commit.MessageShort,
                        Author = commit.Author.Name,
                        AuthorEmail = commit.Author.Email,
                        Date = commit.Author.When,
                        ParentShas = commit.Parents.Select(p => p.Sha).ToList()
                    });

                    if (results.Count >= maxResults)
                        break;
                }
            }

            return results;
        }, cancellationToken);
    }

    /// <summary>
    /// Get blame information for a file.
    /// </summary>
    public async Task<List<FileBlameLine>> GetFileBlameAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath, ["blame", "--line-porcelain", "--", filePath], cancellationToken: cancellationToken);

        if (!result.Success)
            throw new InvalidOperationException(result.StandardError);

        var lines = new List<FileBlameLine>();
        string currentSha = string.Empty;
        string currentAuthor = string.Empty;
        string currentSubject = string.Empty;
        DateTimeOffset currentDate = DateTimeOffset.MinValue;
        int currentLineNumber = 0;

        foreach (var rawLine in result.StandardOutput.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0) continue;

            if (line[0] == '\t')
            {
                lines.Add(new FileBlameLine
                {
                    LineNumber = currentLineNumber,
                    Sha = currentSha,
                    Author = currentAuthor,
                    Date = currentDate,
                    Subject = currentSubject,
                    Content = line[1..]
                });
                continue;
            }

            if (line.Length >= 40 && _context.OutputParser.IsShaLine(line))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                currentSha = parts[0];
                if (parts.Length >= 3 && int.TryParse(parts[2], out var finalLine))
                    currentLineNumber = finalLine;
                continue;
            }

            if (line.StartsWith("author ", StringComparison.Ordinal))
            {
                currentAuthor = line["author ".Length..];
                continue;
            }

            if (line.StartsWith("author-time ", StringComparison.Ordinal))
            {
                if (long.TryParse(line["author-time ".Length..], out var seconds))
                    currentDate = DateTimeOffset.FromUnixTimeSeconds(seconds);
                continue;
            }

            // 'summary' = the commit subject line. Porcelain emits it once per
            // commit (cached by sha) so the same subject applies to every
            // subsequent "\t"-prefixed content line for that commit until a
            // new sha header appears.
            if (line.StartsWith("summary ", StringComparison.Ordinal))
            {
                currentSubject = line["summary ".Length..];
            }
        }

        return lines;
    }

    /// <summary>
    /// Get history for a file.
    /// </summary>
    public async Task<List<CommitInfo>> GetFileHistoryAsync(string repoPath, string filePath, int maxCount = 200, CancellationToken cancellationToken = default)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["log", "--follow", "--date=iso", $"--max-count={maxCount}",
             "--pretty=format:%H%x1f%an%x1f%ad%x1f%s", "--", filePath], cancellationToken: cancellationToken);

        if (!result.Success)
            throw new InvalidOperationException(result.StandardError);

        var commits = new List<CommitInfo>();
        foreach (var rawLine in result.StandardOutput.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split('\x1f');
            if (parts.Length < 4) continue;

            if (!DateTimeOffset.TryParse(parts[2], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
                date = DateTimeOffset.Now;

            commits.Add(new CommitInfo
            {
                Sha = parts[0],
                Message = parts[3],
                MessageShort = parts[3],
                Author = parts[1],
                Date = date
            });
        }

        return commits;
    }

    /// <summary>
    /// Get all files in the repository at a given commit, with changed files marked with their status.
    /// </summary>
    public Task<List<FileChangeInfo>> GetCommitAllFilesAsync(string repoPath, string sha, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var commit = repo.Lookup<Commit>(sha);
            if (commit == null) return [];

            // Get changed files with their statuses
            var parent = commit.Parents.FirstOrDefault();
            var diff = repo.Diff.Compare<TreeChanges>(parent?.Tree, commit.Tree,
                new LibGit2Sharp.CompareOptions { Similarity = SimilarityOptions.Renames });

            var changedByPath = new Dictionary<string, FileChangeInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var change in diff)
            {
                changedByPath[change.Path] = new FileChangeInfo
                {
                    Path = change.Path,
                    OldPath = change.OldPath != change.Path ? change.OldPath : null,
                    Status = MapChangeStatus(change.Status),
                    LinesAdded = 0,
                    LinesDeleted = 0,
                    IsBinary = false
                };
            }

            // Walk the full tree to get all files
            var allFiles = new List<FileChangeInfo>();
            foreach (var entry in commit.Tree.SelectMany(e => EnumerateTreeEntries(e, commit.Tree)))
            {
                if (changedByPath.TryGetValue(entry, out var changedFile))
                {
                    allFiles.Add(changedFile);
                }
                else
                {
                    allFiles.Add(new FileChangeInfo
                    {
                        Path = entry,
                        Status = FileChangeStatus.Unchanged
                    });
                }
            }

            return allFiles;
        }, cancellationToken);
    }

    private static IEnumerable<string> EnumerateTreeEntries(TreeEntry entry, Tree root)
    {
        if (entry.TargetType == TreeEntryTargetType.Blob)
        {
            yield return entry.Path;
        }
        else if (entry.TargetType == TreeEntryTargetType.Tree && entry.Target is Tree subtree)
        {
            foreach (var child in subtree)
            {
                foreach (var path in EnumerateTreeEntries(child, root))
                {
                    yield return path;
                }
            }
        }
    }

    private static FileChangeStatus MapChangeStatus(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => FileChangeStatus.Added,
        ChangeKind.Deleted => FileChangeStatus.Deleted,
        ChangeKind.Modified => FileChangeStatus.Modified,
        ChangeKind.Renamed => FileChangeStatus.Renamed,
        ChangeKind.Copied => FileChangeStatus.Copied,
        ChangeKind.TypeChanged => FileChangeStatus.TypeChanged,
        ChangeKind.Untracked => FileChangeStatus.Untracked,
        ChangeKind.Ignored => FileChangeStatus.Ignored,
        ChangeKind.Conflicted => FileChangeStatus.Conflicted,
        _ => FileChangeStatus.Modified
    };
}
