using System.Diagnostics;
using System.IO;
using Leaf.Services.Git.Core;
using LibGit2Sharp;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Operations for merging branches.
/// </summary>
internal class MergeOperations
{
    private readonly IGitOperationContext _context;

    public MergeOperations(IGitOperationContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Merge a branch into the current branch.
    /// </summary>
    public Task<Models.MergeResult> MergeBranchAsync(string repoPath, string branchName, bool allowUnrelatedHistories = false)
    {
        return Task.Run(() =>
        {
            // Always use --no-ff to create merge commit with visible merge lines in git graph
            var args = $"merge --no-ff \"{branchName}\"";
            if (allowUnrelatedHistories)
            {
                args += " --allow-unrelated-histories";
            }

            Debug.WriteLine($"[GitService] Merging {branchName} in {repoPath} (allowUnrelatedHistories={allowUnrelatedHistories})");
            var result = GitCliHelpers.RunGit(repoPath, args);
            Debug.WriteLine($"[GitService] Merge output: {result.Output}");
            Debug.WriteLine($"[GitService] Merge error: {result.Error}");
            Debug.WriteLine($"[GitService] Merge exit code: {result.ExitCode}");

            if (result.ExitCode == 0)
            {
                return new Models.MergeResult { Success = true };
            }

            // Check for unrelated histories error
            if (_context.ErrorMapper.IsUnrelatedHistoriesError(result.Error))
            {
                return new Models.MergeResult
                {
                    Success = false,
                    HasUnrelatedHistories = true,
                    ErrorMessage = "Unrelated histories detected."
                };
            }

            // Check if there are conflicts
            if (_context.ErrorMapper.IsConflictError(result.Output, result.Error))
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

    /// <summary>
    /// Fast-forward the current branch to match a target branch.
    /// </summary>
    public Task<Models.MergeResult> FastForwardAsync(string repoPath, string targetBranchName)
    {
        return Task.Run(() =>
        {
            // Use --ff-only to ensure we only fast-forward (no merge commit)
            var args = $"merge --ff-only \"{targetBranchName}\"";

            Debug.WriteLine($"[GitService] Fast-forwarding to {targetBranchName} in {repoPath}");
            var result = GitCliHelpers.RunGit(repoPath, args);
            Debug.WriteLine($"[GitService] Fast-forward output: {result.Output}");
            Debug.WriteLine($"[GitService] Fast-forward error: {result.Error}");
            Debug.WriteLine($"[GitService] Fast-forward exit code: {result.ExitCode}");

            if (result.ExitCode == 0)
            {
                return new Models.MergeResult { Success = true };
            }

            // Check if fast-forward is not possible (branches have diverged)
            if (_context.ErrorMapper.IsFastForwardNotPossible(result.Output, result.Error))
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

    /// <summary>
    /// Perform a squash merge of a branch into the current branch.
    /// </summary>
    public Task<Models.MergeResult> SquashMergeAsync(string repoPath, string branchName)
    {
        return Task.Run(() =>
        {
            var result = GitCliHelpers.RunGit(repoPath, $"merge --squash \"{branchName}\"");

            if (result.ExitCode != 0)
            {
                if (_context.ErrorMapper.IsConflictError(result.Output, result.Error))
                {
                    return new Models.MergeResult { Success = false, HasConflicts = true };
                }

                return new Models.MergeResult
                {
                    Success = false,
                    ErrorMessage = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error
                };
            }

            return new Models.MergeResult { Success = true };
        });
    }

    /// <summary>
    /// Complete a merge by creating the merge commit.
    /// </summary>
    public Task CompleteMergeAsync(string repoPath, string commitMessage)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
            repo.Commit(commitMessage, signature, signature);
        });
    }

    /// <summary>
    /// Abort an in-progress merge and return to pre-merge state.
    /// </summary>
    public Task AbortMergeAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            GitCliHelpers.RunGit(repoPath, "merge --abort");
        });
    }

    /// <summary>
    /// Check if the repository is in an "orphaned conflict" state.
    /// This occurs when the index has unmerged entries (conflicts) but MERGE_HEAD doesn't exist.
    /// This can happen after a failed checkout operation.
    /// </summary>
    public Task<bool> IsOrphanedConflictStateAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            var mergeHeadPath = Path.Combine(repoPath, ".git", "MERGE_HEAD");
            var hasMergeHead = File.Exists(mergeHeadPath);

            if (hasMergeHead)
            {
                // Normal merge in progress, not orphaned
                return false;
            }

            // Check if there are unmerged entries in the index
            var conflictCount = GitCliHelpers.GetConflictCount(repoPath);
            return conflictCount > 0;
        });
    }

    /// <summary>
    /// Reset the index to clear orphaned conflict state.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="discardWorkingChanges">If true, also discards all working directory changes</param>
    public async Task ResetOrphanedConflictsAsync(string repoPath, bool discardWorkingChanges)
    {
        // Reset the index to HEAD to clear unmerged entries
        var resetResult = await _context.CommandRunner.RunAsync(repoPath, ["reset", "HEAD"]);
        if (!resetResult.Success && !string.IsNullOrEmpty(resetResult.StandardError))
        {
            // Ignore "Unstaged changes after reset" which is expected
            if (!resetResult.StandardError.Contains("Unstaged changes"))
            {
                throw new InvalidOperationException(resetResult.StandardError);
            }
        }

        if (discardWorkingChanges)
        {
            // Discard all working directory changes
            var checkoutResult = await _context.CommandRunner.RunAsync(repoPath, ["checkout", "--", "."]);
            if (!checkoutResult.Success && !string.IsNullOrEmpty(checkoutResult.StandardError))
            {
                throw new InvalidOperationException(checkoutResult.StandardError);
            }
        }
    }

    /// <summary>
    /// Cherry-pick a commit onto the current branch.
    /// </summary>
    public async Task<Models.MergeResult> CherryPickAsync(string repoPath, string commitSha)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["cherry-pick", commitSha]);

        if (result.Success)
        {
            return new Models.MergeResult { Success = true };
        }

        var conflicts = GitCliHelpers.GetConflictFiles(repoPath);
        return new Models.MergeResult
        {
            Success = false,
            HasConflicts = conflicts.Count > 0,
            ErrorMessage = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError
        };
    }
}
