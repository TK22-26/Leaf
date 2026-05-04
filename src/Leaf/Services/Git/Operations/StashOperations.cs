using Leaf.Models;
using Leaf.Services;
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

    public StashOperations(IGitOperationContext context, IConflictOperations conflictOps)
    {
        _context = context;
    }

    /// <summary>
    /// Stash changes.
    /// </summary>
    public Task StashAsync(string repoPath, string? message = null, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
            repo.Stashes.Add(signature, message ?? "Stash from Leaf");
        }, cancellationToken);
    }

    /// <summary>
    /// Stash only staged changes (requires Git 2.35+).
    /// </summary>
    public async Task StashStagedAsync(string repoPath, string? message = null, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "stash", "push", "--staged" };
        if (!string.IsNullOrEmpty(message))
        {
            args.Add("-m");
            args.Add(message);
        }

        await _context.CommandRunner.RunAsync(repoPath, args, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Pop stashed changes (index 0).
    /// </summary>
    public Task<Models.MergeResult> PopStashAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return PopStashAsync(repoPath, 0);
    }

    /// <summary>
    /// Pop a specific stash by index with smart merge logic.
    /// </summary>
    public Task<Models.MergeResult> PopStashAsync(string repoPath, int stashIndex, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var result = new Models.MergeResult();

            Log.Info("Stash",$"[PopStash] Starting smart pop for stash index {stashIndex} in {repoPath}");

            // Step 1: Check if there are uncommitted changes
            bool hasChanges = GitCliHelpers.HasUncommittedChanges(repoPath);
            Log.Info("Stash",$"[PopStash] Has uncommitted changes: {hasChanges}");

            if (!hasChanges)
            {
                // Simple case - no local changes, pop directly
                Log.Info("Stash","[PopStash] No local changes - using simple pop");
                return StashMergeHelpers.SimplePopStash(repoPath, stashIndex);
            }

            // Smart pop: Patch-based approach
            Log.Info("Stash","[PopStash] Local changes detected - using patch-based approach");

            // Step 2: Get the stash diff as a patch
            var stashRef = $"stash@{{{stashIndex}}}";
            var patchResult = GitCliHelpers.RunGitArgs(repoPath, "stash", "show", "-p", stashRef);
            Log.Info("Stash",$"[PopStash] Patch result: exit={patchResult.ExitCode}, length={patchResult.Output.Length}");

            if (patchResult.ExitCode != 0 || string.IsNullOrWhiteSpace(patchResult.Output))
            {
                result.ErrorMessage = $"Failed to get stash patch: {patchResult.Error}";
                Log.Error("Stash", $"PopStash: {result.ErrorMessage}");
                return result;
            }

            // Step 3: Apply the patch using 'patch' with fuzz for fuzzy matching
            var applyResult = GitCliHelpers.RunPatchWithInput(repoPath, patchResult.Output);
            Log.Info("Stash",$"[PopStash] Patch apply result: exit={applyResult.ExitCode}, output={applyResult.Output}, error={applyResult.Error}");

            // Check if patch.exe wasn't found
            if (applyResult.ExitCode == -1 && applyResult.Error.Contains("patch.exe"))
            {
                Log.Error("Stash", "PopStash: patch.exe not found - Git for Windows required");
                result.ErrorMessage = applyResult.Error;
                return result;
            }

            // Check if patch created .rej files (rejected hunks = conflicts)
            bool hasRejections = applyResult.Output.Contains("FAILED") || applyResult.Output.Contains("saving rejects");

            if (applyResult.ExitCode == 0 && !hasRejections)
            {
                // Success! Patch applied cleanly - now drop the stash
                Log.Info("Stash","[PopStash] Patch applied cleanly - dropping stash");
                GitCliHelpers.RunGitArgs(repoPath, "stash", "drop", stashIndex.ToString());

                result.Success = true;
                return result;
            }

            // Patch failed with rejections - try commit-based merge to get proper conflict markers
            if (hasRejections)
            {
                Log.Info("Stash","[PopStash] Patch has rejections - attempting commit-based merge for conflict resolution");

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
                Log.Info("Stash","[PopStash] CONFLICTS: Merge conflicts detected - dropping stash");
                GitCliHelpers.RunGitArgs(repoPath, "stash", "drop", stashIndex.ToString());

                result.HasConflicts = true;
                result.ConflictingFiles = conflicts;
                result.ErrorMessage = "Merge conflicts detected - resolve to complete";
                return result;
            }

            // Patch failed for unknown reason - fall back to simple pop for error message
            Log.Info("Stash","[PopStash] Patch apply failed - falling back to simple pop for error message");
            return StashMergeHelpers.SimplePopStash(repoPath, stashIndex);
        }, cancellationToken);
    }

    /// <summary>
    /// Get all stashes in the repository.
    /// </summary>
    public Task<List<StashInfo>> GetStashesAsync(string repoPath, CancellationToken cancellationToken = default)
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
                    BranchName = _context.OutputParser.ExtractBranchFromStashMessage(stash.Message),
                    ParentSha = workTreeCommit.Parents.FirstOrDefault()?.Sha ?? string.Empty
                });
                index++;
            }

            return stashes;
        }, cancellationToken);
    }

    /// <summary>
    /// Delete a specific stash by index.
    /// </summary>
    public async Task DeleteStashAsync(string repoPath, int stashIndex, CancellationToken cancellationToken = default)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["stash", "drop", stashIndex.ToString()], cancellationToken: cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to delete stash: {result.StandardError.Trim()}");
        }
    }

    /// <summary>
    /// Clean up any temporary stash created during smart pop operation.
    /// </summary>
    public Task CleanupTempStashAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var listResult = GitCliHelpers.RunGitArgs(repoPath, "stash", "list");
            var lines = listResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(GitCliHelpers.TempStashMessage))
                {
                    // Re-query the stash list to guard against index shifts from concurrent
                    // stash operations between the find and the drop.
                    var verifyResult = GitCliHelpers.RunGitArgs(repoPath, "stash", "list");
                    var verifyLines = verifyResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    if (i < verifyLines.Length && verifyLines[i].Contains(GitCliHelpers.TempStashMessage))
                    {
                        GitCliHelpers.RunGitArgs(repoPath, "stash", "drop", i.ToString());
                    }
                    break;
                }
            }
        }, cancellationToken);
    }
}
