using System.IO;
using Leaf.Models;
using Leaf.Services.Git.Core;
using LibGit2Sharp;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Operations for remote synchronization (clone, fetch, pull, push).
/// Authentication is routed through GIT_ASKPASS (see <see cref="GitCommandRunner"/>)
/// — PATs never enter git URLs or command lines.
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
    public Task<List<RemoteInfo>> GetRemotesAsync(string repoPath, CancellationToken cancellationToken = default)
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
        }, cancellationToken);
    }

    /// <summary>
    /// Add a new remote to the repository.
    /// </summary>
    public async Task AddRemoteAsync(string repoPath, string remoteName, string url, string? pushUrl = null, CancellationToken cancellationToken = default)
    {
        var result = await _context.CommandRunner.RunAsync(repoPath, ["remote", "add", remoteName, url], cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.IsNullOrEmpty(result.StandardError)
                ? $"Failed to add remote '{remoteName}'"
                : result.StandardError);
        }

        // Set separate push URL if provided
        if (!string.IsNullOrEmpty(pushUrl))
        {
            var pushResult = await _context.CommandRunner.RunAsync(repoPath,
                ["remote", "set-url", "--push", remoteName, pushUrl], cancellationToken: cancellationToken);
            if (!pushResult.Success)
            {
                throw new InvalidOperationException(string.IsNullOrEmpty(pushResult.StandardError)
                    ? $"Failed to set push URL for remote '{remoteName}'"
                    : pushResult.StandardError);
            }
        }
    }

    /// <summary>
    /// Remove a remote from the repository.
    /// </summary>
    public async Task RemoveRemoteAsync(string repoPath, string remoteName, CancellationToken cancellationToken = default)
    {
        var result = await _context.CommandRunner.RunAsync(repoPath, ["remote", "remove", remoteName], cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.IsNullOrEmpty(result.StandardError)
                ? $"Failed to remove remote '{remoteName}'"
                : result.StandardError);
        }
    }

    /// <summary>
    /// Rename a remote.
    /// </summary>
    public async Task RenameRemoteAsync(string repoPath, string oldName, string newName, CancellationToken cancellationToken = default)
    {
        var result = await _context.CommandRunner.RunAsync(repoPath, ["remote", "rename", oldName, newName], cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.IsNullOrEmpty(result.StandardError)
                ? $"Failed to rename remote '{oldName}' to '{newName}'"
                : result.StandardError);
        }
    }

    /// <summary>
    /// Set a remote's URL.
    /// </summary>
    public async Task SetRemoteUrlAsync(string repoPath, string remoteName, string url, bool isPushUrl = false, CancellationToken cancellationToken = default)
    {
        var args = isPushUrl
            ? new[] { "remote", "set-url", "--push", remoteName, url }
            : new[] { "remote", "set-url", remoteName, url };

        var result = await _context.CommandRunner.RunAsync(repoPath, args, cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.IsNullOrEmpty(result.StandardError)
                ? $"Failed to set URL for remote '{remoteName}'"
                : result.StandardError);
        }
    }

    /// <summary>
    /// Clone a remote repository.
    /// </summary>
    /// <param name="credentialKey">Optional credential storage key for GIT_ASKPASS auth.</param>
    public async Task<string> CloneAsync(string url, string localPath, string? credentialKey = null,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Cloning repository...");

        var result = await _context.CommandRunner.RunAsync(
            Path.GetDirectoryName(localPath) ?? ".",
            ["clone", "--progress", url, localPath],
            input: null,
            credentialKey: credentialKey, cancellationToken: cancellationToken);

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
    /// <param name="credentialKey">Optional credential storage key for GIT_ASKPASS auth.</param>
    public async Task FetchAsync(string repoPath, string remoteName = "origin", string? credentialKey = null,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Fetching...");

        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["fetch", "--prune", remoteName],
            input: null,
            credentialKey: credentialKey, cancellationToken: cancellationToken);

        if (!result.Success && !string.IsNullOrEmpty(result.StandardError))
        {
            throw new InvalidOperationException(result.StandardError);
        }
    }

    /// <summary>
    /// Pull from remote.
    /// </summary>
    /// <param name="credentialKey">Optional credential storage key for GIT_ASKPASS auth.</param>
    public async Task PullAsync(string repoPath, string? credentialKey = null,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Pulling...");

        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["pull"],
            input: null,
            credentialKey: credentialKey, cancellationToken: cancellationToken);

        if (!result.Success && !string.IsNullOrEmpty(result.StandardError))
        {
            throw new InvalidOperationException(result.StandardError);
        }
    }

    /// <summary>
    /// Push to remote.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="remoteName">Optional remote name (uses tracking branch's remote or default if not specified)</param>
    /// <param name="credentialKey">Optional credential storage key for GIT_ASKPASS auth.</param>
    /// <param name="progress">Optional progress reporter</param>
    public async Task PushAsync(string repoPath, string? remoteName = null, string? credentialKey = null,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        // Check if we're in detached HEAD state
        string branchName;
        bool hasTrackingBranch;
        using (var repo = new Repository(repoPath))
        {
            if (repo.Info.IsHeadDetached)
            {
                throw new InvalidOperationException("Cannot push while in detached HEAD state.");
            }
            branchName = repo.Head.FriendlyName;
            hasTrackingBranch = repo.Head.TrackedBranch != null;
        }

        // Determine the target remote
        var targetRemote = remoteName ?? await GetDefaultRemoteAsync(repoPath);

        // Build push arguments. With GIT_ASKPASS there is no need to mutate the
        // URL; pushing by remote name preserves upstream tracking semantics.
        string[] args = hasTrackingBranch
            ? ["push"]
            : ["push", "-u", targetRemote, branchName];

        progress?.Report("Pushing...");

        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            args,
            input: null,
            credentialKey: credentialKey, cancellationToken: cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException(string.IsNullOrEmpty(result.StandardError)
                ? "Push failed"
                : result.StandardError);
        }
    }

    /// <summary>
    /// Get the default remote for a repository.
    /// Prefers "origin" if it exists, otherwise returns the first available remote.
    /// </summary>
    private async Task<string> GetDefaultRemoteAsync(string repoPath)
    {
        var remotes = await GetRemotesAsync(repoPath);
        return remotes.FirstOrDefault(r => r.Name == "origin")?.Name
               ?? remotes.FirstOrDefault()?.Name
               ?? "origin";
    }

    /// <summary>
    /// Pull updates for a specific branch (fast-forward if possible).
    /// </summary>
    public async Task PullBranchFastForwardAsync(string repoPath, string branchName, string remoteName,
        string remoteBranchName, bool isCurrentBranch, CancellationToken cancellationToken = default)
    {
        var args = isCurrentBranch
            ? new[] { "pull", "--ff-only", remoteName, remoteBranchName }
            : new[] { "fetch", remoteName, $"{remoteBranchName}:{branchName}" };

        var result = await _context.CommandRunner.RunAsync(repoPath, args, cancellationToken: cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException(result.StandardError);
        }
    }

    /// <summary>
    /// Push a specific branch to remote.
    /// </summary>
    public async Task PushBranchAsync(string repoPath, string branchName, string remoteName,
        string remoteBranchName, bool isCurrentBranch, CancellationToken cancellationToken = default)
    {
        var args = isCurrentBranch
            ? new[] { "push", remoteName, branchName }
            : new[] { "push", remoteName, $"{branchName}:{remoteBranchName}" };

        var result = await _context.CommandRunner.RunAsync(repoPath, args, cancellationToken: cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException(result.StandardError);
        }
    }
}
