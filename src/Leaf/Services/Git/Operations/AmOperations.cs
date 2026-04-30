using System.IO;
using Leaf.Services.Git.Core;

namespace Leaf.Services.Git.Operations;


/// <summary>
/// <c>git am</c> control verbs (continue/skip/abort) plus the
/// am-vs-rebase disambiguation. Both commands share
/// <c>.git/rebase-apply/</c> as their state directory; what
/// distinguishes them is the <c>applying</c> sentinel that <c>git am</c>
/// writes there. <see cref="IsAmInProgressAsync"/> exists so callers
/// (the merge editor's continue path, the apply-patch dialog's pre-flight)
/// don't have to repeat that check inline.
/// </summary>
internal class AmOperations
{
    private readonly IGitOperationContext _context;

    public AmOperations(IGitOperationContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<bool> IsAmInProgressAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        // Resolve the actual git directory before probing for the marker.
        // For the main worktree this is `<repoPath>/.git`, but for linked
        // worktrees the on-disk `.git` is a pointer file, not a directory —
        // the real per-worktree gitdir lives elsewhere. Without this hop a
        // paused `git am` inside a linked worktree would be invisible to
        // detection and the apply-patch pre-flight would let a second am
        // start on top of the first.
        var gitDir = await ResolveGitDirAsync(repoPath, cancellationToken);
        return File.Exists(Path.Combine(gitDir, "rebase-apply", "applying"));
    }

    public async Task<Models.MergeResult> ContinueAmAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        Log.Info("Am", "Continue: running git am --continue");
        MergeDebugHelper.LogMergeState("BeforeContinueAm", repoPath);

        var result = await _context.CommandRunner.RunAsync(
            repoPath, ["am", "--continue"], cancellationToken: cancellationToken);

        MergeDebugHelper.LogMergeState("AfterContinueAm", repoPath);

        if (result.Success)
        {
            return new Models.MergeResult { Success = true };
        }

        // Paused again on the next patch — the rebase-apply dir + applying
        // marker are still on disk. Mirror RebaseOperations.ContinueRebaseAsync
        // so the merge editor can keep treating "another conflict" the same
        // way for both backends. Use the resolved gitdir so worktrees probe
        // the right rebase-apply directory.
        var gitDir = await ResolveGitDirAsync(repoPath, cancellationToken);
        var stillPaused = File.Exists(Path.Combine(gitDir, "rebase-apply", "applying"));
        if (stillPaused)
        {
            Log.Info("Am", $"Continue: paused on next patch (exit {result.ExitCode}).");
            return new Models.MergeResult
            {
                Success = false,
                HasConflicts = true,
                ErrorMessage = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput.Trim()
                    : result.StandardError.Trim(),
            };
        }

        Log.Error("Am", $"Continue failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
        return new Models.MergeResult
        {
            Success = false,
            ErrorMessage = string.IsNullOrWhiteSpace(result.StandardError)
                ? $"git am --continue exited with code {result.ExitCode}."
                : result.StandardError.Trim(),
        };
    }

    public async Task<Models.MergeResult> SkipAmAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        Log.Info("Am", "Skip: running git am --skip");
        var result = await _context.CommandRunner.RunAsync(
            repoPath, ["am", "--skip"], cancellationToken: cancellationToken);

        var gitDir = await ResolveGitDirAsync(repoPath, cancellationToken);
        if (result.Success && !File.Exists(Path.Combine(gitDir, "rebase-apply", "applying")))
        {
            return new Models.MergeResult { Success = true };
        }
        if (result.Success)
        {
            // Skipped past one patch but the next one conflicts too. We
            // surface a non-empty ErrorMessage so the merge editor's
            // "another conflict" label has something to show — git itself
            // doesn't print on the success-but-still-applying path.
            return new Models.MergeResult
            {
                Success = false,
                HasConflicts = true,
                ErrorMessage = "Patch skipped, but another patch in the series conflicts.",
            };
        }

        Log.Error("Am", $"Skip failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
        return new Models.MergeResult
        {
            Success = false,
            ErrorMessage = string.IsNullOrWhiteSpace(result.StandardError)
                ? $"git am --skip exited with code {result.ExitCode}."
                : result.StandardError.Trim(),
        };
    }

    public async Task AbortAmAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        Log.Info("Am", "Abort: running git am --abort");
        MergeDebugHelper.LogMergeState("BeforeAbortAm", repoPath);
        var result = await _context.CommandRunner.RunAsync(
            repoPath, ["am", "--abort"], cancellationToken: cancellationToken);
        MergeDebugHelper.LogMergeState("AfterAbortAm", repoPath);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.StandardError)
                    ? $"git am --abort exited with code {result.ExitCode}."
                    : result.StandardError.Trim());
        }
    }

    /// <summary>
    /// Resolve the absolute path to <paramref name="repoPath"/>'s git
    /// directory. For the main worktree this is <c>repoPath/.git</c>; for
    /// a linked worktree it is somewhere under <c>main/.git/worktrees/…</c>.
    /// We ask git directly via <c>rev-parse --absolute-git-dir</c> rather
    /// than reasoning about the on-disk shape — the answer is authoritative
    /// and this is the same approach git's own porcelain uses. On rare
    /// failure (not-a-repo, git missing) we fall back to the legacy
    /// <c>repoPath/.git</c> guess so error reporting from the caller
    /// still surfaces a useful path.
    /// </summary>
    private async Task<string> ResolveGitDirAsync(string repoPath, CancellationToken cancellationToken)
    {
        var probe = await _context.CommandRunner.RunAsync(
            repoPath, ["rev-parse", "--absolute-git-dir"], cancellationToken: cancellationToken);
        if (probe.Success)
        {
            var resolved = probe.StandardOutput.Trim();
            if (!string.IsNullOrEmpty(resolved)) return resolved;
        }
        Log.Info("Am", $"ResolveGitDir failed for '{repoPath}'; falling back to <repo>/.git.");
        return Path.Combine(repoPath, ".git");
    }
}
