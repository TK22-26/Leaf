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
    public Task<List<CommitInfo>> GetCommitHistoryAsync(string repoPath, int count = 500, string? branchName = null, int skip = 0)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            var headSha = repo.Head?.Tip?.Sha;
            var isDetachedHead = repo.Info.IsHeadDetached;
            var currentBranchName = isDetachedHead ? null : repo.Head?.FriendlyName;

            // Debug: Log all branches and their tips
            System.Diagnostics.Debug.WriteLine($"[HISTORY] GetCommitHistoryAsync: HEAD={headSha?[..7] ?? "null"}, isDetached={isDetachedHead}");
            foreach (var b in repo.Branches.Where(br => br.IsRemote).Take(10))
            {
                System.Diagnostics.Debug.WriteLine($"[HISTORY]   Remote branch: {b.FriendlyName} -> {b.Tip?.Sha?[..7] ?? "null"}");
            }
            foreach (var b in repo.Branches.Where(br => !br.IsRemote).Take(10))
            {
                System.Diagnostics.Debug.WriteLine($"[HISTORY]   Local branch: {b.FriendlyName} -> {b.Tip?.Sha?[..7] ?? "null"}");
            }

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

            var tagTips = repo.Tags
                .GroupBy(t => t.Target?.Sha)
                .ToDictionary(g => g.Key ?? "", g => g.Select(t => t.FriendlyName).ToList());

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
                var allBranchTipsList = repo.Branches
                    .Where(b => b.Tip != null)
                    .Select(b => b.Tip)
                    .Distinct()
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
        });
    }

    /// <summary>
    /// Get details for a specific commit.
    /// </summary>
    public Task<CommitInfo?> GetCommitAsync(string repoPath, string sha)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var commit = repo.Lookup<Commit>(sha);
            if (commit == null) return null;

            var headSha = repo.Head?.Tip?.Sha;

            return new CommitInfo
            {
                Sha = commit.Sha,
                Message = commit.Message,
                MessageShort = commit.MessageShort,
                Author = commit.Author.Name,
                AuthorEmail = commit.Author.Email,
                Date = commit.Author.When,
                ParentShas = commit.Parents.Select(p => p.Sha).ToList(),
                IsHead = commit.Sha == headSha
            };
        });
    }

    /// <summary>
    /// Get file changes for a commit.
    /// </summary>
    public Task<List<FileChangeInfo>> GetCommitChangesAsync(string repoPath, string sha)
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

            var diff = repo.Diff.Compare<TreeChanges>(parentTree, tree);

            foreach (var change in diff)
            {
                changes.Add(new FileChangeInfo
                {
                    Path = change.Path,
                    OldPath = change.OldPath != change.Path ? change.OldPath : null,
                    Status = MapChangeStatus(change.Status),
                    LinesAdded = 0,
                    LinesDeleted = 0,
                    IsBinary = false
                });
            }

            return changes;
        });
    }

    /// <summary>
    /// Get commits that were merged in a merge commit.
    /// </summary>
    public Task<List<CommitInfo>> GetMergeCommitsAsync(string repoPath, string mergeSha)
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
        });
    }

    /// <summary>
    /// Get commits between two references (for changelog generation).
    /// </summary>
    public Task<List<CommitInfo>> GetCommitsBetweenAsync(string repoPath, string fromRef, string? toRef = null)
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
        });
    }

    /// <summary>
    /// Search commits by message or SHA.
    /// </summary>
    public Task<List<CommitInfo>> SearchCommitsAsync(string repoPath, string searchText, int maxResults = 100)
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
        });
    }

    /// <summary>
    /// Get blame information for a file.
    /// </summary>
    public async Task<List<FileBlameLine>> GetFileBlameAsync(string repoPath, string filePath)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath, ["blame", "--line-porcelain", "--", filePath]);

        if (!result.Success)
            throw new InvalidOperationException(result.StandardError);

        var lines = new List<FileBlameLine>();
        string currentSha = string.Empty;
        string currentAuthor = string.Empty;
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
            }
        }

        return lines;
    }

    /// <summary>
    /// Get history for a file.
    /// </summary>
    public async Task<List<CommitInfo>> GetFileHistoryAsync(string repoPath, string filePath, int maxCount = 200)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["log", "--follow", "--date=iso", $"--max-count={maxCount}",
             "--pretty=format:%H%x1f%an%x1f%ad%x1f%s", "--", filePath]);

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
