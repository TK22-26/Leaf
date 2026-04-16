using Leaf.Services;
using Leaf.Services.Git.Core;
using LibGit2Sharp;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Operations for creating and manipulating commits.
/// </summary>
internal class CommitOperations
{
    private readonly IGitOperationContext _context;

    public CommitOperations(IGitOperationContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Create a commit with staged files.
    /// </summary>
    public Task CommitAsync(string repoPath, string message, string? description = null, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            var fullMessage = string.IsNullOrEmpty(description)
                ? message
                : $"{message}\n\n{description}";

            var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
            repo.Commit(fullMessage, signature, signature);
        }, cancellationToken);
    }

    /// <summary>
    /// Revert a commit (creates a new commit).
    /// </summary>
    public async Task RevertCommitAsync(string repoPath, string commitSha, CancellationToken cancellationToken = default)
    {
        Log.Info("Merge", $"RevertCommit: commit={commitSha}");
        MergeDebugHelper.LogMergeState("BeforeRevert", repoPath);

        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["revert", commitSha], cancellationToken: cancellationToken);

        MergeDebugHelper.LogMergeState("AfterRevert", repoPath);

        if (!result.Success)
        {
            Log.Error("Merge", $"RevertCommit: {result.StandardError}");
            throw new InvalidOperationException(result.StandardError);
        }

        Log.Info("Merge", "RevertCommit: success");
    }

    /// <summary>
    /// Revert a merge commit using the specified parent index.
    /// </summary>
    public async Task RevertMergeCommitAsync(string repoPath, string commitSha, int parentIndex, CancellationToken cancellationToken = default)
    {
        Log.Info("Merge", $"RevertMergeCommit: commit={commitSha} parent={parentIndex}");
        MergeDebugHelper.LogMergeState("BeforeRevertMerge", repoPath);

        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["revert", "-m", parentIndex.ToString(), commitSha], cancellationToken: cancellationToken);

        MergeDebugHelper.LogMergeState("AfterRevertMerge", repoPath);

        if (!result.Success)
        {
            Log.Error("Merge", $"RevertMergeCommit: {result.StandardError}");
            throw new InvalidOperationException(result.StandardError);
        }

        Log.Info("Merge", "RevertMergeCommit: success");
    }

    /// <summary>
    /// Undo last commit (soft reset HEAD~1). Only works if not pushed.
    /// </summary>
    public Task<bool> UndoCommitAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            // Check if HEAD has been pushed
            if (repo.Head.TrackedBranch != null)
            {
                var localTip = repo.Head.Tip;
                var remoteTip = repo.Head.TrackedBranch.Tip;

                if (localTip.Sha == remoteTip?.Sha)
                {
                    return false; // Cannot undo - already pushed
                }
            }

            // Soft reset to HEAD~1
            if (repo.Head.Tip.Parents.Any())
            {
                var parentCommit = repo.Head.Tip.Parents.First();
                repo.Reset(ResetMode.Soft, parentCommit);
                return true;
            }

            return false; // No parent commit to reset to
        }, cancellationToken);
    }

    /// <summary>
    /// Redo the last undone commit (if available).
    /// </summary>
    public async Task<bool> RedoCommitAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["reset", "--soft", "ORIG_HEAD"], cancellationToken: cancellationToken);

        return result.Success;
    }

    /// <summary>
    /// Check if the current HEAD has been pushed to remote.
    /// </summary>
    public Task<bool> IsHeadPushedAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            if (repo.Head.TrackedBranch == null)
                return false;

            var localTip = repo.Head.Tip;
            var remoteTip = repo.Head.TrackedBranch.Tip;

            return localTip.Sha == remoteTip?.Sha;
        }, cancellationToken);
    }
}
