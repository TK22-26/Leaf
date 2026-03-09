using System.Diagnostics;
using System.IO;
using Leaf.Services.Git.Core;
using LibGit2Sharp;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Operations for rebasing.
/// </summary>
internal class RebaseOperations
{
    private readonly IGitOperationContext _context;

    public RebaseOperations(IGitOperationContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Rebase the current branch onto another branch.
    /// </summary>
    public Task<Models.MergeResult> RebaseAsync(string repoPath, string ontoBranch, IProgress<string>? progress = null)
    {
        return Task.Run(() =>
        {
            Debug.WriteLine($"[MERGE][OPS] Rebase: onto={ontoBranch}");
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

            Debug.WriteLine($"[MERGE][OPS] Rebase: status={rebaseResult.Status}");
            MergeDebugHelper.LogMergeState("AfterRebase", repoPath);

            return rebaseResult.Status switch
            {
                RebaseStatus.Complete => new Models.MergeResult { Success = true },
                RebaseStatus.Conflicts => new Models.MergeResult { Success = false, HasConflicts = true },
                _ => new Models.MergeResult { Success = false, ErrorMessage = $"Rebase status: {rebaseResult.Status}" }
            };
        });
    }

    /// <summary>
    /// Abort an in-progress rebase operation.
    /// </summary>
    public Task AbortRebaseAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            Debug.WriteLine("[MERGE][OPS] AbortRebase: running git rebase --abort");
            MergeDebugHelper.LogMergeState("BeforeAbortRebase", repoPath);
            GitCliHelpers.RunGit(repoPath, "rebase --abort");
            MergeDebugHelper.LogMergeState("AfterAbortRebase", repoPath);
        });
    }

    /// <summary>
    /// Continue a rebase after resolving conflicts.
    /// </summary>
    public Task<Models.MergeResult> ContinueRebaseAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            Debug.WriteLine("[MERGE][OPS] ContinueRebase");
            MergeDebugHelper.LogMergeState("BeforeContinueRebase", repoPath);
            using var repo = new Repository(repoPath);
            var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
            var options = new RebaseOptions();

            var result = repo.Rebase.Continue(new Identity(signature.Name, signature.Email), options);

            Debug.WriteLine($"[MERGE][OPS] ContinueRebase: status={result.Status}");
            MergeDebugHelper.LogMergeState("AfterContinueRebase", repoPath);

            return result.Status switch
            {
                RebaseStatus.Complete => new Models.MergeResult { Success = true },
                RebaseStatus.Conflicts => new Models.MergeResult { Success = false, HasConflicts = true },
                _ => new Models.MergeResult { Success = false, ErrorMessage = $"Rebase status: {result.Status}" }
            };
        });
    }

    /// <summary>
    /// Skip the current commit during a rebase.
    /// </summary>
    public Task<Models.MergeResult> SkipRebaseCommitAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            Debug.WriteLine("[MERGE][OPS] SkipRebaseCommit");
            var result = GitCliHelpers.RunGit(repoPath, "rebase --skip");
            return new Models.MergeResult
            {
                Success = result.ExitCode == 0,
                ErrorMessage = result.Error
            };
        });
    }

    /// <summary>
    /// Check if a rebase is in progress.
    /// </summary>
    public Task<bool> IsRebaseInProgressAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            var rebaseApplyPath = Path.Combine(repoPath, ".git", "rebase-apply");
            var rebaseMergePath = Path.Combine(repoPath, ".git", "rebase-merge");
            var inProgress = Directory.Exists(rebaseApplyPath) || Directory.Exists(rebaseMergePath);
            Debug.WriteLine($"[MERGE][STATE] IsRebaseInProgress: {inProgress} (apply={Directory.Exists(rebaseApplyPath)}, merge={Directory.Exists(rebaseMergePath)})");
            return inProgress;
        });
    }
}
