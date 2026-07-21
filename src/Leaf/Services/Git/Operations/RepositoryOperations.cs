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
    public Task<bool> IsValidRepositoryAsync(string path, CancellationToken cancellationToken = default)
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
        }, cancellationToken);
    }

    /// <summary>
    /// Get repository status information (slow — uses LibGit2Sharp RetrieveStatus).
    /// Prefer <see cref="GetRepositoryInfoFastAsync"/> for performance-critical paths.
    /// </summary>
    [System.Obsolete("Use GetRepositoryInfoFastAsync for performance-critical paths.")]
    public Task<RepositoryInfo> GetRepositoryInfoAsync(string repoPath, CancellationToken cancellationToken = default)
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

            // Detect operation type via the shared sentinel ladder.
            (var operationType, var mergingBranch) =
                DetectInProgressOperation(Path.Combine(repoPath, ".git"));
            int conflictCount = 0;

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
        }, cancellationToken);
    }

    /// <summary>
    /// Get repository status information using fast git CLI commands instead of LibGit2Sharp.
    /// ~20x faster than <see cref="GetRepositoryInfoAsync"/> on large repos.
    /// </summary>
    public Task<RepositoryInfo> GetRepositoryInfoFastAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var sw = Log.StartTimer();
            // Check if bare repo via git CLI
            var revParseResult = GitCliHelpers.RunGitArgs(repoPath, "rev-parse", "--is-bare-repository");
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
            var headResult = GitCliHelpers.RunGitArgs(repoPath, "symbolic-ref", "--short", "HEAD");
            bool isDetached = headResult.ExitCode != 0;
            string? headSha = null;
            string currentBranch;

            if (isDetached)
            {
                var shaResult = GitCliHelpers.RunGitArgs(repoPath, "rev-parse", "HEAD");
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
                var abResult = GitCliHelpers.RunGitArgs(repoPath, "rev-list", "--left-right", "--count", "HEAD...@{upstream}");
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

            // Detect operation type via the shared sentinel ladder.
            (var operationType, var mergingBranch) =
                DetectInProgressOperation(Path.Combine(repoPath, ".git"));
            int conflictCount = 0;
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
        }, cancellationToken);
    }

    /// <summary>
    /// Resolve the top-level working-tree directory for any path inside a
    /// git working tree (<c>git rev-parse --show-toplevel</c>). Throws when
    /// the path is not inside a working tree.
    /// </summary>
    public async Task<string> GetRepositoryRootAsync(string anyPath, CancellationToken cancellationToken = default)
    {
        var result = await _context.CommandRunner.RunAsync(
            anyPath, ["rev-parse", "--show-toplevel"], cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException(
                $"'{anyPath}' is not inside a git working tree: {result.StandardError.Trim()}");

        var toplevel = result.StandardOutput.Trim();
        if (string.IsNullOrEmpty(toplevel))
            throw new InvalidOperationException(
                $"'{anyPath}' has no working tree (bare repository or inside .git).");
        return Path.GetFullPath(toplevel);
    }

    /// <summary>
    /// Resolve the superproject working tree that contains
    /// <paramref name="repoPath"/> as a submodule
    /// (<c>git rev-parse --show-superproject-working-tree</c>).
    /// Returns null when the repo is not a submodule of anything.
    /// </summary>
    public async Task<string?> GetSuperprojectWorkingTreeAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath, ["rev-parse", "--show-superproject-working-tree"], cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException(
                $"Failed to resolve superproject for '{repoPath}': {result.StandardError.Trim()}");

        var superproject = result.StandardOutput.Trim();
        return string.IsNullOrEmpty(superproject) ? null : Path.GetFullPath(superproject);
    }

    /// <summary>
    /// Walk the standard <c>.git</c> sentinel files / directories to figure
    /// out which long-running git operation (if any) is paused on this
    /// repo. Shared by <see cref="GetRepositoryInfoAsync"/> and
    /// <see cref="GetRepositoryInfoFastAsync"/> so the detection ladder
    /// lives in one place — keeps the am-vs-rebase disambiguator in lockstep
    /// with the rest of the rules. Returns the operation type and the label
    /// the UI uses for the "[verb] [preposition] [label]" banner; merge
    /// reads <c>MERGE_MSG</c> for a richer label, others fall back to a
    /// generic word.
    /// </summary>
    /// <param name="gitDir">
    /// The repo's resolved git directory. The current callers pass
    /// <c>Path.Combine(repoPath, ".git")</c> — fine for non-worktree repos;
    /// linked worktrees would need the actual gitdir from
    /// <c>git rev-parse --absolute-git-dir</c>, which is a pre-existing
    /// limitation tracked outside this feature.
    /// </param>
    private (Models.GitOperationType Type, string Label) DetectInProgressOperation(string gitDir)
    {
        if (File.Exists(Path.Combine(gitDir, "MERGE_HEAD")))
        {
            var label = "Incoming";
            var mergeMsgPath = Path.Combine(gitDir, "MERGE_MSG");
            if (File.Exists(mergeMsgPath))
            {
                try
                {
                    var msg = File.ReadAllText(mergeMsgPath);
                    label = _context.OutputParser.ParseMergingBranch(msg);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // MERGE_MSG may be locked or removed by a concurrent git
                    // operation — fall back to the generic label.
                    Log.Info("Repository", $"Skipped MERGE_MSG read at {mergeMsgPath}: {ex.Message}");
                }
            }
            return (Models.GitOperationType.Merge, label);
        }
        if (File.Exists(Path.Combine(gitDir, "rebase-apply", "applying")))
        {
            // git am shares rebase-apply/ with the rebase-apply rebase
            // backend; the `applying` file is the am-only marker. Routing
            // to Am here is what lets the merge editor / abort path call
            // `git am --continue/--abort` instead of the rebase verbs.
            return (Models.GitOperationType.Am, "patch apply");
        }
        if (Directory.Exists(Path.Combine(gitDir, "rebase-merge"))
            || Directory.Exists(Path.Combine(gitDir, "rebase-apply")))
        {
            return (Models.GitOperationType.Rebase, "rebase");
        }
        if (File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD")))
        {
            return (Models.GitOperationType.CherryPick, "cherry-pick");
        }
        if (File.Exists(Path.Combine(gitDir, "REVERT_HEAD")))
        {
            return (Models.GitOperationType.Revert, "revert");
        }
        if (File.Exists(Path.Combine(gitDir, "BISECT_START")))
        {
            // BISECT_START is the canonical bisect-in-progress marker
            // (BISECT_LOG can linger after a crashed bisect; START is
            // written on `git bisect start` and removed on `reset`).
            return (Models.GitOperationType.Bisect, "bisect");
        }
        return (Models.GitOperationType.None, string.Empty);
    }
}
