using System.Diagnostics;
using System.IO;
using Leaf.Models;
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
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Get repository status information.
    /// </summary>
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
                    catch { /* ignore */ }
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
                Debug.WriteLine($"[MERGE][STATE] Orphaned conflicts detected: {conflictCount} files");
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
}
