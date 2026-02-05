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

            var status = repo.RetrieveStatus();
            var isDirty = status.IsDirty;

            var isDetached = repo.Info.IsHeadDetached;
            var headSha = repo.Head?.Tip?.Sha;
            var currentBranch = isDetached
                ? $"HEAD ({headSha?[..7] ?? "detached"})"
                : (repo.Head?.FriendlyName ?? "HEAD");
            var tracking = repo.Head?.TrackingDetails;

            // Check for merge in progress
            bool isMergeInProgress = false;
            string mergingBranch = string.Empty;
            int conflictCount = 0;

            var mergeHeadPath = Path.Combine(repoPath, ".git", "MERGE_HEAD");
            if (File.Exists(mergeHeadPath))
            {
                isMergeInProgress = true;
                mergingBranch = "Incoming";

                var mergeMsgPath = Path.Combine(repoPath, ".git", "MERGE_MSG");
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

            // Count conflicts using git command (more reliable)
            conflictCount = GitCliHelpers.GetConflictCount(repoPath);

            // Fallback to LibGit2Sharp if git command returns 0
            if (conflictCount == 0 && repo.Index.Conflicts.Any())
            {
                conflictCount = repo.Index.Conflicts
                    .Select(c => c.Ancestor?.Path ?? c.Ours?.Path ?? c.Theirs?.Path)
                    .Distinct()
                    .Count();
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
                MergingBranch = mergingBranch,
                ConflictCount = conflictCount,
                IsDetachedHead = isDetached,
                DetachedHeadSha = isDetached ? headSha : null
            };
        });
    }
}
