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

            Debug.WriteLine($"[MERGE][OPS] MergeBranch: branch={branchName} allowUnrelatedHistories={allowUnrelatedHistories}");
            MergeDebugHelper.LogMergeState("BeforeMerge", repoPath);
            var result = GitCliHelpers.RunGit(repoPath, args);
            Debug.WriteLine($"[MERGE][OPS] MergeBranch: exitCode={result.ExitCode} output={result.Output}");
            if (!string.IsNullOrEmpty(result.Error))
                Debug.WriteLine($"[MERGE][ERROR] MergeBranch: {result.Error}");

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

            Debug.WriteLine($"[MERGE][OPS] FastForward: target={targetBranchName}");
            var result = GitCliHelpers.RunGit(repoPath, args);
            Debug.WriteLine($"[MERGE][OPS] FastForward: exitCode={result.ExitCode} output={result.Output}");
            if (!string.IsNullOrEmpty(result.Error))
                Debug.WriteLine($"[MERGE][ERROR] FastForward: {result.Error}");

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
            Debug.WriteLine($"[MERGE][OPS] SquashMerge: branch={branchName}");
            var result = GitCliHelpers.RunGitArgs(repoPath, "merge", "--squash", branchName);

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
            Debug.WriteLine($"[MERGE][OPS] CompleteMerge: message={commitMessage}");
            MergeDebugHelper.LogMergeState("BeforeCompleteMerge", repoPath);
            using var repo = new Repository(repoPath);

            // B8 fix: validate no unmerged entries remain in the index
            if (repo.Index.Conflicts.Any())
            {
                var unmergedFiles = repo.Index.Conflicts
                    .Select(c => c.Ancestor?.Path ?? c.Ours?.Path ?? c.Theirs?.Path)
                    .Distinct()
                    .ToList();
                Debug.WriteLine($"[MERGE][ERROR] CompleteMerge: {unmergedFiles.Count} unmerged files remain");
                throw new InvalidOperationException(
                    $"Cannot complete merge: {unmergedFiles.Count} file(s) still have unresolved conflicts: {string.Join(", ", unmergedFiles)}");
            }

            // B9 fix: use .git/MERGE_MSG if available (preserves git-generated message)
            var mergeMsgPath = Path.Combine(repo.Info.Path, "MERGE_MSG");
            if (File.Exists(mergeMsgPath))
            {
                try
                {
                    var rawMessage = File.ReadAllText(mergeMsgPath);
                    // Strip comment lines (starting with #) like git does
                    var cleanedLines = rawMessage.Split('\n')
                        .Where(line => !line.TrimStart().StartsWith('#'))
                        .ToArray();
                    var gitMessage = string.Join("\n", cleanedLines).TrimEnd();
                    if (!string.IsNullOrWhiteSpace(gitMessage))
                    {
                        Debug.WriteLine($"[MERGE][OPS] CompleteMerge: using MERGE_MSG instead of caller message");
                        commitMessage = gitMessage;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MERGE][OPS] CompleteMerge: failed to read MERGE_MSG, using caller message: {ex.Message}");
                }
            }

            var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
            repo.Commit(commitMessage, signature, signature);
            MergeDebugHelper.LogMergeState("AfterCompleteMerge", repoPath);
        });
    }

    /// <summary>
    /// Abort an in-progress merge and return to pre-merge state.
    /// </summary>
    public Task AbortMergeAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            Debug.WriteLine("[MERGE][OPS] AbortMerge: running git merge --abort");
            MergeDebugHelper.LogMergeState("BeforeAbortMerge", repoPath);
            GitCliHelpers.RunGit(repoPath, "merge --abort");
            MergeDebugHelper.LogMergeState("AfterAbortMerge", repoPath);
        });
    }

    /// <summary>
    /// Abort an in-progress cherry-pick.
    /// </summary>
    public Task AbortCherryPickAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            Debug.WriteLine("[MERGE][OPS] AbortCherryPick: running git cherry-pick --abort");
            MergeDebugHelper.LogMergeState("BeforeAbortCherryPick", repoPath);
            GitCliHelpers.RunGit(repoPath, "cherry-pick --abort");
            MergeDebugHelper.LogMergeState("AfterAbortCherryPick", repoPath);
        });
    }

    /// <summary>
    /// Abort an in-progress revert.
    /// </summary>
    public Task AbortRevertAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            Debug.WriteLine("[MERGE][OPS] AbortRevert: running git revert --abort");
            MergeDebugHelper.LogMergeState("BeforeAbortRevert", repoPath);
            GitCliHelpers.RunGit(repoPath, "revert --abort");
            MergeDebugHelper.LogMergeState("AfterAbortRevert", repoPath);
        });
    }

    /// <summary>
    /// Check if the repository is in an "orphaned conflict" state.
    /// This occurs when the index has unmerged entries (conflicts) but no operation sentinel exists.
    /// This can happen after a failed checkout operation.
    /// </summary>
    public Task<bool> IsOrphanedConflictStateAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            var gitDir = Path.Combine(repoPath, ".git");

            // If any operation sentinel exists, it's not orphaned
            if (File.Exists(Path.Combine(gitDir, "MERGE_HEAD"))
                || File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD"))
                || File.Exists(Path.Combine(gitDir, "REVERT_HEAD"))
                || Directory.Exists(Path.Combine(gitDir, "rebase-merge"))
                || Directory.Exists(Path.Combine(gitDir, "rebase-apply")))
            {
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
        Debug.WriteLine($"[MERGE][OPS] CherryPick: commit={commitSha}");
        MergeDebugHelper.LogMergeState("BeforeCherryPick", repoPath);

        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["cherry-pick", commitSha]);

        if (result.Success)
        {
            Debug.WriteLine("[MERGE][OPS] CherryPick: success");
            return new Models.MergeResult { Success = true };
        }

        var conflicts = GitCliHelpers.GetConflictFiles(repoPath);
        Debug.WriteLine($"[MERGE][OPS] CherryPick: failed, conflicts={conflicts.Count}");
        if (!string.IsNullOrWhiteSpace(result.StandardError))
            Debug.WriteLine($"[MERGE][ERROR] CherryPick: {result.StandardError}");
        MergeDebugHelper.LogMergeState("AfterCherryPick", repoPath);

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
