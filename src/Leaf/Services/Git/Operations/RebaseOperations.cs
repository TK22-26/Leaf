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
    private readonly IGitOperationContext _context;

    public RebaseOperations(IGitOperationContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
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
    /// Continue a rebase after the user has resolved conflicts.
    /// </summary>
    /// <remarks>
    /// Routes through <c>git rebase --continue</c> (CLI) rather than
    /// LibGit2Sharp's <c>repo.Rebase.Continue</c>. Two reasons:
    /// <list type="bullet">
    ///   <item>LibGit2Sharp does not invoke <c>GIT_EDITOR</c> for
    ///         <c>reword</c> / <c>squash</c> entries, so any custom
    ///         message Leaf had queued for a row after the conflict
    ///         point would silently be replaced with the original
    ///         commit message.</item>
    ///   <item>The CLI matches what every other Leaf rebase verb
    ///         (skip, abort) already uses — a single execution model
    ///         is easier to reason about than a mixed LibGit2Sharp /
    ///         CLI split.</item>
    /// </list>
    /// When a Leaf-driven interactive rebase is in progress (marker file
    /// present in <c>.git/rebase-merge/</c>), <see cref="RebaseHelperResolver.BuildContinuationEnvironment"/>
    /// re-establishes the helper env so the editor invocations on the
    /// CLI side reach our <c>Leaf.SequenceEditor.exe</c>.
    /// </remarks>
    public async Task<Models.MergeResult> ContinueRebaseAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        Log.Info("Rebase", "ContinueRebase: running git rebase --continue");
        MergeDebugHelper.LogMergeState("BeforeContinueRebase", repoPath);

        var gitDir = Path.Combine(repoPath, ".git");
        var env = RebaseHelperResolver.BuildContinuationEnvironment(gitDir);
        if (env != null)
        {
            Log.Info("Rebase", "ContinueRebase: leaf marker present, re-establishing helper env");
        }

        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["rebase", "--continue"],
            input: null,
            credentialKey: null,
            extraEnvironment: env,
            cancellationToken: cancellationToken);

        Log.Info("Rebase", $"ContinueRebase: exit={result.ExitCode}");
        MergeDebugHelper.LogMergeState("AfterContinueRebase", repoPath);

        if (result.Success)
        {
            return new Models.MergeResult { Success = true };
        }

        // Rebase paused again — another conflict or an `edit` stop. Same
        // probe that InteractiveRebaseService.StartAsync uses.
        var paused = Directory.Exists(Path.Combine(gitDir, "rebase-merge")) ||
                     Directory.Exists(Path.Combine(gitDir, "rebase-apply"));

        if (paused)
        {
            Log.Info("Rebase", $"ContinueRebase: paused again (exit {result.ExitCode}); user action required.");
            return new Models.MergeResult
            {
                Success = false,
                HasConflicts = true,
                ErrorMessage = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput.Trim()
                    : result.StandardError.Trim(),
            };
        }

        Log.Error("Rebase", $"ContinueRebase: failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
        return new Models.MergeResult
        {
            Success = false,
            ErrorMessage = string.IsNullOrWhiteSpace(result.StandardError)
                ? $"git rebase --continue exited with code {result.ExitCode}."
                : result.StandardError.Trim(),
        };
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
