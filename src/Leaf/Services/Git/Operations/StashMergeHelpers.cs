using System.Diagnostics;
using Leaf.Services.Git.Core;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Helper methods for 3-way merge logic during stash operations.
/// Extracted to keep StashOperations under 500 lines.
/// </summary>
internal static class StashMergeHelpers
{
    /// <summary>
    /// Try commit-based merge approach for stash pop with conflicts.
    /// </summary>
    public static Models.MergeResult? TryCommitBasedMerge(string repoPath, int stashIndex)
    {
        // Approach: stash local -> apply target -> stage -> apply local stash -> get conflicts
        Debug.WriteLine("[TryCommitBasedMerge] Starting commit-based merge approach");

        // Step 1: Stash local changes temporarily
        var tempStashResult = GitCliHelpers.RunGit(repoPath, $"stash push -m \"{GitCliHelpers.TempStashMessage}\"");
        if (tempStashResult.ExitCode != 0)
        {
            Debug.WriteLine($"[TryCommitBasedMerge] Failed to create temp stash: {tempStashResult.Error}");
            return null;
        }
        Debug.WriteLine("[TryCommitBasedMerge] Created temp stash for local changes");

        // Target stash index shifted by +1 since we added TEMP at index 0
        int adjustedIndex = stashIndex + 1;

        // Step 2: Apply target stash (working dir is now clean)
        var applyTargetResult = GitCliHelpers.RunGit(repoPath, $"stash apply {adjustedIndex}");
        if (applyTargetResult.ExitCode != 0)
        {
            Debug.WriteLine($"[TryCommitBasedMerge] Failed to apply target stash: {applyTargetResult.Error}");
            // Restore local changes
            GitCliHelpers.RunGit(repoPath, "stash pop 0");
            return null;
        }
        Debug.WriteLine("[TryCommitBasedMerge] Applied target stash");

        // Step 3: Stage all changes from target stash
        GitCliHelpers.RunGit(repoPath, "add -A");
        Debug.WriteLine("[TryCommitBasedMerge] Staged target stash changes");

        // Step 4: Apply temp stash (local changes) - this should attempt merge
        var applyTempResult = GitCliHelpers.RunGit(repoPath, "stash apply 0");
        Debug.WriteLine($"[TryCommitBasedMerge] Apply temp result: exit={applyTempResult.ExitCode}, error={applyTempResult.Error}");

        // Check for conflicts
        var conflicts = GitCliHelpers.GetConflictFiles(repoPath);
        Debug.WriteLine($"[TryCommitBasedMerge] Conflicts found: {conflicts.Count}");

        if (conflicts.Count > 0)
        {
            // Success! We have proper git conflicts that can be resolved
            // Drop the target stash since its changes are now in the working dir
            GitCliHelpers.RunGit(repoPath, $"stash drop {adjustedIndex}");
            // Keep TEMP stash - will be cleaned up after conflict resolution
            Debug.WriteLine("[TryCommitBasedMerge] Conflicts created successfully");

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
            GitCliHelpers.RunGit(repoPath, $"stash drop {adjustedIndex}"); // Drop target
            GitCliHelpers.RunGit(repoPath, "stash drop 0"); // Drop temp
            Debug.WriteLine("[TryCommitBasedMerge] Both stashes applied cleanly");

            return new Models.MergeResult { Success = true };
        }

        // Apply failed but no conflicts - something else went wrong
        // Try to restore original state
        Debug.WriteLine("[TryCommitBasedMerge] Apply failed without conflicts - restoring state");
        GitCliHelpers.RunGit(repoPath, "reset --hard HEAD");
        GitCliHelpers.RunGit(repoPath, "stash pop 0"); // Restore local changes
        return null;
    }

    /// <summary>
    /// Simple stash pop without smart merge logic.
    /// </summary>
    public static Models.MergeResult SimplePopStash(string repoPath, int stashIndex)
    {
        var result = new Models.MergeResult();

        var popResult = GitCliHelpers.RunGit(repoPath, $"stash pop {stashIndex}");
        Debug.WriteLine($"[SimplePopStash] Result: exit={popResult.ExitCode}, output={popResult.Output}, error={popResult.Error}");

        var conflicts = GitCliHelpers.GetConflictFiles(repoPath);

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
            result.ErrorMessage = !string.IsNullOrEmpty(popResult.Error)
                ? popResult.Error.Trim()
                : popResult.Output.Trim();

            if (string.IsNullOrEmpty(result.ErrorMessage))
            {
                result.ErrorMessage = $"git stash pop failed with exit code {popResult.ExitCode}";
            }
        }

        return result;
    }
}
