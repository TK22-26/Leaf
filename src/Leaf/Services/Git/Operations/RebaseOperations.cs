using System.IO;
using Leaf.Services;
using Leaf.Services.Git.Core;
using LibGit2Sharp;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Operations for rebasing.
/// </summary>
internal class RebaseOperations
{
    public RebaseOperations(IGitOperationContext context)
    {
    }

    /// <summary>
    /// Rebase the current branch onto another branch.
    /// </summary>
    public Task<Models.MergeResult> RebaseAsync(string repoPath, string ontoBranch, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            Log.Info("Rebase", $"Rebase: onto={ontoBranch}");
            MergeDebugHelper.LogMergeState("BeforeRebase", repoPath);
            using var repo = new Repository(repoPath);

            var targetBranch = repo.Branches[ontoBranch];
            if (targetBranch == null)
            {
                throw new InvalidOperationException($"Branch '{ontoBranch}' not found.");
            }

            progress?.Report($"Rebasing onto {ontoBranch}...");

            var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
            var options = new RebaseOptions();

            var rebaseResult = repo.Rebase.Start(repo.Head, targetBranch, targetBranch, new Identity(signature.Name, signature.Email), options);

            Log.Info("Rebase", $"Rebase: status={rebaseResult.Status}");
            MergeDebugHelper.LogMergeState("AfterRebase", repoPath);

            return rebaseResult.Status switch
            {
                RebaseStatus.Complete => new Models.MergeResult { Success = true },
                RebaseStatus.Conflicts => new Models.MergeResult { Success = false, HasConflicts = true },
                _ => new Models.MergeResult { Success = false, ErrorMessage = $"Rebase status: {rebaseResult.Status}" }
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Abort an in-progress rebase operation.
    /// </summary>
    public Task AbortRebaseAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            Log.Info("Rebase", "AbortRebase: running git rebase --abort");
            MergeDebugHelper.LogMergeState("BeforeAbortRebase", repoPath);
            GitCliHelpers.RunGitArgs(repoPath, "rebase", "--abort");
            MergeDebugHelper.LogMergeState("AfterAbortRebase", repoPath);
        }, cancellationToken);
    }

    /// <summary>
    /// Continue a rebase after resolving conflicts.
    /// </summary>
    public Task<Models.MergeResult> ContinueRebaseAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            Log.Info("Rebase", "ContinueRebase");
            MergeDebugHelper.LogMergeState("BeforeContinueRebase", repoPath);
            using var repo = new Repository(repoPath);
            var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
            var options = new RebaseOptions();

            var result = repo.Rebase.Continue(new Identity(signature.Name, signature.Email), options);

            Log.Info("Rebase", $"ContinueRebase: status={result.Status}");
            MergeDebugHelper.LogMergeState("AfterContinueRebase", repoPath);

            return result.Status switch
            {
                RebaseStatus.Complete => new Models.MergeResult { Success = true },
                RebaseStatus.Conflicts => new Models.MergeResult { Success = false, HasConflicts = true },
                _ => new Models.MergeResult { Success = false, ErrorMessage = $"Rebase status: {result.Status}" }
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Skip the current commit during a rebase.
    /// </summary>
    public Task<Models.MergeResult> SkipRebaseCommitAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            Log.Info("Rebase", "SkipRebaseCommit");
            var result = GitCliHelpers.RunGitArgs(repoPath, "rebase", "--skip");
            return new Models.MergeResult
            {
                Success = result.ExitCode == 0,
                ErrorMessage = result.Error
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Check if a rebase is in progress.
    /// </summary>
    public Task<bool> IsRebaseInProgressAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var rebaseApplyPath = Path.Combine(repoPath, ".git", "rebase-apply");
            var rebaseMergePath = Path.Combine(repoPath, ".git", "rebase-merge");
            var inProgress = Directory.Exists(rebaseApplyPath) || Directory.Exists(rebaseMergePath);
            Log.Info("Rebase", $"IsRebaseInProgress: {inProgress} (apply={Directory.Exists(rebaseApplyPath)}, merge={Directory.Exists(rebaseMergePath)})");
            return inProgress;
        }, cancellationToken);
    }
}
