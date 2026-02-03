using System.IO;
using Leaf.Models;
using Leaf.Services.Git.Core;
using LibGit2Sharp;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Operations for remote synchronization (clone, fetch, pull, push).
/// </summary>
internal class RemoteSyncOperations
{
    private readonly IGitOperationContext _context;

    public RemoteSyncOperations(IGitOperationContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Get all remotes in the repository.
    /// </summary>
    public Task<List<RemoteInfo>> GetRemotesAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            return repo.Network.Remotes
                .Select(r => new RemoteInfo
                {
                    Name = r.Name,
                    Url = r.Url,
                    PushUrl = r.PushUrl != r.Url ? r.PushUrl : null
                })
                .ToList();
        });
    }

    /// <summary>
    /// Clone a remote repository.
    /// </summary>
    public async Task<string> CloneAsync(string url, string localPath, string? username = null,
        string? password = null, IProgress<string>? progress = null)
    {
        progress?.Report("Cloning repository...");

        var result = await _context.CommandRunner.RunAsync(
            Path.GetDirectoryName(localPath) ?? ".",
            ["clone", "--progress", url, localPath]);

        if (!result.Success)
        {
            throw new InvalidOperationException(string.IsNullOrEmpty(result.StandardError)
                ? "Clone failed"
                : result.StandardError);
        }

        return localPath;
    }

    /// <summary>
    /// Fetch from remote.
    /// </summary>
    public async Task FetchAsync(string repoPath, string remoteName = "origin", string? username = null,
        string? password = null, IProgress<string>? progress = null)
    {
        progress?.Report("Fetching...");

        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["fetch", "--prune", remoteName]);

        if (!result.Success && !string.IsNullOrEmpty(result.StandardError))
        {
            throw new InvalidOperationException(result.StandardError);
        }
    }

    /// <summary>
    /// Pull from remote.
    /// </summary>
    public async Task PullAsync(string repoPath, string? username = null, string? password = null,
        IProgress<string>? progress = null)
    {
        progress?.Report("Pulling...");

        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["pull"]);

        if (!result.Success && !string.IsNullOrEmpty(result.StandardError))
        {
            throw new InvalidOperationException(result.StandardError);
        }
    }

    /// <summary>
    /// Push to remote.
    /// </summary>
    public async Task PushAsync(string repoPath, string? username = null, string? password = null,
        IProgress<string>? progress = null)
    {
        // Check if we're in detached HEAD state
        using (var repo = new Repository(repoPath))
        {
            if (repo.Info.IsHeadDetached)
            {
                throw new InvalidOperationException("Cannot push while in detached HEAD state.");
            }
        }

        // Get branch name and determine push args
        string[] args;
        using (var repo = new Repository(repoPath))
        {
            var branchName = repo.Head.FriendlyName;
            args = repo.Head.TrackedBranch == null
                ? ["push", "-u", "origin", branchName]
                : ["push"];
        }

        progress?.Report("Pushing...");

        var result = await _context.CommandRunner.RunAsync(repoPath, args);

        if (!result.Success)
        {
            throw new InvalidOperationException(string.IsNullOrEmpty(result.StandardError)
                ? "Push failed"
                : result.StandardError);
        }
    }

    /// <summary>
    /// Pull updates for a specific branch (fast-forward if possible).
    /// </summary>
    public async Task PullBranchFastForwardAsync(string repoPath, string branchName, string remoteName,
        string remoteBranchName, bool isCurrentBranch)
    {
        var args = isCurrentBranch
            ? new[] { "pull", "--ff-only", remoteName, remoteBranchName }
            : new[] { "fetch", remoteName, $"{remoteBranchName}:{branchName}" };

        var result = await _context.CommandRunner.RunAsync(repoPath, args);

        if (!result.Success)
        {
            throw new InvalidOperationException(result.StandardError);
        }
    }

    /// <summary>
    /// Push a specific branch to remote.
    /// </summary>
    public async Task PushBranchAsync(string repoPath, string branchName, string remoteName,
        string remoteBranchName, bool isCurrentBranch)
    {
        var args = isCurrentBranch
            ? new[] { "push", remoteName, branchName }
            : new[] { "push", remoteName, $"{branchName}:{remoteBranchName}" };

        var result = await _context.CommandRunner.RunAsync(repoPath, args);

        if (!result.Success)
        {
            throw new InvalidOperationException(result.StandardError);
        }
    }
}
