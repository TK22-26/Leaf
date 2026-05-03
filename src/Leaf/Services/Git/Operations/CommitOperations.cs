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
    /// Create a commit with staged files. When <paramref name="amend"/> is
    /// true, the new commit replaces HEAD — preserving the original author
    /// but updating the committer and the message/description.
    ///
    /// <para>§5.8: when <c>commit.gpgsign=true</c>, route through the git
    /// CLI rather than LibGit2Sharp. <c>libgit2</c> doesn't run gpg /
    /// ssh-keygen, so signing-enabled commits would silently produce
    /// unsigned commits if we kept the libgit2 path. The CLI handles
    /// every signing setting (key id, format, key file path) via git's
    /// own machinery, including credential helpers and pinentry.</para>
    /// </summary>
    public async Task CommitAsync(string repoPath, string message, string? description = null, bool amend = false, CancellationToken cancellationToken = default)
    {
        var fullMessage = string.IsNullOrEmpty(description)
            ? message
            : $"{message}\n\n{description}";

        var shouldSign = await IsCommitSigningEnabledAsync(repoPath, cancellationToken).ConfigureAwait(false);

        if (shouldSign)
        {
            await CommitViaCliAsync(repoPath, fullMessage, amend, cancellationToken).ConfigureAwait(false);
            return;
        }

        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var committer = repo.Config.BuildSignature(DateTimeOffset.Now);

            if (amend)
            {
                // Amend requires a HEAD commit to replace. An unborn branch
                // has nothing to amend — fail loudly rather than silently
                // falling back to a regular commit (which would silently
                // change the meaning of the caller's request).
                var tip = repo.Head.Tip
                    ?? throw new InvalidOperationException("Cannot amend: HEAD has no commit yet.");

                // Preserve original author on amend (matches `git commit --amend` default behaviour).
                repo.Commit(fullMessage, tip.Author, committer, new CommitOptions { AmendPreviousCommit = true });
            }
            else
            {
                repo.Commit(fullMessage, committer, committer);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Read <c>commit.gpgsign</c> from the repo's effective config (local
    /// then global). When the key isn't set, <c>git config --get</c> exits
    /// non-zero — we treat that as "signing off". Real failures (process
    /// spawn errors, cancellation) propagate so the caller and ultimately
    /// the user know signing routing is broken before the commit happens;
    /// silently catching here would let a transient git-launch failure
    /// downgrade a signed commit to unsigned, which is the worse failure
    /// mode for a security feature.
    /// </summary>
    private async Task<bool> IsCommitSigningEnabledAsync(string repoPath, CancellationToken cancellationToken)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["config", "--get", "--bool", "commit.gpgsign"],
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Success
            && string.Equals(result.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Commit through the git CLI. Message comes in via stdin (<c>-F -</c>)
    /// so newlines / blank lines / trailers round-trip exactly. The CLI
    /// honours every signing-related config key automatically, including
    /// agent / pinentry interactions.
    /// </summary>
    private async Task CommitViaCliAsync(string repoPath, string fullMessage, bool amend, CancellationToken cancellationToken)
    {
        var args = new List<string> { "commit", "-F", "-" };
        if (amend) args.Add("--amend");

        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            args.ToArray(),
            input: fullMessage,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            // git commit prints the actual error on stderr — pinentry
            // failures, missing key, etc. Surface it verbatim.
            var detail = string.IsNullOrEmpty(result.StandardError)
                ? "git commit failed (unknown error)"
                : result.StandardError.Trim();
            throw new InvalidOperationException(detail);
        }
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
