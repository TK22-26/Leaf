using Leaf.Models;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;

namespace Leaf.Services;

/// <summary>
/// LibGit2Sharp wrapper implementing IGitService.
/// All operations run on background threads and return POCOs.
/// CRITICAL: All LibGit2Sharp access must be inside using blocks to avoid lazy-loading bugs.
/// </summary>
public class GitService : IGitService
{
    public async Task<bool> IsValidRepositoryAsync(string path)
    {
        return await Task.Run(() =>
        {
            try
            {
                return Repository.IsValid(path);
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<List<CommitInfo>> GetCommitHistoryAsync(string repoPath, int count = 500, string? branchName = null)
    {
        return await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            // Get HEAD SHA and current branch name for decoration
            var headSha = repo.Head?.Tip?.Sha;
            var currentBranchName = repo.Head?.FriendlyName;

            // Get local branch tips (name without remote prefix)
            var localBranchTips = repo.Branches
                .Where(b => !b.IsRemote)
                .GroupBy(b => b.Tip?.Sha)
                .ToDictionary(g => g.Key ?? "", g => g.Select(b => b.FriendlyName).ToList());

            // Get remote branch tips (filter out HEAD, group by SHA)
            var remoteBranchTips = repo.Branches
                .Where(b => b.IsRemote && !b.FriendlyName.EndsWith("/HEAD", StringComparison.OrdinalIgnoreCase))
                .GroupBy(b => b.Tip?.Sha)
                .ToDictionary(g => g.Key ?? "", g => g.Select(b => new RemoteBranchRef(GetBranchNameWithoutRemote(b.FriendlyName), b.RemoteName ?? "origin")).ToList());

            // Combined branch tips for display names
            var allBranchTips = repo.Branches
                .Where(b => !b.IsRemote)
                .GroupBy(b => b.Tip?.Sha)
                .ToDictionary(g => g.Key ?? "", g => g.Select(b => b.FriendlyName).ToList());

            var tagTips = repo.Tags
                .GroupBy(t => t.Target?.Sha)
                .ToDictionary(g => g.Key ?? "", g => g.Select(t => t.FriendlyName).ToList());

            // Get commits from ALL branches to show all branch labels
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
                // Include commits from all branches, not just HEAD
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

            return commits
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
                    BranchLabels = BuildBranchLabels(c.Sha, localBranchTips, remoteBranchTips, currentBranchName),
                    TagNames = tagTips.TryGetValue(c.Sha, out var tags) ? tags : []
                })
                .ToList();
        });
    }

    private static string GetBranchNameWithoutRemote(string fullName)
    {
        // "origin/main" -> "main"
        var slashIndex = fullName.IndexOf('/');
        return slashIndex >= 0 ? fullName[(slashIndex + 1)..] : fullName;
    }

    private static List<BranchLabel> BuildBranchLabels(
        string sha,
        Dictionary<string, List<string>> localBranchTips,
        Dictionary<string, List<RemoteBranchRef>> remoteBranchTips,
        string? currentBranchName)
    {
        var labels = new List<BranchLabel>();
        var localBranches = localBranchTips.TryGetValue(sha, out var locals) ? locals : [];
        var remoteBranches = remoteBranchTips.TryGetValue(sha, out var remotes) ? remotes : [];

        // Create a set of all branch names at this commit
        var processedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Process local branches first
        foreach (var localName in localBranches)
        {
            // Check if there's a matching remote branch at the same commit
            var matchingRemote = remoteBranches.FirstOrDefault(r =>
                string.Equals(r.Name, localName, StringComparison.OrdinalIgnoreCase));

            labels.Add(new BranchLabel
            {
                Name = localName,
                IsLocal = true,
                IsRemote = matchingRemote != null,
                RemoteName = matchingRemote?.RemoteName,
                IsCurrent = string.Equals(localName, currentBranchName, StringComparison.OrdinalIgnoreCase)
            });
            processedNames.Add(localName);
        }

        // Process remote-only branches
        foreach (var remote in remoteBranches)
        {
            if (!processedNames.Contains(remote.Name))
            {
                labels.Add(new BranchLabel
                {
                    Name = remote.Name,
                    IsLocal = false,
                    IsRemote = true,
                    RemoteName = remote.RemoteName
                });
            }
        }

        // Sort so current branch comes first
        labels.Sort((a, b) =>
        {
            if (a.IsCurrent && !b.IsCurrent) return -1;
            if (!a.IsCurrent && b.IsCurrent) return 1;
            return 0;
        });

        return labels;
    }

    /// <summary>
    /// Helper record for tracking remote branch info.
    /// </summary>
    private record RemoteBranchRef(string Name, string RemoteName);

    public async Task<CommitInfo?> GetCommitAsync(string repoPath, string sha)
    {
        return await Task.Run(() =>
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

    public async Task<List<FileChangeInfo>> GetCommitChangesAsync(string repoPath, string sha)
    {
        return await Task.Run(() =>
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
                    LinesAdded = 0, // Would need patch for accurate count
                    LinesDeleted = 0,
                    IsBinary = false // TreeEntryChanges doesn't expose binary info directly
                });
            }

            return changes;
        });
    }

    public async Task<(string oldContent, string newContent)> GetFileDiffAsync(string repoPath, string sha, string filePath)
    {
        return await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var commit = repo.Lookup<Commit>(sha);
            if (commit == null) return ("", "");

            var parent = commit.Parents.FirstOrDefault();

            string oldContent = "";
            string newContent = "";

            // Get old content from parent
            if (parent != null)
            {
                var oldEntry = parent[filePath];
                if (oldEntry?.Target is Blob oldBlob && !oldBlob.IsBinary)
                {
                    oldContent = oldBlob.GetContentText();
                }
            }

            // Get new content from commit
            var newEntry = commit[filePath];
            if (newEntry?.Target is Blob newBlob && !newBlob.IsBinary)
            {
                newContent = newBlob.GetContentText();
            }

            return (oldContent, newContent);
        });
    }

    public async Task<List<BranchInfo>> GetBranchesAsync(string repoPath)
    {
        return await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var currentBranch = repo.Head?.FriendlyName;

            return repo.Branches
                .Select(b => new BranchInfo
                {
                    FullName = b.CanonicalName,
                    Name = b.FriendlyName,
                    IsCurrent = b.FriendlyName == currentBranch,
                    IsRemote = b.IsRemote,
                    RemoteName = b.RemoteName,
                    TrackingBranchName = b.TrackedBranch?.FriendlyName,
                    TipSha = b.Tip?.Sha ?? "",
                    AheadBy = b.TrackingDetails?.AheadBy ?? 0,
                    BehindBy = b.TrackingDetails?.BehindBy ?? 0
                })
                .ToList();
        });
    }

    public async Task<List<RemoteInfo>> GetRemotesAsync(string repoPath)
    {
        return await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            return repo.Network.Remotes
                .Select(r => new RemoteInfo
                {
                    Name = r.Name,
                    Url = r.Url,
                    PushUrl = r.PushUrl != r.Url ? r.PushUrl : null
                })
                .ToList();
        });
    }

    public async Task<RepositoryInfo> GetRepositoryInfoAsync(string repoPath)
    {
        return await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            var status = repo.RetrieveStatus();
            var isDirty = status.IsDirty;

            var currentBranch = repo.Head?.FriendlyName ?? "HEAD";
            var tracking = repo.Head?.TrackingDetails;

            return new RepositoryInfo
            {
                Path = repoPath,
                Name = System.IO.Path.GetFileName(repoPath),
                CurrentBranch = currentBranch,
                IsDirty = isDirty,
                AheadBy = tracking?.AheadBy ?? 0,
                BehindBy = tracking?.BehindBy ?? 0,
                LastAccessed = DateTimeOffset.Now
            };
        });
    }

    public async Task<string> CloneAsync(string url, string localPath, string? username = null, string? password = null, IProgress<string>? progress = null)
    {
        return await Task.Run(() =>
        {
            var options = new CloneOptions
            {
                OnCheckoutProgress = (path, completed, total) =>
                {
                    progress?.Report($"Checking out: {completed}/{total}");
                }
            };

            // Set up credentials via FetchOptions
            if (!string.IsNullOrEmpty(password))
            {
                options.FetchOptions.CredentialsProvider = CreateCredentialsProvider(url, username, password);
            }

            return Repository.Clone(url, localPath, options);
        });
    }

    public async Task FetchAsync(string repoPath, string remoteName = "origin", string? username = null, string? password = null, IProgress<string>? progress = null)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var remote = repo.Network.Remotes[remoteName];
            if (remote == null) return;

            var options = new FetchOptions
            {
                OnTransferProgress = tp =>
                {
                    progress?.Report($"Receiving: {tp.ReceivedObjects}/{tp.TotalObjects}");
                    return true;
                }
            };

            if (!string.IsNullOrEmpty(password))
            {
                options.CredentialsProvider = CreateCredentialsProvider(remote.Url, username, password);
            }

            var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
            Commands.Fetch(repo, remoteName, refSpecs, options, "Fetch from " + remoteName);
        });
    }

    public async Task PullAsync(string repoPath, string? username = null, string? password = null, IProgress<string>? progress = null)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            var options = new PullOptions
            {
                FetchOptions = new FetchOptions
                {
                    OnTransferProgress = tp =>
                    {
                        progress?.Report($"Receiving: {tp.ReceivedObjects}/{tp.TotalObjects}");
                        return true;
                    }
                }
            };

            if (!string.IsNullOrEmpty(password))
            {
                var remote = repo.Network.Remotes["origin"];
                options.FetchOptions.CredentialsProvider = CreateCredentialsProvider(remote?.Url ?? "", username, password);
            }

            var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
            Commands.Pull(repo, signature, options);
        });
    }

    public async Task PushAsync(string repoPath, string? username = null, string? password = null, IProgress<string>? progress = null)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            var options = new PushOptions
            {
                OnPushTransferProgress = (current, total, bytes) =>
                {
                    progress?.Report($"Pushing: {current}/{total}");
                    return true;
                }
            };

            if (!string.IsNullOrEmpty(password))
            {
                var remote = repo.Network.Remotes["origin"];
                options.CredentialsProvider = CreateCredentialsProvider(remote?.Url ?? "", username, password);
            }

            repo.Network.Push(repo.Head, options);
        });
    }

    private static CredentialsHandler CreateCredentialsProvider(string url, string? username, string? password)
    {
        return (_, usernameFromUrl, types) =>
        {
            // Azure DevOps: use "git" as username with PAT as password
            if (IsAzureDevOpsUrl(url))
            {
                return new UsernamePasswordCredentials
                {
                    Username = "git",
                    Password = password
                };
            }

            return new UsernamePasswordCredentials
            {
                Username = username ?? usernameFromUrl,
                Password = password
            };
        };
    }

    public async Task CheckoutAsync(string repoPath, string branchName)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            // Find the branch
            var branch = repo.Branches[branchName];
            if (branch == null)
            {
                // Try to find remote branch and create local tracking branch
                var remoteBranch = repo.Branches[$"origin/{branchName}"];
                if (remoteBranch != null)
                {
                    branch = repo.CreateBranch(branchName, remoteBranch.Tip);
                    repo.Branches.Update(branch, b => b.TrackedBranch = remoteBranch.CanonicalName);
                }
                else
                {
                    throw new InvalidOperationException($"Branch '{branchName}' not found");
                }
            }

            Commands.Checkout(repo, branch);
        });
    }

    private static bool IsAzureDevOpsUrl(string url)
    {
        return url.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase);
    }

    private static FileChangeStatus MapChangeStatus(ChangeKind kind)
    {
        return kind switch
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

    public async Task CreateBranchAsync(string repoPath, string branchName, bool checkout = true)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var branch = repo.CreateBranch(branchName);
            if (checkout)
            {
                Commands.Checkout(repo, branch);
            }
        });
    }

    public async Task StashAsync(string repoPath, string? message = null)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
            repo.Stashes.Add(signature, message ?? "Stash from Leaf");
        });
    }

    public async Task PopStashAsync(string repoPath)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            if (repo.Stashes.Any())
            {
                repo.Stashes.Pop(0);
            }
        });
    }

    public async Task<bool> UndoCommitAsync(string repoPath)
    {
        return await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            // Check if HEAD has been pushed
            if (repo.Head.TrackedBranch != null)
            {
                var localTip = repo.Head.Tip;
                var remoteTip = repo.Head.TrackedBranch.Tip;

                // If local tip equals remote tip, the commit has been pushed
                if (localTip.Sha == remoteTip?.Sha)
                {
                    return false; // Cannot undo - already pushed
                }
            }

            // Soft reset to HEAD~1
            if (repo.Head.Tip.Parents.Any())
            {
                var parentCommit = repo.Head.Tip.Parents.First();
                repo.Reset(ResetMode.Soft, parentCommit);
                return true;
            }

            return false; // No parent commit to reset to
        });
    }

    public async Task<bool> IsHeadPushedAsync(string repoPath)
    {
        return await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            if (repo.Head.TrackedBranch == null)
                return false; // No tracking branch

            var localTip = repo.Head.Tip;
            var remoteTip = repo.Head.TrackedBranch.Tip;

            return localTip.Sha == remoteTip?.Sha;
        });
    }

    public async Task<List<CommitInfo>> SearchCommitsAsync(string repoPath, string searchText, int maxResults = 100)
    {
        return await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var results = new List<CommitInfo>();
            var searchLower = searchText.ToLowerInvariant();

            foreach (var commit in repo.Commits.Take(1000)) // Search through recent commits
            {
                // Match by SHA prefix or message content
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

    public async Task<WorkingChangesInfo> GetWorkingChangesAsync(string repoPath)
    {
        return await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            var status = repo.RetrieveStatus(new StatusOptions
            {
                IncludeUntracked = true,
                RecurseUntrackedDirs = true
            });

            var workingChanges = new WorkingChangesInfo
            {
                BranchName = repo.Head?.FriendlyName ?? "HEAD"
            };

            foreach (var entry in status)
            {
                // Determine if file is staged, unstaged, or both
                var fileStatus = entry.State;

                // Check for staged changes
                if (fileStatus.HasFlag(FileStatus.NewInIndex) ||
                    fileStatus.HasFlag(FileStatus.ModifiedInIndex) ||
                    fileStatus.HasFlag(FileStatus.DeletedFromIndex) ||
                    fileStatus.HasFlag(FileStatus.RenamedInIndex) ||
                    fileStatus.HasFlag(FileStatus.TypeChangeInIndex))
                {
                    workingChanges.StagedFiles.Add(new FileStatusInfo
                    {
                        Path = entry.FilePath,
                        OldPath = entry.HeadToIndexRenameDetails?.OldFilePath,
                        Status = MapFileStatus(fileStatus, staged: true),
                        IsStaged = true
                    });
                }

                // Check for unstaged changes (working directory)
                if (fileStatus.HasFlag(FileStatus.ModifiedInWorkdir) ||
                    fileStatus.HasFlag(FileStatus.DeletedFromWorkdir) ||
                    fileStatus.HasFlag(FileStatus.TypeChangeInWorkdir) ||
                    fileStatus.HasFlag(FileStatus.RenamedInWorkdir))
                {
                    workingChanges.UnstagedFiles.Add(new FileStatusInfo
                    {
                        Path = entry.FilePath,
                        OldPath = entry.IndexToWorkDirRenameDetails?.OldFilePath,
                        Status = MapFileStatus(fileStatus, staged: false),
                        IsStaged = false
                    });
                }

                // Untracked files go to unstaged
                if (fileStatus.HasFlag(FileStatus.NewInWorkdir))
                {
                    workingChanges.UnstagedFiles.Add(new FileStatusInfo
                    {
                        Path = entry.FilePath,
                        Status = FileChangeStatus.Untracked,
                        IsStaged = false
                    });
                }
            }

            return workingChanges;
        });
    }

    private static FileChangeStatus MapFileStatus(FileStatus status, bool staged)
    {
        if (staged)
        {
            if (status.HasFlag(FileStatus.NewInIndex)) return FileChangeStatus.Added;
            if (status.HasFlag(FileStatus.ModifiedInIndex)) return FileChangeStatus.Modified;
            if (status.HasFlag(FileStatus.DeletedFromIndex)) return FileChangeStatus.Deleted;
            if (status.HasFlag(FileStatus.RenamedInIndex)) return FileChangeStatus.Renamed;
            if (status.HasFlag(FileStatus.TypeChangeInIndex)) return FileChangeStatus.TypeChanged;
        }
        else
        {
            if (status.HasFlag(FileStatus.NewInWorkdir)) return FileChangeStatus.Untracked;
            if (status.HasFlag(FileStatus.ModifiedInWorkdir)) return FileChangeStatus.Modified;
            if (status.HasFlag(FileStatus.DeletedFromWorkdir)) return FileChangeStatus.Deleted;
            if (status.HasFlag(FileStatus.RenamedInWorkdir)) return FileChangeStatus.Renamed;
            if (status.HasFlag(FileStatus.TypeChangeInWorkdir)) return FileChangeStatus.TypeChanged;
        }
        return FileChangeStatus.Modified;
    }

    public async Task StageFileAsync(string repoPath, string filePath)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            Commands.Stage(repo, filePath);
        });
    }

    public async Task UnstageFileAsync(string repoPath, string filePath)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            Commands.Unstage(repo, filePath);
        });
    }

    public async Task StageAllAsync(string repoPath)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            Commands.Stage(repo, "*");
        });
    }

    public async Task UnstageAllAsync(string repoPath)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            repo.Reset(ResetMode.Mixed);
        });
    }

    public async Task DiscardAllChangesAsync(string repoPath)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            // Reset index to HEAD
            repo.Reset(ResetMode.Hard);

            // Clean untracked files
            repo.RemoveUntrackedFiles();
        });
    }

    public async Task DiscardFileChangesAsync(string repoPath, string filePath)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            var status = repo.RetrieveStatus(filePath);

            if (status == FileStatus.NewInWorkdir)
            {
                // Untracked file - delete it
                var fullPath = System.IO.Path.Combine(repoPath, filePath);
                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }
            }
            else
            {
                // Modified/deleted file - checkout from HEAD
                repo.CheckoutPaths("HEAD", new[] { filePath }, new CheckoutOptions
                {
                    CheckoutModifiers = CheckoutModifiers.Force
                });
            }
        });
    }

    public async Task CommitAsync(string repoPath, string message, string? description = null)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            // Build full commit message
            var fullMessage = string.IsNullOrEmpty(description)
                ? message
                : $"{message}\n\n{description}";

            // Get signature from config
            var signature = repo.Config.BuildSignature(DateTimeOffset.Now);

            // Create the commit
            repo.Commit(fullMessage, signature, signature);
        });
    }
}
