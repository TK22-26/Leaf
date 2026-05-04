using System.IO;
using Leaf.Models;
using Leaf.Services.Git.Core;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Operations for managing git worktrees.
/// </summary>
internal class WorktreeOperations
{
    private readonly IGitOperationContext _context;

    public WorktreeOperations(IGitOperationContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Get all worktrees for the repository.
    /// </summary>
    public async Task<List<WorktreeInfo>> GetWorktreesAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["worktree", "list", "--porcelain"], cancellationToken: cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                string.IsNullOrEmpty(result.StandardError)
                    ? "Failed to list worktrees"
                    : result.StandardError);
        }

        return ParseWorktreeListOutput(result.StandardOutput);
    }

    /// <summary>
    /// Create a new worktree for an existing branch.
    /// </summary>
    public async Task CreateWorktreeAsync(string repoPath, string worktreePath, string branchName, CancellationToken cancellationToken = default)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["worktree", "add", worktreePath, branchName], cancellationToken: cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                string.IsNullOrEmpty(result.StandardError)
                    ? "Failed to create worktree"
                    : result.StandardError);
        }
    }

    /// <summary>
    /// Create a new worktree with a new branch.
    /// </summary>
    public async Task CreateWorktreeWithNewBranchAsync(string repoPath, string worktreePath, string newBranchName, string? startPoint = null, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "worktree", "add", "-b", newBranchName, worktreePath };
        if (!string.IsNullOrEmpty(startPoint))
        {
            args.Add(startPoint);
        }

        var result = await _context.CommandRunner.RunAsync(repoPath, args, cancellationToken: cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                string.IsNullOrEmpty(result.StandardError)
                    ? "Failed to create worktree with new branch"
                    : result.StandardError);
        }
    }

    /// <summary>
    /// Create a new worktree in detached HEAD state at a specific commit.
    /// </summary>
    public async Task CreateWorktreeDetachedAsync(string repoPath, string worktreePath, string commitSha, CancellationToken cancellationToken = default)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["worktree", "add", "--detach", worktreePath, commitSha], cancellationToken: cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                string.IsNullOrEmpty(result.StandardError)
                    ? "Failed to create detached worktree"
                    : result.StandardError);
        }
    }

    /// <summary>
    /// Remove a worktree.
    /// </summary>
    public async Task RemoveWorktreeAsync(string repoPath, string worktreePath, bool force = false, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "worktree", "remove" };
        if (force)
        {
            args.Add("--force");
        }
        args.Add(worktreePath);

        var result = await _context.CommandRunner.RunAsync(repoPath, args, cancellationToken: cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                string.IsNullOrEmpty(result.StandardError)
                    ? "Failed to remove worktree"
                    : result.StandardError);
        }
    }

    /// <summary>
    /// Lock a worktree to prevent removal.
    /// </summary>
    public async Task LockWorktreeAsync(string repoPath, string worktreePath, string? reason = null, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "worktree", "lock" };
        if (!string.IsNullOrEmpty(reason))
        {
            args.Add("--reason");
            args.Add(reason);
        }
        args.Add(worktreePath);

        var result = await _context.CommandRunner.RunAsync(repoPath, args, cancellationToken: cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                string.IsNullOrEmpty(result.StandardError)
                    ? "Failed to lock worktree"
                    : result.StandardError);
        }
    }

    /// <summary>
    /// Unlock a worktree.
    /// </summary>
    public async Task UnlockWorktreeAsync(string repoPath, string worktreePath, CancellationToken cancellationToken = default)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["worktree", "unlock", worktreePath], cancellationToken: cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                string.IsNullOrEmpty(result.StandardError)
                    ? "Failed to unlock worktree"
                    : result.StandardError);
        }
    }

    /// <summary>
    /// Prune stale worktree references.
    /// </summary>
    public async Task PruneWorktreesAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["worktree", "prune"], cancellationToken: cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                string.IsNullOrEmpty(result.StandardError)
                    ? "Failed to prune worktrees"
                    : result.StandardError);
        }
    }

    /// <summary>
    /// Sanitize a branch name for use as a directory name. Replaces the
    /// path separator <c>/</c> and any OS-invalid file name chars with
    /// <c>-</c>. Extracted so the worktree UI preview can show the same
    /// sanitized form that actually lands on disk, without duplicating
    /// the rule.
    /// </summary>
    public static string SanitizeBranchNameForPath(string branchName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Concat(branchName.Select(c =>
            c == '/' || invalidChars.Contains(c) ? '-' : c));
    }

    /// <summary>
    /// Generate a default worktree path as a sibling directory.
    /// </summary>
    public static string GenerateDefaultWorktreePath(string repoPath, string branchName)
    {
        // Normalize the path by removing trailing separators first
        var normalizedPath = repoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parentDir = Path.GetDirectoryName(normalizedPath)!;
        var repoName = Path.GetFileName(normalizedPath);
        var safeBranchName = SanitizeBranchNameForPath(branchName);

        var basePath = Path.Combine(parentDir, $"{repoName}-{safeBranchName}");

        // Ensure uniqueness - append number if path exists
        var finalPath = basePath;
        var counter = 2;
        while (Directory.Exists(finalPath))
        {
            finalPath = $"{basePath}-{counter++}";
        }

        return finalPath;
    }

    /// <summary>
    /// Parse the output of 'git worktree list --porcelain'.
    /// </summary>
    internal static List<WorktreeInfo> ParseWorktreeListOutput(string output)
    {
        var worktrees = new List<WorktreeInfo>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        WorktreeInfo? current = null;
        var isFirstWorktree = true;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("worktree "))
            {
                // Save the previous worktree if any
                if (current != null)
                {
                    worktrees.Add(current);
                }

                current = new WorktreeInfo
                {
                    // Normalize path separators - git outputs forward slashes on Windows
                    Path = Path.GetFullPath(line["worktree ".Length..].Trim()),
                    // Main worktree is ALWAYS first in git worktree list output
                    IsMainWorktree = isFirstWorktree
                };
                isFirstWorktree = false;
            }
            else if (current != null)
            {
                if (line.StartsWith("HEAD "))
                {
                    current.HeadSha = line["HEAD ".Length..].Trim();
                }
                else if (line.StartsWith("branch refs/heads/"))
                {
                    current.BranchName = line["branch refs/heads/".Length..].Trim();
                }
                else if (line.StartsWith("branch "))
                {
                    // Fallback for other branch formats
                    current.BranchName = line["branch ".Length..].Trim();
                }
                else if (line == "detached")
                {
                    current.IsDetached = true;
                }
                else if (line == "locked")
                {
                    current.IsLocked = true;
                }
                else if (line.StartsWith("locked "))
                {
                    current.IsLocked = true;
                    current.LockReason = line["locked ".Length..].Trim();
                }
            }
        }

        // Don't forget the last worktree
        if (current != null)
        {
            worktrees.Add(current);
        }

        return worktrees;
    }
}
