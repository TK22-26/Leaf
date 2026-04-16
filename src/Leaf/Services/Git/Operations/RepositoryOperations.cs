using System;
using System.IO;
using Leaf.Models;
using Leaf.Services;
using Leaf.Services.Git.Core;
using LibGit2Sharp;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Operations for repository-level queries and information.
/// </summary>
internal class RepositoryOperations
{
    private readonly IGitOperationContext _context;

    public RepositoryOperations(IGitOperationContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Check if a path contains a valid Git repository.
    /// </summary>
    public Task<bool> IsValidRepositoryAsync(string path)
    {
        return Task.Run(() =>
        {
            try
            {
                return Repository.IsValid(path);
            }
            catch (Exception ex) when (ex is LibGit2SharpException
                                    or IOException
                                    or UnauthorizedAccessException
                                    or ArgumentException)
            {
                // Path is missing, unreadable, or not a git working tree.
                Leaf.Services.Log.Info("Repo", $"IsValid({path}) failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        });
    }

    /// <summary>
    /// Get repository status information (slow — uses LibGit2Sharp RetrieveStatus).
    /// Prefer <see cref="GetRepositoryInfoFastAsync"/> for performance-critical paths.
    /// </summary>
    [System.Obsolete("Use GetRepositoryInfoFastAsync for performance-critical paths.")]
    public Task<RepositoryInfo> GetRepositoryInfoAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            if (repo.Info.IsBare)
            {
                return new RepositoryInfo
                {
                    Path = repoPath,
                    Name = Path.GetFileName(repoPath),
                    CurrentBranch = "(bare)",
                    LastAccessed = DateTimeOffset.Now
                };
            }

            var status = repo.RetrieveStatus();
            var isDirty = status.IsDirty;

            var isDetached = repo.Info.IsHeadDetached;
            var headSha = repo.Head?.Tip?.Sha;
            var currentBranch = isDetached
                ? $"HEAD ({headSha?[..7] ?? "detached"})"
                : (repo.Head?.FriendlyName ?? "HEAD");
            var tracking = repo.Head?.TrackingDetails;

            // Detect operation type from .git/ sentinel files
            var operationType = Models.GitOperationType.None;
            string mergingBranch = string.Empty;
            int conflictCount = 0;

            var gitDir = Path.Combine(repoPath, ".git");
            if (File.Exists(Path.Combine(gitDir, "MERGE_HEAD")))
            {
                operationType = Models.GitOperationType.Merge;
                mergingBranch = "Incoming";

                var mergeMsgPath = Path.Combine(gitDir, "MERGE_MSG");
                if (File.Exists(mergeMsgPath))
                {
                    try
                    {
                        var msg = File.ReadAllText(mergeMsgPath);
                        mergingBranch = _context.OutputParser.ParseMergingBranch(msg);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // MERGE_MSG may be locked or removed by a concurrent git
                        // operation — we fall back to the generic "merge" label.
                        // Narrowed + logged per plan §2.2.
                        Log.Info("Repository", $"Skipped MERGE_MSG read at {mergeMsgPath}: {ex.Message}");
                    }
                }
            }
            else if (Directory.Exists(Path.Combine(gitDir, "rebase-merge"))
                     || Directory.Exists(Path.Combine(gitDir, "rebase-apply")))
            {
                operationType = Models.GitOperationType.Rebase;
                mergingBranch = "rebase";
            }
            else if (File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD")))
            {
                operationType = Models.GitOperationType.CherryPick;
                mergingBranch = "cherry-pick";
            }
            else if (File.Exists(Path.Combine(gitDir, "REVERT_HEAD")))
            {
                operationType = Models.GitOperationType.Revert;
                mergingBranch = "revert";
            }

            bool isMergeInProgress = operationType != Models.GitOperationType.None;

            // Count conflicts when an operation is in progress
            if (isMergeInProgress)
            {
                conflictCount = GitCliHelpers.GetConflictCount(repoPath);

                // Fallback to LibGit2Sharp if git command returns 0
                if (conflictCount == 0 && repo.Index.Conflicts.Any())
                {
                    conflictCount = repo.Index.Conflicts
                        .Select(c => c.Ancestor?.Path ?? c.Ours?.Path ?? c.Theirs?.Path)
                        .Distinct()
                        .Count();
                }
            }
            else if (repo.Index.Conflicts.Any())
            {
                // Orphaned conflict state: unmerged entries without any operation sentinel
                conflictCount = repo.Index.Conflicts
                    .Select(c => c.Ancestor?.Path ?? c.Ours?.Path ?? c.Theirs?.Path)
                    .Distinct()
                    .Count();
                Log.Warn("Merge", $"Orphaned conflicts detected: {conflictCount} files");
            }

            return new RepositoryInfo
            {
                Path = repoPath,
                Name = Path.GetFileName(repoPath),
                CurrentBranch = currentBranch,
                IsDirty = isDirty,
                AheadBy = tracking?.AheadBy ?? 0,
                BehindBy = tracking?.BehindBy ?? 0,
                LastAccessed = DateTimeOffset.Now,
                IsMergeInProgress = isMergeInProgress,
                OperationType = operationType,
                MergingBranch = mergingBranch,
                ConflictCount = conflictCount,
                IsDetachedHead = isDetached,
                DetachedHeadSha = isDetached ? headSha : null
            };
        });
    }

    /// <summary>
    /// Get repository status information using fast git CLI commands instead of LibGit2Sharp.
    /// ~20x faster than <see cref="GetRepositoryInfoAsync"/> on large repos.
    /// </summary>
    public Task<RepositoryInfo> GetRepositoryInfoFastAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            var sw = Log.StartTimer();
            // Check if bare repo via git CLI
            var revParseResult = GitCliHelpers.RunGit(repoPath, "rev-parse --is-bare-repository");
            if (revParseResult.ExitCode == 0 && revParseResult.Output.Trim() == "true")
            {
                return new RepositoryInfo
                {
                    Path = repoPath,
                    Name = Path.GetFileName(repoPath),
                    CurrentBranch = "(bare)",
                    LastAccessed = DateTimeOffset.Now
                };
            }

            // Get current branch, detached HEAD state, and HEAD SHA — all from git CLI
            var headResult = GitCliHelpers.RunGit(repoPath, "symbolic-ref --short HEAD");
            bool isDetached = headResult.ExitCode != 0;
            string? headSha = null;
            string currentBranch;

            if (isDetached)
            {
                var shaResult = GitCliHelpers.RunGit(repoPath, "rev-parse HEAD");
                headSha = shaResult.ExitCode == 0 ? shaResult.Output.Trim() : null;
                currentBranch = $"HEAD ({headSha?[..7] ?? "detached"})";
            }
            else
            {
                currentBranch = headResult.Output.Trim();
            }

            // Get ahead/behind counts via git CLI
            int aheadBy = 0, behindBy = 0;
            if (!isDetached)
            {
                var abResult = GitCliHelpers.RunGit(repoPath, "rev-list --left-right --count HEAD...@{upstream}");
                if (abResult.ExitCode == 0)
                {
                    var parts = abResult.Output.Trim().Split('\t');
                    if (parts.Length == 2)
                    {
                        int.TryParse(parts[0], out aheadBy);
                        int.TryParse(parts[1], out behindBy);
                    }
                }
            }

            // Check dirty state via fast porcelain status
            bool isDirty = GitCliHelpers.HasUncommittedChanges(repoPath);

            // Detect operation type from .git/ sentinel files (already fast — file existence checks)
            var operationType = Models.GitOperationType.None;
            string mergingBranch = string.Empty;
            int conflictCount = 0;

            var gitDir = Path.Combine(repoPath, ".git");
            if (File.Exists(Path.Combine(gitDir, "MERGE_HEAD")))
            {
                operationType = Models.GitOperationType.Merge;
                mergingBranch = "Incoming";

                var mergeMsgPath = Path.Combine(gitDir, "MERGE_MSG");
                if (File.Exists(mergeMsgPath))
                {
                    try
                    {
                        var msg = File.ReadAllText(mergeMsgPath);
                        mergingBranch = _context.OutputParser.ParseMergingBranch(msg);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // MERGE_MSG may be locked or removed by a concurrent git
                        // operation — we fall back to the generic "merge" label.
                        // Narrowed + logged per plan §2.2.
                        Log.Info("Repository", $"Skipped MERGE_MSG read at {mergeMsgPath}: {ex.Message}");
                    }
                }
            }
            else if (Directory.Exists(Path.Combine(gitDir, "rebase-merge"))
                     || Directory.Exists(Path.Combine(gitDir, "rebase-apply")))
            {
                operationType = Models.GitOperationType.Rebase;
                mergingBranch = "rebase";
            }
            else if (File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD")))
            {
                operationType = Models.GitOperationType.CherryPick;
                mergingBranch = "cherry-pick";
            }
            else if (File.Exists(Path.Combine(gitDir, "REVERT_HEAD")))
            {
                operationType = Models.GitOperationType.Revert;
                mergingBranch = "revert";
            }

            bool isMergeInProgress = operationType != Models.GitOperationType.None;

            // Count conflicts via git CLI (only when a merge-like operation is in progress)
            if (isMergeInProgress)
            {
                conflictCount = GitCliHelpers.GetConflictCount(repoPath);
            }

            Log.Perf("RepoOps", "GetRepositoryInfoFastAsync", sw.ElapsedMilliseconds);

            return new RepositoryInfo
            {
                Path = repoPath,
                Name = Path.GetFileName(repoPath),
                CurrentBranch = currentBranch,
                IsDirty = isDirty,
                AheadBy = aheadBy,
                BehindBy = behindBy,
                LastAccessed = DateTimeOffset.Now,
                IsMergeInProgress = isMergeInProgress,
                OperationType = operationType,
                MergingBranch = mergingBranch,
                ConflictCount = conflictCount,
                IsDetachedHead = isDetached,
                DetachedHeadSha = isDetached ? headSha : null
            };
        });
    }
}
