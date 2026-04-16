using Leaf.Services;
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
        Log.Info("StashMerge", "Starting commit-based merge approach");

        // Step 1: Stash local changes temporarily
        var tempStashResult = GitCliHelpers.RunGitArgs(repoPath, "stash", "push", "-m", GitCliHelpers.TempStashMessage);
        if (tempStashResult.ExitCode != 0)
        {
            Log.Error("StashMerge", $"Failed to create temp stash: {tempStashResult.Error}");
            return null;
        }
        Log.Info("StashMerge", "Created temp stash for local changes");

        // Target stash index shifted by +1 since we added TEMP at index 0
        int adjustedIndex = stashIndex + 1;

        // Step 2: Apply target stash (working dir is now clean)
        var applyTargetResult = GitCliHelpers.RunGitArgs(repoPath, "stash", "apply", adjustedIndex.ToString());
        if (applyTargetResult.ExitCode != 0)
        {
            Log.Error("StashMerge", $"Failed to apply target stash: {applyTargetResult.Error}");
            // Restore local changes
            GitCliHelpers.RunGitArgs(repoPath, "stash", "pop", "0");
            return null;
        }
        Log.Info("StashMerge", "Applied target stash");

        // Step 3: Stage all changes from target stash
        GitCliHelpers.RunGitArgs(repoPath, "add", "-A");
        Log.Info("StashMerge", "Staged target stash changes");

        // Step 4: Apply temp stash (local changes) - this should attempt merge
        var applyTempResult = GitCliHelpers.RunGitArgs(repoPath, "stash", "apply", "0");
        Log.Info("StashMerge", $"Apply temp result: exit={applyTempResult.ExitCode}, error={applyTempResult.Error}");

        // Check for conflicts
        var conflicts = GitCliHelpers.GetConflictFiles(repoPath);
        Log.Info("StashMerge", $"Conflicts found: {conflicts.Count}");

        if (conflicts.Count > 0)
        {
            // Success! We have proper git conflicts that can be resolved
            // Drop the target stash since its changes are now in the working dir
            GitCliHelpers.RunGitArgs(repoPath, "stash", "drop", adjustedIndex.ToString());
            // Keep TEMP stash - will be cleaned up after conflict resolution
            Log.Info("StashMerge", "Conflicts created successfully");

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
            GitCliHelpers.RunGitArgs(repoPath, "stash", "drop", adjustedIndex.ToString()); // Drop target
            GitCliHelpers.RunGitArgs(repoPath, "stash", "drop", "0"); // Drop temp
            Log.Info("StashMerge", "Both stashes applied cleanly");

            return new Models.MergeResult { Success = true };
        }

        // Apply failed but no conflicts - something else went wrong
        // Try to restore original state
        Log.Warn("StashMerge", "Apply failed without conflicts - restoring state");
        GitCliHelpers.RunGitArgs(repoPath, "reset", "--hard", "HEAD");
        GitCliHelpers.RunGitArgs(repoPath, "stash", "pop", "0"); // Restore local changes
        return null;
    }

    /// <summary>
    /// Simple stash pop without smart merge logic.
    /// </summary>
    public static Models.MergeResult SimplePopStash(string repoPath, int stashIndex)
    {
        var result = new Models.MergeResult();

        var popResult = GitCliHelpers.RunGitArgs(repoPath, "stash", "pop", stashIndex.ToString());
        Log.Info("StashMerge", $"SimplePopStash: exit={popResult.ExitCode}, output={popResult.Output}, error={popResult.Error}");

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
