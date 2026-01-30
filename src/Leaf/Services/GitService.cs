using System.Globalization;
using System.IO;
using System.Text;
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
    public event EventHandler<GitCommandEventArgs>? GitCommandExecuted;

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

            // Build remote URL lookup for determining remote type (GitHub, AzureDevOps, etc.)
            var remoteUrls = repo.Network.Remotes
                .ToDictionary(r => r.Name, r => r.Url, StringComparer.OrdinalIgnoreCase);

            // Get remote branch tips (filter out HEAD, group by SHA)
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

            var commitList = commits
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

            var commitsBySha = commitList.ToDictionary(c => c.Sha, StringComparer.OrdinalIgnoreCase);
            var visibleShas = new HashSet<string>(commitsBySha.Keys, StringComparer.OrdinalIgnoreCase);

            foreach (var tipSha in localBranchTips.Keys.Concat(remoteBranchTips.Keys))
            {
                if (string.IsNullOrWhiteSpace(tipSha) || visibleShas.Contains(tipSha))
                    continue;

                var nearestSha = FindNearestVisibleAncestor(repo, tipSha, visibleShas);
                if (nearestSha == null || !commitsBySha.TryGetValue(nearestSha, out var targetCommit))
                    continue;

                var labels = BuildBranchLabels(tipSha, localBranchTips, remoteBranchTips, currentBranchName);
                AddBranchLabels(targetCommit, labels);
            }

            return commitList;
        });
    }

    private static string? FindNearestVisibleAncestor(Repository repo, string tipSha, HashSet<string> visibleShas)
    {
        var start = repo.Lookup<Commit>(tipSha);
        if (start == null)
            return null;

        var queue = new Queue<Commit>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current.Sha))
                continue;

            if (visibleShas.Contains(current.Sha))
                return current.Sha;

            foreach (var parent in current.Parents)
            {
                if (!visited.Contains(parent.Sha))
                {
                    queue.Enqueue(parent);
                }
            }
        }

        return null;
    }

    private static void AddBranchLabels(CommitInfo commit, List<BranchLabel> labels)
    {
        foreach (var label in labels)
        {
            if (!commit.BranchLabels.Any(existing =>
                    string.Equals(existing.FullName, label.FullName, StringComparison.OrdinalIgnoreCase)))
            {
                commit.BranchLabels.Add(label);
            }

            if (label.IsLocal && !commit.BranchNames.Any(name =>
                    string.Equals(name, label.Name, StringComparison.OrdinalIgnoreCase)))
            {
                commit.BranchNames.Add(label.Name);
            }
        }
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
                RemoteType = matchingRemote?.RemoteType ?? RemoteType.Other,
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
                    RemoteName = remote.RemoteName,
                    RemoteType = remote.RemoteType
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
    private record RemoteBranchRef(string Name, string RemoteName, RemoteType RemoteType);

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

    public async Task<(string oldContent, string newContent)> GetUnstagedFileDiffAsync(string repoPath, string filePath)
    {
        return await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            string oldContent = "";
            string newContent = "";

            // Get content from index (staged version)
            var indexEntry = repo.Index[filePath];
            if (indexEntry != null)
            {
                var blob = repo.Lookup<Blob>(indexEntry.Id);
                if (blob != null && !blob.IsBinary)
                {
                    oldContent = blob.GetContentText();
                }
            }
            else
            {
                // File not in index, check HEAD
                var headCommit = repo.Head?.Tip;
                if (headCommit != null)
                {
                    var headEntry = headCommit[filePath];
                    if (headEntry?.Target is Blob headBlob && !headBlob.IsBinary)
                    {
                        oldContent = headBlob.GetContentText();
                    }
                }
            }

            // Get content from working directory
            var fullPath = Path.Combine(repoPath, filePath);
            if (File.Exists(fullPath))
            {
                try
                {
                    newContent = File.ReadAllText(fullPath);
                }
                catch
                {
                    // File might be locked or binary
                }
            }

            return (oldContent, newContent);
        });
    }

    public async Task<(string oldContent, string newContent)> GetStagedFileDiffAsync(string repoPath, string filePath)
    {
        return await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            string oldContent = "";
            string newContent = "";

            // Get content from HEAD
            var headCommit = repo.Head?.Tip;
            if (headCommit != null)
            {
                var headEntry = headCommit[filePath];
                if (headEntry?.Target is Blob headBlob && !headBlob.IsBinary)
                {
                    oldContent = headBlob.GetContentText();
                }
            }

            // Get content from index (staged version)
            var indexEntry = repo.Index[filePath];
            if (indexEntry != null)
            {
                var blob = repo.Lookup<Blob>(indexEntry.Id);
                if (blob != null && !blob.IsBinary)
                {
                    newContent = blob.GetContentText();
                }
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

            // Check for merge in progress
            bool isMergeInProgress = false;
            string mergingBranch = string.Empty;
            int conflictCount = 0;

            var mergeHeadPath = System.IO.Path.Combine(repoPath, ".git", "MERGE_HEAD");
            if (System.IO.File.Exists(mergeHeadPath))
            {
                isMergeInProgress = true;
                
                // Try to read the merging branch sha/name
                // This is a simplification; MERGE_HEAD contains the SHA
                // We might want to look it up, but for now knowing a merge is in progress is key
                // Often we can get the name from .git/MERGE_MSG if needed, but let's keep it simple
                mergingBranch = "Incoming"; // Default
                
                var mergeMsgPath = System.IO.Path.Combine(repoPath, ".git", "MERGE_MSG");
                if (System.IO.File.Exists(mergeMsgPath))
                {
                    try 
                    {
                        var msg = System.IO.File.ReadAllText(mergeMsgPath).Trim();
                        // Common format: "Merge branch 'feature' into master"
                        if (msg.StartsWith("Merge branch '") && msg.Contains("'"))
                        {
                             var parts = msg.Split('\'');
                             if (parts.Length >= 2)
                             {
                                 mergingBranch = parts[1];
                             }
                        }
                    }
                    catch { /* ignore */ }
                }
            }

            // Count conflicts
            if (repo.Index.Conflicts.Any())
            {
                 conflictCount = repo.Index.Conflicts.Select(c => c.Ancestor?.Path ?? c.Ours?.Path ?? c.Theirs?.Path).Distinct().Count();
            }

            return new RepositoryInfo
            {
                Path = repoPath,
                Name = System.IO.Path.GetFileName(repoPath),
                CurrentBranch = currentBranch,
                IsDirty = isDirty,
                AheadBy = tracking?.AheadBy ?? 0,
                BehindBy = tracking?.BehindBy ?? 0,
                LastAccessed = DateTimeOffset.Now,
                IsMergeInProgress = isMergeInProgress,
                MergingBranch = mergingBranch,
                ConflictCount = conflictCount
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
                Prune = true, // Remove remote-tracking branches that no longer exist on remote
                OnTransferProgress = tp =>
                {
                    progress?.Report($"Receiving: {tp.ReceivedObjects}/{tp.TotalObjects}");
                    return true;
                },
                // Always set credentials provider - it will try Git Credential Manager if no explicit creds
                CredentialsProvider = CreateCredentialsProvider(remote.Url, username, password)
            };

            var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
            Commands.Fetch(repo, remoteName, refSpecs, options, "Fetch from " + remoteName);
        });
    }

    public async Task PullAsync(string repoPath, string? username = null, string? password = null, IProgress<string>? progress = null)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var remote = repo.Network.Remotes["origin"];

            var options = new PullOptions
            {
                FetchOptions = new FetchOptions
                {
                    Prune = true, // Remove remote-tracking branches that no longer exist on remote
                    OnTransferProgress = tp =>
                    {
                        progress?.Report($"Receiving: {tp.ReceivedObjects}/{tp.TotalObjects}");
                        return true;
                    },
                    // Always set credentials provider - it will try Git Credential Manager if no explicit creds
                    CredentialsProvider = CreateCredentialsProvider(remote?.Url ?? "", username, password)
                }
            };

            var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
            Commands.Pull(repo, signature, options);
        });
    }

    public async Task PushAsync(string repoPath, string? username = null, string? password = null, IProgress<string>? progress = null)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var remote = repo.Network.Remotes["origin"];

            if (repo.Info.IsHeadDetached)
            {
                throw new InvalidOperationException("Cannot push while in detached HEAD state.");
            }

            if (repo.Head.TrackedBranch == null)
            {
                var branchName = repo.Head.FriendlyName;
                var result = RunGitCommand(repoPath, $"push -u \"origin\" \"{branchName}\"");
                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException(result.Error);
                }
                return;
            }

            var options = new PushOptions
            {
                OnPushTransferProgress = (current, total, bytes) =>
                {
                    progress?.Report($"Pushing: {current}/{total}");
                    return true;
                },
                // Always set credentials provider - it will try Git Credential Manager if no explicit creds
                CredentialsProvider = CreateCredentialsProvider(remote?.Url ?? "", username, password)
            };

            repo.Network.Push(repo.Head, options);
        });
    }

    public async Task PullBranchFastForwardAsync(string repoPath, string branchName, string remoteName, string remoteBranchName, bool isCurrentBranch)
    {
        var args = isCurrentBranch
            ? $"pull --ff-only \"{remoteName}\" \"{remoteBranchName}\""
            : $"fetch \"{remoteName}\" \"{remoteBranchName}\":\"{branchName}\"";

        var result = await RunGitCommandAsync(repoPath, args);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Error);
        }
    }

    public async Task PushBranchAsync(string repoPath, string branchName, string remoteName, string remoteBranchName, bool isCurrentBranch)
    {
        var args = isCurrentBranch
            ? $"push \"{remoteName}\" \"{branchName}\""
            : $"push \"{remoteName}\" \"{branchName}\":\"{remoteBranchName}\"";

        var result = await RunGitCommandAsync(repoPath, args);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Error);
        }
    }

    public async Task SetUpstreamAsync(string repoPath, string branchName, string remoteName, string remoteBranchName)
    {
        var result = await RunGitCommandAsync(repoPath, "branch", "--set-upstream-to", $"{remoteName}/{remoteBranchName}", branchName);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Error);
        }
    }

    public async Task RenameBranchAsync(string repoPath, string oldName, string newName)
    {
        var result = await RunGitCommandAsync(repoPath, "branch", "-m", oldName, newName);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Error);
        }
    }

    public async Task RevertCommitAsync(string repoPath, string commitSha)
    {
        var result = await RunGitCommandAsync(repoPath, "revert", commitSha);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Error);
        }
    }

    public async Task RevertMergeCommitAsync(string repoPath, string commitSha, int parentIndex)
    {
        var result = await RunGitCommandAsync(repoPath, "revert", "-m", parentIndex.ToString(), commitSha);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Error);
        }
    }

    public async Task<bool> RedoCommitAsync(string repoPath)
    {
        var result = await RunGitCommandAsync(repoPath, "reset", "--soft", "ORIG_HEAD");
        return result.ExitCode == 0;
    }

    public async Task ResetBranchToCommitAsync(string repoPath, string branchName, string commitSha, bool updateWorkingTree)
    {
        var result = updateWorkingTree
            ? await RunGitCommandAsync(repoPath, "reset", "--hard", commitSha)
            : await RunGitCommandAsync(repoPath, "branch", "-f", branchName, commitSha);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Error);
        }
    }

    private static CredentialsHandler CreateCredentialsProvider(string url, string? username, string? password)
    {
        return (_, usernameFromUrl, types) =>
        {
            var effectiveUsername = username ?? usernameFromUrl;
            var effectivePassword = password;

            // If no credentials provided, try Git Credential Manager
            if (string.IsNullOrEmpty(effectivePassword))
            {
                var (gcmUser, gcmPass) = GetCredentialsFromGitCredentialManager(url);
                if (!string.IsNullOrEmpty(gcmPass))
                {
                    effectiveUsername = gcmUser ?? effectiveUsername;
                    effectivePassword = gcmPass;
                }
            }

            // Azure DevOps: use "git" as username with PAT as password
            if (IsAzureDevOpsUrl(url))
            {
                return new UsernamePasswordCredentials
                {
                    Username = "git",
                    Password = effectivePassword
                };
            }

            return new UsernamePasswordCredentials
            {
                Username = effectiveUsername,
                Password = effectivePassword
            };
        };
    }

    private static (string? username, string? password) GetCredentialsFromGitCredentialManager(string url)
    {
        try
        {
            if (string.IsNullOrEmpty(url)) return (null, null);

            var uri = new Uri(url);
            var input = $"protocol={uri.Scheme}\nhost={uri.Host}\n";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "credential fill",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return (null, null);

            process.StandardInput.Write(input);
            process.StandardInput.Close();

            if (!process.WaitForExit(5000)) // 5 second timeout
            {
                process.Kill();
                return (null, null);
            }

            if (process.ExitCode != 0) return (null, null);

            var output = process.StandardOutput.ReadToEnd();
            string? username = null;
            string? password = null;

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("username="))
                    username = line["username=".Length..];
                else if (line.StartsWith("password="))
                    password = line["password=".Length..];
            }

            return (username, password);
        }
        catch
        {
            return (null, null);
        }
    }

    public async Task CheckoutAsync(string repoPath, string branchName, bool allowConflicts = false)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            // Check if there's a merge in progress with conflicts
            if (repo.Index.Conflicts.Any())
            {
                throw new InvalidOperationException(
                    "Cannot switch branches: there are unresolved merge conflicts. " +
                    "Please resolve the conflicts or abort the merge first.");
            }

            // Check if repo is in a merge state
            var mergeHeadPath = System.IO.Path.Combine(repoPath, ".git", "MERGE_HEAD");
            if (System.IO.File.Exists(mergeHeadPath))
            {
                throw new InvalidOperationException(
                    "Cannot switch branches: a merge is in progress. " +
                    "Please complete or abort the merge first.");
            }

            // Find the branch (normalize remote names)
            var branch = repo.Branches[branchName];
            var remoteName = "origin";
            var shortName = branchName;
            var slashIndex = branchName.IndexOf('/');
            if (slashIndex > 0)
            {
                remoteName = branchName[..slashIndex];
                shortName = branchName[(slashIndex + 1)..];
            }

            if (branch != null && branch.IsRemote)
            {
                var localBranch = repo.Branches[shortName];
                if (localBranch == null)
                {
                    localBranch = repo.CreateBranch(shortName, branch.Tip);
                    repo.Branches.Update(localBranch, b => b.TrackedBranch = branch.CanonicalName);
                }
                branch = localBranch;
            }

            if (branch == null)
            {
                // Try to find remote branch and create local tracking branch
                var remoteBranch = repo.Branches[$"{remoteName}/{shortName}"];
                if (remoteBranch != null)
                {
                    branch = repo.CreateBranch(shortName, remoteBranch.Tip);
                    repo.Branches.Update(branch, b => b.TrackedBranch = remoteBranch.CanonicalName);
                }
                else
                {
                    throw new InvalidOperationException($"Branch '{branchName}' not found");
                }
            }

            if (allowConflicts)
            {
                var result = RunGit(repoPath, $"checkout -m \"{branch.FriendlyName}\"");
                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Checkout failed: {result.Error}");
                }
                return;
            }

            Commands.Checkout(repo, branch);
        });
    }

    public async Task CheckoutCommitAsync(string repoPath, string commitSha)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            // Check if there's a merge in progress with conflicts
            if (repo.Index.Conflicts.Any())
            {
                throw new InvalidOperationException(
                    "Cannot checkout commit: there are unresolved merge conflicts. " +
                    "Please resolve the conflicts or abort the merge first.");
            }

            // Check if repo is in a merge state
            var mergeHeadPath = System.IO.Path.Combine(repoPath, ".git", "MERGE_HEAD");
            if (System.IO.File.Exists(mergeHeadPath))
            {
                throw new InvalidOperationException(
                    "Cannot checkout commit: a merge is in progress. " +
                    "Please complete or abort the merge first.");
            }

            // Find the commit
            var commit = repo.Lookup<Commit>(commitSha);
            if (commit == null)
            {
                throw new InvalidOperationException($"Commit '{commitSha}' not found");
            }

            // Checkout the commit (detached HEAD)
            Commands.Checkout(repo, commit);
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

    public async Task CreateBranchAtCommitAsync(string repoPath, string branchName, string commitSha, bool checkout = true)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var commit = repo.Lookup<Commit>(commitSha);
            if (commit == null)
            {
                throw new InvalidOperationException($"Commit '{commitSha}' not found.");
            }

            var branch = repo.CreateBranch(branchName, commit);
            if (checkout)
            {
                Commands.Checkout(repo, branch);
            }
        });
    }

    public async Task<Models.MergeResult> CherryPickAsync(string repoPath, string commitSha)
    {
        var result = await RunGitCommandAsync(repoPath, "cherry-pick", commitSha);
        if (result.ExitCode == 0)
        {
            return new Models.MergeResult { Success = true };
        }

        var conflicts = await GetConflictsAsync(repoPath);
        return new Models.MergeResult
        {
            Success = false,
            HasConflicts = conflicts.Count > 0,
            ErrorMessage = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error
        };
    }

    public async Task<string> GetCommitToWorkingTreeDiffAsync(string repoPath, string commitSha)
    {
        var result = await RunGitCommandAsync(repoPath, "diff", commitSha);
        if (result.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            throw new InvalidOperationException(message);
        }

        return result.Output;
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

    public async Task StashStagedAsync(string repoPath, string? message = null)
    {
        // Use git command line since LibGit2Sharp doesn't support --staged option (Git 2.35+)
        var args = new List<string> { "stash", "push", "--staged" };
        if (!string.IsNullOrEmpty(message))
        {
            args.Add("-m");
            args.Add(message);
        }

        await RunGitCommandAsync(repoPath, args.ToArray());
    }

    public async Task<Models.MergeResult> PopStashAsync(string repoPath)
    {
        return await PopStashAsync(repoPath, 0);
    }

    public async Task<Models.MergeResult> PopStashAsync(string repoPath, int stashIndex)
    {
        return await Task.Run(() =>
        {
            var result = new Models.MergeResult();

            System.Diagnostics.Debug.WriteLine($"[PopStash] Starting smart pop for stash index {stashIndex} in {repoPath}");

            // Step 1: Check if there are uncommitted changes
            bool hasChanges = HasUncommittedChanges(repoPath);
            System.Diagnostics.Debug.WriteLine($"[PopStash] Has uncommitted changes: {hasChanges}");

            if (!hasChanges)
            {
                // Simple case - no local changes, pop directly
                System.Diagnostics.Debug.WriteLine("[PopStash] No local changes - using simple pop");
                return SimplePopStash(repoPath, stashIndex);
            }

            // Smart pop: Patch-based approach
            // Get stash as patch and apply with 3-way merge
            System.Diagnostics.Debug.WriteLine("[PopStash] Local changes detected - using patch-based approach");

            // Step 2: Get the stash diff as a patch
            var stashRef = $"stash@{{{stashIndex}}}";
            var patchResult = RunGit(repoPath, $"stash show -p {stashRef}");
            System.Diagnostics.Debug.WriteLine($"[PopStash] Patch result: exit={patchResult.ExitCode}, length={patchResult.Output.Length}");

            if (patchResult.ExitCode != 0 || string.IsNullOrWhiteSpace(patchResult.Output))
            {
                result.ErrorMessage = $"Failed to get stash patch: {patchResult.Error}";
                System.Diagnostics.Debug.WriteLine($"[PopStash] ERROR: {result.ErrorMessage}");
                return result;
            }

            // Step 3: Apply the patch using 'patch' with fuzz for fuzzy matching
            // This allows merging when local changes exist on different lines
            var applyResult = RunPatchWithInput(repoPath, patchResult.Output);
            System.Diagnostics.Debug.WriteLine($"[PopStash] Patch apply result: exit={applyResult.ExitCode}, output={applyResult.Output}, error={applyResult.Error}");

            // Check if patch.exe wasn't found (Git for Windows not installed)
            if (applyResult.ExitCode == -1 && applyResult.Error.Contains("patch.exe"))
            {
                System.Diagnostics.Debug.WriteLine("[PopStash] patch.exe not found - Git for Windows required");
                result.ErrorMessage = applyResult.Error;
                return result;
            }

            // Check if patch created .rej files (rejected hunks = conflicts)
            bool hasRejections = applyResult.Output.Contains("FAILED") || applyResult.Output.Contains("saving rejects");

            if (applyResult.ExitCode == 0 && !hasRejections)
            {
                // Success! Patch applied cleanly - now drop the stash
                System.Diagnostics.Debug.WriteLine("[PopStash] Patch applied cleanly - dropping stash");
                var dropResult = RunGit(repoPath, $"stash drop {stashIndex}");
                System.Diagnostics.Debug.WriteLine($"[PopStash] Drop result: exit={dropResult.ExitCode}");

                result.Success = true;
                return result;
            }

            // Patch failed with rejections - try commit-based merge to get proper conflict markers
            if (hasRejections)
            {
                System.Diagnostics.Debug.WriteLine("[PopStash] Patch has rejections - attempting commit-based merge for conflict resolution");

                // Clean up any .rej files created by patch
                CleanupRejectFiles(repoPath);

                // Try commit-based approach to get proper git conflicts
                var mergeResult = TryCommitBasedMerge(repoPath, stashIndex);
                if (mergeResult != null)
                {
                    return mergeResult;
                }

                // Fallback if commit-based merge also fails
                result.ErrorMessage = "Stash conflicts with your local changes. Commit or stash your changes first, then try again.";
                return result;
            }

            // Patch failed - check for actual git conflicts (shouldn't happen with patch, but check anyway)
            var conflicts = GetConflictFiles(repoPath);
            if (conflicts.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine("[PopStash] CONFLICTS: Merge conflicts detected - dropping stash");
                RunGit(repoPath, $"stash drop {stashIndex}");

                result.HasConflicts = true;
                result.ConflictingFiles = conflicts;
                result.ErrorMessage = "Merge conflicts detected - resolve to complete";
                return result;
            }

            // Patch failed for unknown reason - fall back to simple pop for error message
            System.Diagnostics.Debug.WriteLine("[PopStash] Patch apply failed - falling back to simple pop for error message");
            return SimplePopStash(repoPath, stashIndex);
        });
    }

    private static Models.MergeResult SimplePopStash(string repoPath, int stashIndex)
    {
        var result = new Models.MergeResult();

        var popResult = RunGit(repoPath, $"stash pop {stashIndex}");
        System.Diagnostics.Debug.WriteLine($"[SimplePopStash] Result: exit={popResult.ExitCode}, output={popResult.Output}, error={popResult.Error}");

        var conflicts = GetConflictFiles(repoPath);

        if (popResult.ExitCode == 0 && conflicts.Count == 0)
        {
            result.Success = true;
        }
        else if (conflicts.Count > 0)
        {
            result.HasConflicts = true;
            result.ConflictingFiles = conflicts;
            result.ErrorMessage = "Stash pop resulted in merge conflicts";
        }
        else
        {
            result.ErrorMessage = !string.IsNullOrEmpty(popResult.Error) ? popResult.Error.Trim() : popResult.Output.Trim();
            if (string.IsNullOrEmpty(result.ErrorMessage))
            {
                result.ErrorMessage = $"git stash pop failed with exit code {popResult.ExitCode}";
            }
        }

        return result;
    }

    public async Task<List<StashInfo>> GetStashesAsync(string repoPath)
    {
        return await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var stashes = new List<StashInfo>();

            int index = 0;
            foreach (var stash in repo.Stashes)
            {
                // Stash.WorkTree contains the commit with the stashed changes
                var workTreeCommit = stash.WorkTree;
                stashes.Add(new StashInfo
                {
                    Sha = workTreeCommit.Sha,
                    Index = index,
                    Message = stash.Message,
                    Author = workTreeCommit.Author.Name,
                    Date = workTreeCommit.Author.When,
                    BranchName = ExtractBranchFromStashMessage(stash.Message)
                });
                index++;
            }

            return stashes;
        });
    }

    /// <summary>
    /// Extract branch name from stash message (format: "WIP on branch: ..." or "On branch: ...").
    /// </summary>
    private static string ExtractBranchFromStashMessage(string message)
    {
        // Stash messages typically have format: "WIP on branch: commit message" or "On branch: message"
        if (string.IsNullOrEmpty(message))
            return string.Empty;

        // Try "WIP on branch:" format
        const string wipPrefix = "WIP on ";
        const string onPrefix = "On ";

        string? branch = null;
        if (message.StartsWith(wipPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var afterPrefix = message[wipPrefix.Length..];
            var colonIndex = afterPrefix.IndexOf(':');
            if (colonIndex > 0)
            {
                branch = afterPrefix[..colonIndex];
            }
        }
        else if (message.StartsWith(onPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var afterPrefix = message[onPrefix.Length..];
            var colonIndex = afterPrefix.IndexOf(':');
            if (colonIndex > 0)
            {
                branch = afterPrefix[..colonIndex];
            }
        }

        return branch ?? string.Empty;
    }

    public async Task DeleteStashAsync(string repoPath, int stashIndex)
    {
        await Task.Run(() =>
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"stash drop {stashIndex}",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start git process");
            }

            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Failed to delete stash: {error.Trim()}");
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

    public async Task<string> GetWorkingChangesPatchAsync(string repoPath)
    {
        return await Task.Run(() =>
        {
            var staged = RunGit(repoPath, "diff --cached");
            var unstaged = RunGit(repoPath, "diff");

            var builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(staged.Output))
            {
                builder.AppendLine("# Staged changes");
                builder.AppendLine(staged.Output.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(unstaged.Output))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.AppendLine("# Unstaged changes");
                builder.AppendLine(unstaged.Output.TrimEnd());
            }

            return builder.ToString();
        });
    }

    public async Task<string> GetStagedSummaryAsync(string repoPath, int maxFiles = 100, int maxDiffChars = 50000)
    {
        return await Task.Run(() =>
        {
            var status = RunGit(repoPath, "status -sb");
            var stat = RunGit(repoPath, "diff --cached --stat");
            var names = RunGit(repoPath, "diff --cached --name-only");
            var diff = RunGit(repoPath, "diff --cached");

            var builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(status.Output))
            {
                builder.AppendLine("Status:");
                builder.AppendLine(status.Output.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(stat.Output))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }
                builder.AppendLine("Staged diff stats:");
                builder.AppendLine(stat.Output.TrimEnd());
            }

            var files = names.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .Take(Math.Max(1, maxFiles))
                .ToList();

            if (files.Count > 0)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }
                builder.AppendLine("Staged files:");
                foreach (var file in files)
                {
                    builder.AppendLine($"- {file}");
                }
            }

            // Include actual diff content (truncated if too large)
            if (!string.IsNullOrWhiteSpace(diff.Output))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }
                builder.AppendLine("Staged diff:");
                var diffContent = diff.Output.TrimEnd();
                if (diffContent.Length > maxDiffChars)
                {
                    builder.AppendLine(diffContent[..maxDiffChars]);
                    builder.AppendLine($"... (truncated, {diffContent.Length - maxDiffChars} more characters)");
                }
                else
                {
                    builder.AppendLine(diffContent);
                }
            }

            return builder.ToString().TrimEnd();
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

    public async Task UntrackFileAsync(string repoPath, string filePath)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            if (repo.Index[filePath] == null)
                return;

            repo.Index.Remove(filePath);
            repo.Index.Write();
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

    public async Task<List<ConflictInfo>> GetConflictsAsync(string repoPath)
    {
        return await Task.Run(() =>
        {
            System.Diagnostics.Debug.WriteLine($"[GitService] GetConflictsAsync repo={repoPath}");
            var conflicts = new List<ConflictInfo>();
            var conflictPaths = new List<string>();

            // Use git diff to find unmerged files (more reliable than LibGit2Sharp for stash conflicts)
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "diff --name-only --diff-filter=U",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null) return conflicts;

            string output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                conflictPaths.AddRange(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            }
            System.Diagnostics.Debug.WriteLine($"[GitService] diff --name-only --diff-filter=U => {conflictPaths.Count}");

            if (conflictPaths.Count == 0)
            {
                var statusResult = RunGit(repoPath, "status --porcelain");
                if (statusResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(statusResult.Output))
                {
                    foreach (var line in statusResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.Length < 3)
                            continue;

                        var status = line[..2];
                        if (!status.Contains('U', StringComparison.Ordinal))
                            continue;

                        var path = line[3..].Trim();
                        if (!string.IsNullOrEmpty(path))
                        {
                            conflictPaths.Add(path);
                        }
                    }
                }
            }
            System.Diagnostics.Debug.WriteLine($"[GitService] status --porcelain U => {conflictPaths.Count}");

            using var repo = new Repository(repoPath);

            if (conflictPaths.Count == 0)
            {
                conflictPaths.AddRange(repo.Index.Conflicts
                    .Select(c => c.Ancestor?.Path ?? c.Ours?.Path ?? c.Theirs?.Path)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p!));
            }
            System.Diagnostics.Debug.WriteLine($"[GitService] index conflicts => {conflictPaths.Count}");

            foreach (var filePath in conflictPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var trimmedPath = filePath.Trim();
                if (string.IsNullOrEmpty(trimmedPath)) continue;

                var conflictInfo = new ConflictInfo
                {
                    FilePath = trimmedPath,
                    IsResolved = false
                };

                conflictInfo.BaseContent = ReadConflictStage(repoPath, trimmedPath, 1);
                conflictInfo.OursContent = ReadConflictStage(repoPath, trimmedPath, 2);
                conflictInfo.TheirsContent = ReadConflictStage(repoPath, trimmedPath, 3);

                // Try to get content from LibGit2Sharp index conflicts
                var indexConflict = repo.Index.Conflicts[trimmedPath];
                if (indexConflict != null)
                {
                    if (indexConflict.Ours != null)
                    {
                        var blob = repo.Lookup<Blob>(indexConflict.Ours.Id);
                        conflictInfo.OursContent = blob?.GetContentText() ?? "";
                    }

                    if (indexConflict.Theirs != null)
                    {
                        var blob = repo.Lookup<Blob>(indexConflict.Theirs.Id);
                        conflictInfo.TheirsContent = blob?.GetContentText() ?? "";
                    }

                    if (indexConflict.Ancestor != null)
                    {
                        var blob = repo.Lookup<Blob>(indexConflict.Ancestor.Id);
                        conflictInfo.BaseContent = blob?.GetContentText() ?? "";
                    }
                }
                else
                {
                    // Fallback: read the file with conflict markers and try to get HEAD version
                    var fullPath = System.IO.Path.Combine(repoPath, trimmedPath);
                    if (System.IO.File.Exists(fullPath))
                    {
                        conflictInfo.MergedContent = System.IO.File.ReadAllText(fullPath);
                    }

                    // Try to get HEAD version as "ours"
                    try
                    {
                        var headCommit = repo.Head.Tip;
                        var treeEntry = headCommit?[trimmedPath];
                        if (treeEntry?.Target is Blob headBlob)
                        {
                            conflictInfo.OursContent = headBlob.GetContentText();
                        }
                    }
                    catch { /* Ignore - file might be new */ }
                }

                conflicts.Add(conflictInfo);
            }

            return conflicts;
        });
    }

    private static string ReadConflictStage(string repoPath, string filePath, int stage)
    {
        var result = RunGit(repoPath, $"show :{stage}:\"{filePath}\"");
        return result.ExitCode == 0 ? result.Output : string.Empty;
    }

    public async Task ResolveConflictWithOursAsync(string repoPath, string filePath)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            // Use git checkout --ours to resolve with our version
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"checkout --ours \"{filePath}\"",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            process?.WaitForExit();

            // Stage the resolved file
            Commands.Stage(repo, filePath);
        });
    }

    public async Task ResolveConflictWithTheirsAsync(string repoPath, string filePath)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            // Use git checkout --theirs to resolve with their version
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"checkout --theirs \"{filePath}\"",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            process?.WaitForExit();

            // Stage the resolved file
            Commands.Stage(repo, filePath);
        });
    }

    public async Task MarkConflictResolvedAsync(string repoPath, string filePath)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            // Stage the file to mark it as resolved
            Commands.Stage(repo, filePath);
        });
    }

    public async Task ReopenConflictAsync(string repoPath, string filePath, string baseContent, string oursContent, string theirsContent)
    {
        await Task.Run(() =>
        {
            var baseResult = RunGitWithInput(repoPath, "hash-object -w --stdin", baseContent ?? string.Empty);
            var oursResult = RunGitWithInput(repoPath, "hash-object -w --stdin", oursContent ?? string.Empty);
            var theirsResult = RunGitWithInput(repoPath, "hash-object -w --stdin", theirsContent ?? string.Empty);

            if (baseResult.ExitCode != 0 || oursResult.ExitCode != 0 || theirsResult.ExitCode != 0)
            {
                System.Diagnostics.Debug.WriteLine($"[GitService] Failed to create conflict blobs: {baseResult.Error} {oursResult.Error} {theirsResult.Error}");
                return;
            }

            var baseSha = baseResult.Output.Trim();
            var oursSha = oursResult.Output.Trim();
            var theirsSha = theirsResult.Output.Trim();

            var indexInfo = $"100644 {baseSha} 1\t{filePath}\n" +
                            $"100644 {oursSha} 2\t{filePath}\n" +
                            $"100644 {theirsSha} 3\t{filePath}\n";

            var indexResult = RunGitWithInput(repoPath, "update-index --index-info", indexInfo);
            if (indexResult.ExitCode != 0)
            {
                System.Diagnostics.Debug.WriteLine($"[GitService] Failed to restore conflict index: {indexResult.Error}");
                return;
            }

            RunGit(repoPath, $"checkout --conflict=merge \"{filePath}\"");
        });
    }

    public async Task<List<ConflictInfo>> GetResolvedMergeFilesAsync(string repoPath)
    {
        return await Task.Run(() =>
        {
            var unresolvedResult = RunGit(repoPath, "diff --name-only --diff-filter=U");
            var unresolved = unresolvedResult.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var stagedResult = RunGit(repoPath, "diff --name-only --cached");
            var stagedFiles = stagedResult.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var conflictFilesFromMergeMsg = GetMergeConflictFilesFromMessage(repoPath);
            var storedFiles = GetStoredMergeConflictFiles(repoPath);
            var candidates = conflictFilesFromMergeMsg
                .Concat(storedFiles)
                .Concat(stagedFiles)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(file => !unresolved.Contains(file));

            var resolvedFiles = new List<ConflictInfo>();
            foreach (var file in candidates)
            {
                var (baseContent, oursContent, theirsContent) = GetMergeSideContents(repoPath, file);
                resolvedFiles.Add(new ConflictInfo
                {
                    FilePath = file,
                    BaseContent = baseContent,
                    OursContent = oursContent,
                    TheirsContent = theirsContent,
                    IsResolved = true
                });
            }

            return resolvedFiles;
        });
    }

    public async Task<List<string>> GetStoredMergeConflictFilesAsync(string repoPath)
    {
        return await Task.Run(() => GetStoredMergeConflictFiles(repoPath));
    }

    public async Task SaveStoredMergeConflictFilesAsync(string repoPath, IEnumerable<string> files)
    {
        await Task.Run(() => SaveStoredMergeConflictFiles(repoPath, files));
    }

    public async Task ClearStoredMergeConflictFilesAsync(string repoPath)
    {
        await Task.Run(() =>
        {
            var path = GetStoredMergeConflictPath(repoPath);
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        });
    }

    public async Task CompleteMergeAsync(string repoPath, string commitMessage)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            // Get signature from config
            var signature = repo.Config.BuildSignature(DateTimeOffset.Now);

            // Create the merge commit
            repo.Commit(commitMessage, signature, signature);
        });
    }

    public async Task AbortMergeAsync(string repoPath)
    {
        await Task.Run(() =>
        {
            // Use git merge --abort
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "merge --abort",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            process?.WaitForExit();
        });
    }

    public async Task OpenConflictInVsCodeAsync(string repoPath, string filePath)
    {
        await Task.Run(async () =>
        {
            var conflicts = await GetConflictsAsync(repoPath);
            var conflict = conflicts.FirstOrDefault(c => c.FilePath == filePath);
            
            if (conflict == null)
            {
                throw new InvalidOperationException($"Conflict for file '{filePath}' not found.");
            }

            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LeafMerge", Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(tempDir);

            var fileName = System.IO.Path.GetFileName(filePath);
            var extension = System.IO.Path.GetExtension(filePath);
            
            var basePath = System.IO.Path.Combine(tempDir, $"{fileName}.base{extension}");
            var localPath = System.IO.Path.Combine(tempDir, $"{fileName}.local{extension}");
            var remotePath = System.IO.Path.Combine(tempDir, $"{fileName}.remote{extension}");
            var mergedPath = System.IO.Path.Combine(tempDir, $"{fileName}{extension}"); // Result file

            // Write contents
            await System.IO.File.WriteAllTextAsync(basePath, conflict.BaseContent);
            await System.IO.File.WriteAllTextAsync(localPath, conflict.OursContent);
            await System.IO.File.WriteAllTextAsync(remotePath, conflict.TheirsContent);
            
            // For the merge result, start with the file content (which has markers) or ours
            // VS Code usually wants the file to write to.
            // Copying the current file (with markers) gives context, but base is cleaner.
            // Let's copy the file from the repo which has markers.
            var repoFilePath = System.IO.Path.Combine(repoPath, filePath);
            if (System.IO.File.Exists(repoFilePath))
            {
                System.IO.File.Copy(repoFilePath, mergedPath, true);
            }
            else
            {
                await System.IO.File.WriteAllTextAsync(mergedPath, conflict.OursContent);
            }

            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "code",
                    Arguments = $"-n --wait --merge \"{basePath}\" \"{localPath}\" \"{remotePath}\" \"{mergedPath}\"",
                    UseShellExecute = true, // Use shell execute to find 'code' in PATH
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                {
                    throw new InvalidOperationException("Failed to launch VS Code.");
                }

                await process.WaitForExitAsync();

                // If process exited successfully, copy result back
                if (process.ExitCode == 0)
                {
                    if (System.IO.File.Exists(mergedPath))
                    {
                        var mergedContent = await System.IO.File.ReadAllTextAsync(mergedPath);
                        await System.IO.File.WriteAllTextAsync(repoFilePath, mergedContent);
                        
                        // Stage the resolved file
                        using var repo = new Repository(repoPath);
                        Commands.Stage(repo, filePath);
                    }
                }
            }
            finally
            {
                // Cleanup
                try 
                { 
                    System.IO.Directory.Delete(tempDir, true); 
                } 
                catch { /* Ignore cleanup errors */ }
            }
        });
    }

    public async Task<Models.MergeResult> MergeBranchAsync(string repoPath, string branchName, bool allowUnrelatedHistories = false)
    {
        return await Task.Run(() =>
        {
            // Always use --no-ff to create merge commit with visible merge lines in git graph
            var args = $"merge --no-ff \"{branchName}\"";
            if (allowUnrelatedHistories)
            {
                args += " --allow-unrelated-histories";
            }

            System.Diagnostics.Debug.WriteLine($"[GitService] Merging {branchName} in {repoPath} (allowUnrelatedHistories={allowUnrelatedHistories})");
            var result = RunGit(repoPath, args);
            System.Diagnostics.Debug.WriteLine($"[GitService] Merge output: {result.Output}");
            System.Diagnostics.Debug.WriteLine($"[GitService] Merge error: {result.Error}");
            System.Diagnostics.Debug.WriteLine($"[GitService] Merge exit code: {result.ExitCode}");

            if (result.ExitCode == 0)
            {
                return new Models.MergeResult { Success = true };
            }

            // Check for unrelated histories error
            if (result.Error.Contains("refusing to merge unrelated histories", StringComparison.OrdinalIgnoreCase))
            {
                return new Models.MergeResult
                {
                    Success = false,
                    HasUnrelatedHistories = true,
                    ErrorMessage = "Unrelated histories detected."
                };
            }

            // Check if there are conflicts
            if (result.Output.Contains("CONFLICT") || result.Error.Contains("CONFLICT"))
            {
                return new Models.MergeResult
                {
                    Success = false,
                    HasConflicts = true,
                    ErrorMessage = "Merge resulted in conflicts that need to be resolved."
                };
            }

            // Some other failure
            return new Models.MergeResult
            {
                Success = false,
                ErrorMessage = result.Error
            };
        });
    }

    public async Task<Models.MergeResult> FastForwardAsync(string repoPath, string targetBranchName)
    {
        return await Task.Run(() =>
        {
            // Use --ff-only to ensure we only fast-forward (no merge commit)
            var args = $"merge --ff-only \"{targetBranchName}\"";

            System.Diagnostics.Debug.WriteLine($"[GitService] Fast-forwarding to {targetBranchName} in {repoPath}");
            var result = RunGit(repoPath, args);
            System.Diagnostics.Debug.WriteLine($"[GitService] Fast-forward output: {result.Output}");
            System.Diagnostics.Debug.WriteLine($"[GitService] Fast-forward error: {result.Error}");
            System.Diagnostics.Debug.WriteLine($"[GitService] Fast-forward exit code: {result.ExitCode}");

            if (result.ExitCode == 0)
            {
                return new Models.MergeResult { Success = true };
            }

            // Check if fast-forward is not possible (branches have diverged)
            if (result.Error.Contains("Not possible to fast-forward", StringComparison.OrdinalIgnoreCase) ||
                result.Output.Contains("Not possible to fast-forward", StringComparison.OrdinalIgnoreCase))
            {
                return new Models.MergeResult
                {
                    Success = false,
                    ErrorMessage = "Cannot fast-forward: branches have diverged. Use merge instead."
                };
            }

            // Some other failure
            return new Models.MergeResult
            {
                Success = false,
                ErrorMessage = string.IsNullOrEmpty(result.Error) ? result.Output : result.Error
            };
        });
    }

    public async Task CleanupTempStashAsync(string repoPath)
    {
        await Task.Run(() =>
        {
            // Find and drop any TEMP_LEAF_AUTOPOP stash left over from smart pop
            var listResult = RunGit(repoPath, "stash list");
            var lines = listResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(TempStashMessage))
                {
                    RunGit(repoPath, $"stash drop {i}");
                    break;
                }
            }
        });
    }

    #region Git CLI Helpers

    private const string TempStashMessage = "TEMP_LEAF_AUTOPOP";

    private record GitResult(int ExitCode, string Output, string Error);

    private static GitResult RunGit(string workingDirectory, string arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Force English output for consistent error message parsing
        startInfo.EnvironmentVariables["LC_ALL"] = "C";

        using var process = System.Diagnostics.Process.Start(startInfo);
        if (process == null)
        {
            return new GitResult(-1, "", "Failed to start git process");
        }

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new GitResult(process.ExitCode, output, error);
    }

    private static GitResult RunGitWithInput(string workingDirectory, string arguments, string input)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Force English output for consistent error message parsing
        startInfo.EnvironmentVariables["LC_ALL"] = "C";

        using var process = System.Diagnostics.Process.Start(startInfo);
        if (process == null)
        {
            return new GitResult(-1, "", "Failed to start git process");
        }

        // Write the input to stdin
        process.StandardInput.Write(input);
        process.StandardInput.Close();

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new GitResult(process.ExitCode, output, error);
    }

    private static GitResult RunPatchWithInput(string workingDirectory, string patchContent)
    {
        // Find patch.exe from Git installation (it's in usr/bin relative to git)
        string? patchPath = FindPatchExecutable();
        if (patchPath == null)
        {
            return new GitResult(-1, "",
                "Could not find patch.exe. Smart stash pop requires Git for Windows to be installed. " +
                "Download from https://git-scm.com/download/win");
        }

        // Use 'patch' command with fuzz factor for fuzzy matching
        // This allows applying patches even when local changes exist on nearby lines
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = patchPath,
            Arguments = "-p1 --fuzz=3 --no-backup",
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(startInfo);
        if (process == null)
        {
            return new GitResult(-1, "", "Failed to start patch process");
        }

        // Write the patch to stdin
        process.StandardInput.Write(patchContent);
        process.StandardInput.Close();

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new GitResult(process.ExitCode, output, error);
    }

    private static string? FindPatchExecutable()
    {
        // Try common Git for Windows installation paths
        string[] possiblePaths =
        [
            @"C:\Program Files\Git\usr\bin\patch.exe",
            @"C:\Program Files (x86)\Git\usr\bin\patch.exe",
        ];

        foreach (var path in possiblePaths)
        {
            if (System.IO.File.Exists(path))
                return path;
        }

        // Try to find git.exe and derive patch.exe location from it
        var gitResult = RunGit(".", "--exec-path");
        if (gitResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(gitResult.Output))
        {
            // git --exec-path returns something like "C:/Program Files/Git/mingw64/libexec/git-core"
            // patch.exe is in "C:/Program Files/Git/usr/bin/patch.exe"
            var execPath = gitResult.Output.Trim().Replace('/', '\\');
            var gitRoot = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(execPath)));
            if (gitRoot != null)
            {
                var patchPath = System.IO.Path.Combine(gitRoot, "usr", "bin", "patch.exe");
                if (System.IO.File.Exists(patchPath))
                    return patchPath;
            }
        }

        return null;
    }

    private static bool HasUncommittedChanges(string repoPath)
    {
        var result = RunGit(repoPath, "status --porcelain");
        return !string.IsNullOrWhiteSpace(result.Output);
    }

    private static void CleanupRejectFiles(string repoPath)
    {
        // Find and delete any .rej files created by patch
        try
        {
            foreach (var rejFile in System.IO.Directory.GetFiles(repoPath, "*.rej", System.IO.SearchOption.AllDirectories))
            {
                System.IO.File.Delete(rejFile);
                System.Diagnostics.Debug.WriteLine($"[CleanupRejectFiles] Deleted {rejFile}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CleanupRejectFiles] Error cleaning up .rej files: {ex.Message}");
        }
    }

    private static Models.MergeResult? TryCommitBasedMerge(string repoPath, int stashIndex)
    {
        // Approach: stash local -> apply target -> stage -> apply local stash -> get conflicts
        System.Diagnostics.Debug.WriteLine("[TryCommitBasedMerge] Starting commit-based merge approach");

        // Step 1: Stash local changes temporarily
        var tempStashResult = RunGit(repoPath, $"stash push -m \"{TempStashMessage}\"");
        if (tempStashResult.ExitCode != 0)
        {
            System.Diagnostics.Debug.WriteLine($"[TryCommitBasedMerge] Failed to create temp stash: {tempStashResult.Error}");
            return null;
        }
        System.Diagnostics.Debug.WriteLine("[TryCommitBasedMerge] Created temp stash for local changes");

        // Target stash index shifted by +1 since we added TEMP at index 0
        int adjustedIndex = stashIndex + 1;

        // Step 2: Apply target stash (working dir is now clean)
        var applyTargetResult = RunGit(repoPath, $"stash apply {adjustedIndex}");
        if (applyTargetResult.ExitCode != 0)
        {
            System.Diagnostics.Debug.WriteLine($"[TryCommitBasedMerge] Failed to apply target stash: {applyTargetResult.Error}");
            // Restore local changes
            RunGit(repoPath, "stash pop 0");
            return null;
        }
        System.Diagnostics.Debug.WriteLine("[TryCommitBasedMerge] Applied target stash");

        // Step 3: Stage all changes from target stash
        RunGit(repoPath, "add -A");
        System.Diagnostics.Debug.WriteLine("[TryCommitBasedMerge] Staged target stash changes");

        // Step 4: Apply temp stash (local changes) - this should attempt merge
        var applyTempResult = RunGit(repoPath, "stash apply 0");
        System.Diagnostics.Debug.WriteLine($"[TryCommitBasedMerge] Apply temp result: exit={applyTempResult.ExitCode}, error={applyTempResult.Error}");

        // Check for conflicts
        var conflicts = GetConflictFiles(repoPath);
        System.Diagnostics.Debug.WriteLine($"[TryCommitBasedMerge] Conflicts found: {conflicts.Count}");

        if (conflicts.Count > 0)
        {
            // Success! We have proper git conflicts that can be resolved
            // Drop the target stash since its changes are now in the working dir
            RunGit(repoPath, $"stash drop {adjustedIndex}");
            // Keep TEMP stash - will be cleaned up after conflict resolution
            System.Diagnostics.Debug.WriteLine("[TryCommitBasedMerge] Conflicts created successfully");

            return new Models.MergeResult
            {
                HasConflicts = true,
                ConflictingFiles = conflicts,
                ErrorMessage = "Merge conflicts detected - resolve to complete"
            };
        }

        if (applyTempResult.ExitCode == 0)
        {
            // No conflicts - both applied cleanly
            // Drop both stashes
            RunGit(repoPath, $"stash drop {adjustedIndex}"); // Drop target
            RunGit(repoPath, "stash drop 0"); // Drop temp
            System.Diagnostics.Debug.WriteLine("[TryCommitBasedMerge] Both stashes applied cleanly");

            return new Models.MergeResult { Success = true };
        }

        // Apply failed but no conflicts - something else went wrong
        // Try to restore original state
        System.Diagnostics.Debug.WriteLine("[TryCommitBasedMerge] Apply failed without conflicts - restoring state");
        RunGit(repoPath, "reset --hard HEAD");
        RunGit(repoPath, "stash pop 0"); // Restore local changes
        return null;
    }

    private static List<string> GetConflictFiles(string repoPath)
    {
        var result = RunGit(repoPath, "diff --name-only --diff-filter=U");
        return result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .Where(f => !string.IsNullOrEmpty(f))
            .ToList();
    }

    #endregion

    private static (string baseContent, string oursContent, string theirsContent) GetMergeSideContents(string repoPath, string filePath)
    {
        var oursContent = GetRefFileContent(repoPath, "HEAD", filePath);
        var theirsContent = GetRefFileContent(repoPath, "MERGE_HEAD", filePath);

        var baseShaResult = RunGit(repoPath, "merge-base HEAD MERGE_HEAD");
        var baseSha = baseShaResult.ExitCode == 0 ? baseShaResult.Output.Trim() : string.Empty;
        var baseContent = string.IsNullOrEmpty(baseSha)
            ? string.Empty
            : GetRefFileContent(repoPath, baseSha, filePath);

        return (baseContent, oursContent, theirsContent);
    }

    private static string GetRefFileContent(string repoPath, string refName, string filePath)
    {
        var result = RunGit(repoPath, $"show {refName}:\"{filePath}\"");
        return result.ExitCode == 0 ? result.Output : string.Empty;
    }

    private static List<string> GetMergeConflictFilesFromMessage(string repoPath)
    {
        try
        {
            using var repo = new Repository(repoPath);
            var mergeMessagePath = System.IO.Path.Combine(repo.Info.Path, "MERGE_MSG");
            if (!System.IO.File.Exists(mergeMessagePath))
            {
                return [];
            }

            var lines = System.IO.File.ReadAllLines(mergeMessagePath);
            var results = new List<string>();
            var inConflicts = false;

            foreach (var line in lines)
            {
                if (!inConflicts)
                {
                    if (line.StartsWith("Conflicts:", StringComparison.OrdinalIgnoreCase))
                    {
                        inConflicts = true;
                    }

                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    break;
                }

                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                results.Add(trimmed);
            }

            return results;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GitService] Failed to read MERGE_MSG: {ex.Message}");
            return [];
        }
    }

    private static string GetStoredMergeConflictPath(string repoPath)
    {
        using var repo = new Repository(repoPath);
        return System.IO.Path.Combine(repo.Info.Path, "leaf-merge-conflicts.txt");
    }

    private static List<string> GetStoredMergeConflictFiles(string repoPath)
    {
        try
        {
            var path = GetStoredMergeConflictPath(repoPath);
            if (!System.IO.File.Exists(path))
            {
                return [];
            }

            return System.IO.File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrEmpty(line))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GitService] Failed to read stored merge conflicts: {ex.Message}");
            return [];
        }
    }

    private static void SaveStoredMergeConflictFiles(string repoPath, IEnumerable<string> files)
    {
        try
        {
            var path = GetStoredMergeConflictPath(repoPath);
            var lines = files
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            System.IO.File.WriteAllLines(path, lines);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GitService] Failed to store merge conflicts: {ex.Message}");
        }
    }

    #region Branch Deletion

    public Task DeleteBranchAsync(string repoPath, string branchName, bool force = false)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var branch = repo.Branches[branchName];
            if (branch == null)
            {
                throw new InvalidOperationException($"Branch '{branchName}' not found.");
            }

            if (branch.IsCurrentRepositoryHead)
            {
                throw new InvalidOperationException("Cannot delete the currently checked out branch.");
            }

            repo.Branches.Remove(branch);
        });
    }

    public Task DeleteRemoteBranchAsync(string repoPath, string remoteName, string branchName,
        string? username = null, string? password = null)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var remote = repo.Network.Remotes[remoteName];
            if (remote == null)
            {
                throw new InvalidOperationException($"Remote '{remoteName}' not found.");
            }

            var options = new PushOptions();
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                options.CredentialsProvider = (_, _, _) =>
                    new UsernamePasswordCredentials { Username = username, Password = password };
            }

            // Push empty refspec to delete remote branch
            var refspec = $":refs/heads/{branchName}";
            repo.Network.Push(remote, refspec, options);
        });
    }

    #endregion

    #region Tag Operations

    public Task<List<TagInfo>> GetTagsAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var tags = new List<TagInfo>();

            foreach (var tag in repo.Tags)
            {
                var tagInfo = new TagInfo
                {
                    Name = tag.FriendlyName,
                    TargetSha = tag.Target.Sha,
                    IsAnnotated = tag.IsAnnotated
                };

                if (tag.IsAnnotated && tag.Annotation != null)
                {
                    tagInfo.Message = tag.Annotation.Message;
                    tagInfo.TaggerName = tag.Annotation.Tagger?.Name;
                    tagInfo.TaggerEmail = tag.Annotation.Tagger?.Email;
                    tagInfo.TaggedAt = tag.Annotation.Tagger?.When;
                }

                tags.Add(tagInfo);
            }

            return tags.OrderByDescending(t => t.TaggedAt ?? DateTimeOffset.MinValue).ToList();
        });
    }

    public Task CreateTagAsync(string repoPath, string tagName, string? message = null, string? targetSha = null)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            var target = string.IsNullOrEmpty(targetSha)
                ? repo.Head.Tip
                : repo.Lookup<Commit>(targetSha);

            if (target == null)
            {
                throw new InvalidOperationException($"Target commit '{targetSha}' not found.");
            }

            if (repo.Tags[tagName] != null)
            {
                throw new InvalidOperationException($"Tag '{tagName}' already exists.");
            }

            if (!string.IsNullOrEmpty(message))
            {
                // Create annotated tag
                var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
                repo.ApplyTag(tagName, target.Sha, signature, message);
            }
            else
            {
                // Create lightweight tag
                repo.ApplyTag(tagName, target.Sha);
            }
        });
    }

    public Task DeleteTagAsync(string repoPath, string tagName)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var tag = repo.Tags[tagName];
            if (tag == null)
            {
                throw new InvalidOperationException($"Tag '{tagName}' not found.");
            }

            repo.Tags.Remove(tag);
        });
    }

    public Task PushTagAsync(string repoPath, string tagName, string remoteName = "origin",
        string? username = null, string? password = null)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var remote = repo.Network.Remotes[remoteName];
            if (remote == null)
            {
                throw new InvalidOperationException($"Remote '{remoteName}' not found.");
            }

            var tag = repo.Tags[tagName];
            if (tag == null)
            {
                throw new InvalidOperationException($"Tag '{tagName}' not found.");
            }

            var options = new PushOptions();
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                options.CredentialsProvider = (_, _, _) =>
                    new UsernamePasswordCredentials { Username = username, Password = password };
            }

            var refspec = $"refs/tags/{tagName}:refs/tags/{tagName}";
            repo.Network.Push(remote, refspec, options);
        });
    }

    public Task DeleteRemoteTagAsync(string repoPath, string tagName, string remoteName = "origin",
        string? username = null, string? password = null)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var remote = repo.Network.Remotes[remoteName];
            if (remote == null)
            {
                throw new InvalidOperationException($"Remote '{remoteName}' not found.");
            }

            var options = new PushOptions();
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                options.CredentialsProvider = (_, _, _) =>
                    new UsernamePasswordCredentials { Username = username, Password = password };
            }

            // Push empty refspec to delete remote tag
            var refspec = $":refs/tags/{tagName}";
            repo.Network.Push(remote, refspec, options);
        });
    }

    #endregion

    #region Rebase Operations

    public Task<Models.MergeResult> RebaseAsync(string repoPath, string ontoBranch, IProgress<string>? progress = null)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            var targetBranch = repo.Branches[ontoBranch];
            if (targetBranch == null)
            {
                throw new InvalidOperationException($"Branch '{ontoBranch}' not found.");
            }

            progress?.Report($"Rebasing onto {ontoBranch}...");

            var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
            var options = new RebaseOptions();

            var rebaseResult = repo.Rebase.Start(repo.Head, targetBranch, targetBranch, new Identity(signature.Name, signature.Email), options);

            return rebaseResult.Status switch
            {
                RebaseStatus.Complete => new Models.MergeResult { Success = true },
                RebaseStatus.Conflicts => new Models.MergeResult { Success = false, HasConflicts = true },
                _ => new Models.MergeResult { Success = false, ErrorMessage = $"Rebase status: {rebaseResult.Status}" }
            };
        });
    }

    public Task AbortRebaseAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            repo.Rebase.Abort();
        });
    }

    public Task<Models.MergeResult> ContinueRebaseAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
            var options = new RebaseOptions();

            var result = repo.Rebase.Continue(new Identity(signature.Name, signature.Email), options);

            return result.Status switch
            {
                RebaseStatus.Complete => new Models.MergeResult { Success = true },
                RebaseStatus.Conflicts => new Models.MergeResult { Success = false, HasConflicts = true },
                _ => new Models.MergeResult { Success = false, ErrorMessage = $"Rebase status: {result.Status}" }
            };
        });
    }

    public Task<Models.MergeResult> SkipRebaseCommitAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            // Skip is handled via git command line as LibGit2Sharp doesn't expose it directly
            var result = RunGitCommand(repoPath, "rebase --skip");
            return new Models.MergeResult { Success = result.ExitCode == 0, ErrorMessage = result.Error };
        });
    }

    public Task<bool> IsRebaseInProgressAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            var rebaseApplyPath = System.IO.Path.Combine(repoPath, ".git", "rebase-apply");
            var rebaseMergePath = System.IO.Path.Combine(repoPath, ".git", "rebase-merge");
            return System.IO.Directory.Exists(rebaseApplyPath) || System.IO.Directory.Exists(rebaseMergePath);
        });
    }

    #endregion

    #region Squash Merge

    public Task<Models.MergeResult> SquashMergeAsync(string repoPath, string branchName)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            var sourceBranch = repo.Branches[branchName];
            if (sourceBranch == null)
            {
                throw new InvalidOperationException($"Branch '{branchName}' not found.");
            }

            var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
            var mergeOptions = new MergeOptions
            {
                FastForwardStrategy = FastForwardStrategy.NoFastForward,
                CommitOnSuccess = false // Don't auto-commit for squash
            };

            var result = repo.Merge(sourceBranch, signature, mergeOptions);

            if (result.Status == MergeStatus.Conflicts)
            {
                return new Models.MergeResult { Success = false, HasConflicts = true };
            }

            if (result.Status == MergeStatus.FastForward || result.Status == MergeStatus.NonFastForward)
            {
                // For squash, we need to reset the merge state but keep changes staged
                // This is a simplified implementation - full squash would require additional handling
                return new Models.MergeResult { Success = true };
            }

            return new Models.MergeResult { Success = result.Status == MergeStatus.UpToDate };
        });
    }

    #endregion

    #region Commit Log

    public Task<List<CommitInfo>> GetMergeCommitsAsync(string repoPath, string mergeSha)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var mergeCommit = repo.Lookup<Commit>(mergeSha);
            if (mergeCommit == null)
            {
                return new List<CommitInfo>();
            }

            var parents = mergeCommit.Parents.ToList();
            if (parents.Count < 2)
            {
                return new List<CommitInfo>();
            }

            var mainParent = parents[0];
            var mergedParent = parents[1];
            var mergeBase = repo.ObjectDatabase.FindMergeBase(mainParent, mergedParent);
            if (mergeBase == null)
            {
                return new List<CommitInfo>();
            }

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

    public Task<List<CommitInfo>> GetCommitsBetweenAsync(string repoPath, string fromRef, string? toRef = null)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var commits = new List<CommitInfo>();

            var fromCommit = repo.Lookup<Commit>(fromRef);
            if (fromCommit == null)
            {
                // Try to find as tag
                var tag = repo.Tags[fromRef];
                if (tag != null)
                {
                    fromCommit = tag.Target as Commit;
                    if (fromCommit == null && tag.Target is TagAnnotation annotation)
                    {
                        fromCommit = annotation.Target as Commit;
                    }
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
                        {
                            toCommit = annotation.Target as Commit;
                        }
                    }
                }
            }

            if (toCommit == null)
            {
                return commits;
            }

            // Get all commits from toCommit back to (but not including) fromCommit
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

    public async Task<List<FileBlameLine>> GetFileBlameAsync(string repoPath, string filePath)
    {
        var result = await RunGitCommandAsync(repoPath, "blame", "--line-porcelain", "--", filePath);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Error);
        }

        var lines = new List<FileBlameLine>();
        string currentSha = string.Empty;
        string currentAuthor = string.Empty;
        DateTimeOffset currentDate = DateTimeOffset.MinValue;
        int currentLineNumber = 0;

        foreach (var rawLine in result.Output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

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

            if (line.Length >= 40 && IsShaLine(line))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                currentSha = parts[0];
                if (parts.Length >= 3 && int.TryParse(parts[2], out var finalLine))
                {
                    currentLineNumber = finalLine;
                }
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
                {
                    currentDate = DateTimeOffset.FromUnixTimeSeconds(seconds);
                }
                continue;
            }
        }

        return lines;
    }

    public async Task<List<CommitInfo>> GetFileHistoryAsync(string repoPath, string filePath, int maxCount = 200)
    {
        var result = await RunGitCommandAsync(
            repoPath,
            "log",
            "--follow",
            "--date=iso",
            $"--max-count={maxCount}",
            "--pretty=format:%H%x1f%an%x1f%ad%x1f%s",
            "--",
            filePath);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Error);
        }

        var commits = new List<CommitInfo>();
        foreach (var rawLine in result.Output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('\x1f');
            if (parts.Length < 4)
            {
                continue;
            }

            var sha = parts[0];
            var author = parts[1];
            var dateText = parts[2];
            var message = parts[3];

            if (!DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
            {
                date = DateTimeOffset.Now;
            }

            commits.Add(new CommitInfo
            {
                Sha = sha,
                Message = message,
                MessageShort = message,
                Author = author,
                Date = date
            });
        }

        return commits;
    }

    #endregion

    #region Git Command Helper

    private (int ExitCode, string Output, string Error) RunGitCommand(string repoPath, string arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(startInfo);
        if (process == null)
        {
            var failed = (ExitCode: -1, Output: "", Error: "Failed to start git process");
            OnGitCommandExecuted(repoPath, $"git {arguments}", failed.ExitCode, failed.Output, failed.Error);
            return failed;
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var result = (ExitCode: process.ExitCode, Output: output, Error: error);
        OnGitCommandExecuted(repoPath, $"git {arguments}", result.ExitCode, result.Output, result.Error);
        return result;
    }

    private async Task<(int ExitCode, string Output, string Error)> RunGitCommandAsync(string repoPath, params string[] args)
    {
        return await Task.Run(() =>
        {
            var arguments = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            return RunGitCommand(repoPath, arguments);
        });
    }

    private static bool IsShaLine(string line)
    {
        for (int i = 0; i < 40 && i < line.Length; i++)
        {
            if (!char.IsAsciiHexDigit(line[i]))
            {
                return false;
            }
        }

        return line.Length >= 40;
    }

    private void OnGitCommandExecuted(string workingDirectory, string arguments, int exitCode, string output, string error)
    {
        GitCommandExecuted?.Invoke(this, new GitCommandEventArgs(workingDirectory, arguments, exitCode, output, error));
    }

    #endregion
}
