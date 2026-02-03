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
    public Task CommitAsync(string repoPath, string message, string? description = null)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            var fullMessage = string.IsNullOrEmpty(description)
                ? message
                : $"{message}\n\n{description}";

            var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
            repo.Commit(fullMessage, signature, signature);
        });
    }

    /// <summary>
    /// Revert a commit (creates a new commit).
    /// </summary>
    public async Task RevertCommitAsync(string repoPath, string commitSha)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["revert", commitSha]);

        if (!result.Success)
        {
            throw new InvalidOperationException(result.StandardError);
        }
    }

    /// <summary>
    /// Revert a merge commit using the specified parent index.
    /// </summary>
    public async Task RevertMergeCommitAsync(string repoPath, string commitSha, int parentIndex)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["revert", "-m", parentIndex.ToString(), commitSha]);

        if (!result.Success)
        {
            throw new InvalidOperationException(result.StandardError);
        }
    }

    /// <summary>
    /// Undo last commit (soft reset HEAD~1). Only works if not pushed.
    /// </summary>
    public Task<bool> UndoCommitAsync(string repoPath)
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
        });
    }

    /// <summary>
    /// Redo the last undone commit (if available).
    /// </summary>
    public async Task<bool> RedoCommitAsync(string repoPath)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["reset", "--soft", "ORIG_HEAD"]);

        return result.Success;
    }

    /// <summary>
    /// Check if the current HEAD has been pushed to remote.
    /// </summary>
    public Task<bool> IsHeadPushedAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            if (repo.Head.TrackedBranch == null)
                return false;

            var localTip = repo.Head.Tip;
            var remoteTip = repo.Head.TrackedBranch.Tip;

            return localTip.Sha == remoteTip?.Sha;
        });
    }
}
