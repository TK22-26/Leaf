using System.Diagnostics;
using Leaf.Models;
using Leaf.Services.Git.Core;
using Leaf.Services.Git.Interfaces;
using LibGit2Sharp;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Operations for managing stashes.
/// </summary>
internal class StashOperations
{
    private readonly IGitOperationContext _context;
    private readonly IConflictOperations _conflictOps;

    public StashOperations(IGitOperationContext context, IConflictOperations conflictOps)
    {
        _context = context;
        _conflictOps = conflictOps;
    }

    /// <summary>
    /// Stash changes.
    /// </summary>
    public Task StashAsync(string repoPath, string? message = null)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
            repo.Stashes.Add(signature, message ?? "Stash from Leaf");
        });
    }

    /// <summary>
    /// Stash only staged changes (requires Git 2.35+).
    /// </summary>
    public async Task StashStagedAsync(string repoPath, string? message = null)
    {
        var args = new List<string> { "stash", "push", "--staged" };
        if (!string.IsNullOrEmpty(message))
        {
            args.Add("-m");
            args.Add(message);
        }

        await _context.CommandRunner.RunAsync(repoPath, args);
    }

    /// <summary>
    /// Pop stashed changes (index 0).
    /// </summary>
    public Task<Models.MergeResult> PopStashAsync(string repoPath)
    {
        return PopStashAsync(repoPath, 0);
    }

    /// <summary>
    /// Pop a specific stash by index with smart merge logic.
    /// </summary>
    public Task<Models.MergeResult> PopStashAsync(string repoPath, int stashIndex)
    {
        return Task.Run(() =>
        {
            var result = new Models.MergeResult();

            Debug.WriteLine($"[PopStash] Starting smart pop for stash index {stashIndex} in {repoPath}");

            // Step 1: Check if there are uncommitted changes
            bool hasChanges = GitCliHelpers.HasUncommittedChanges(repoPath);
            Debug.WriteLine($"[PopStash] Has uncommitted changes: {hasChanges}");

            if (!hasChanges)
            {
                // Simple case - no local changes, pop directly
                Debug.WriteLine("[PopStash] No local changes - using simple pop");
                return StashMergeHelpers.SimplePopStash(repoPath, stashIndex);
            }

            // Smart pop: Patch-based approach
            Debug.WriteLine("[PopStash] Local changes detected - using patch-based approach");

            // Step 2: Get the stash diff as a patch
            var stashRef = $"stash@{{{stashIndex}}}";
            var patchResult = GitCliHelpers.RunGit(repoPath, $"stash show -p {stashRef}");
            Debug.WriteLine($"[PopStash] Patch result: exit={patchResult.ExitCode}, length={patchResult.Output.Length}");

            if (patchResult.ExitCode != 0 || string.IsNullOrWhiteSpace(patchResult.Output))
            {
                result.ErrorMessage = $"Failed to get stash patch: {patchResult.Error}";
                Debug.WriteLine($"[PopStash] ERROR: {result.ErrorMessage}");
                return result;
            }

            // Step 3: Apply the patch using 'patch' with fuzz for fuzzy matching
            var applyResult = GitCliHelpers.RunPatchWithInput(repoPath, patchResult.Output);
            Debug.WriteLine($"[PopStash] Patch apply result: exit={applyResult.ExitCode}, output={applyResult.Output}, error={applyResult.Error}");

            // Check if patch.exe wasn't found
            if (applyResult.ExitCode == -1 && applyResult.Error.Contains("patch.exe"))
            {
                Debug.WriteLine("[PopStash] patch.exe not found - Git for Windows required");
                result.ErrorMessage = applyResult.Error;
                return result;
            }

            // Check if patch created .rej files (rejected hunks = conflicts)
            bool hasRejections = applyResult.Output.Contains("FAILED") || applyResult.Output.Contains("saving rejects");

            if (applyResult.ExitCode == 0 && !hasRejections)
            {
                // Success! Patch applied cleanly - now drop the stash
                Debug.WriteLine("[PopStash] Patch applied cleanly - dropping stash");
                GitCliHelpers.RunGit(repoPath, $"stash drop {stashIndex}");

                result.Success = true;
                return result;
            }

            // Patch failed with rejections - try commit-based merge to get proper conflict markers
            if (hasRejections)
            {
                Debug.WriteLine("[PopStash] Patch has rejections - attempting commit-based merge for conflict resolution");

                // Clean up any .rej files created by patch
                GitCliHelpers.CleanupRejectFiles(repoPath);

                // Try commit-based approach to get proper git conflicts
                var mergeResult = StashMergeHelpers.TryCommitBasedMerge(repoPath, stashIndex);
                if (mergeResult != null)
                {
                    return mergeResult;
                }

                // Fallback if commit-based merge also fails
                result.ErrorMessage = "Stash conflicts with your local changes. Commit or stash your changes first, then try again.";
                return result;
            }

            // Patch failed - check for actual git conflicts
            var conflicts = GitCliHelpers.GetConflictFiles(repoPath);
            if (conflicts.Count > 0)
            {
                Debug.WriteLine("[PopStash] CONFLICTS: Merge conflicts detected - dropping stash");
                GitCliHelpers.RunGit(repoPath, $"stash drop {stashIndex}");

                result.HasConflicts = true;
                result.ConflictingFiles = conflicts;
                result.ErrorMessage = "Merge conflicts detected - resolve to complete";
                return result;
            }

            // Patch failed for unknown reason - fall back to simple pop for error message
            Debug.WriteLine("[PopStash] Patch apply failed - falling back to simple pop for error message");
            return StashMergeHelpers.SimplePopStash(repoPath, stashIndex);
        });
    }

    /// <summary>
    /// Get all stashes in the repository.
    /// </summary>
    public Task<List<StashInfo>> GetStashesAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var stashes = new List<StashInfo>();

            int index = 0;
            foreach (var stash in repo.Stashes)
            {
                var workTreeCommit = stash.WorkTree;
                stashes.Add(new StashInfo
                {
                    Sha = workTreeCommit.Sha,
                    Index = index,
                    Message = stash.Message,
                    Author = workTreeCommit.Author.Name,
                    Date = workTreeCommit.Author.When,
                    BranchName = _context.OutputParser.ExtractBranchFromStashMessage(stash.Message)
                });
                index++;
            }

            return stashes;
        });
    }

    /// <summary>
    /// Delete a specific stash by index.
    /// </summary>
    public async Task DeleteStashAsync(string repoPath, int stashIndex)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["stash", "drop", stashIndex.ToString()]);

        if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to delete stash: {result.StandardError.Trim()}");
        }
    }

    /// <summary>
    /// Clean up any temporary stash created during smart pop operation.
    /// </summary>
    public Task CleanupTempStashAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            var listResult = GitCliHelpers.RunGit(repoPath, "stash list");
            var lines = listResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(GitCliHelpers.TempStashMessage))
                {
                    GitCliHelpers.RunGit(repoPath, $"stash drop {i}");
                    break;
                }
            }
        });
    }
}
