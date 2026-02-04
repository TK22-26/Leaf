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
    /// Add a new remote to the repository.
    /// </summary>
    public async Task AddRemoteAsync(string repoPath, string remoteName, string url, string? pushUrl = null)
    {
        var result = await _context.CommandRunner.RunAsync(repoPath, ["remote", "add", remoteName, url]);
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
                ["remote", "set-url", "--push", remoteName, pushUrl]);
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
    public async Task RemoveRemoteAsync(string repoPath, string remoteName)
    {
        var result = await _context.CommandRunner.RunAsync(repoPath, ["remote", "remove", remoteName]);
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
    public async Task RenameRemoteAsync(string repoPath, string oldName, string newName)
    {
        var result = await _context.CommandRunner.RunAsync(repoPath, ["remote", "rename", oldName, newName]);
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
    public async Task SetRemoteUrlAsync(string repoPath, string remoteName, string url, bool isPushUrl = false)
    {
        var args = isPushUrl
            ? new[] { "remote", "set-url", "--push", remoteName, url }
            : new[] { "remote", "set-url", remoteName, url };

        var result = await _context.CommandRunner.RunAsync(repoPath, args);
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

        // If password provided, use authenticated URL
        string fetchTarget = remoteName;
        if (!string.IsNullOrEmpty(password))
        {
            var remotes = await GetRemotesAsync(repoPath);
            var remote = remotes.FirstOrDefault(r => r.Name.Equals(remoteName, StringComparison.OrdinalIgnoreCase));
            if (remote != null && !string.IsNullOrEmpty(remote.Url))
            {
                var authUrl = BuildAuthenticatedUrl(remote.Url, password);
                if (authUrl != null)
                {
                    fetchTarget = authUrl;
                }
            }
        }

        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["fetch", "--prune", fetchTarget]);

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

        string[] args = ["pull"];

        // If password provided, try to use authenticated URL
        if (!string.IsNullOrEmpty(password))
        {
            // Determine which remote to use (from tracking branch)
            string? trackingRemoteName = null;
            string? trackingBranchName = null;

            using (var repo = new Repository(repoPath))
            {
                if (repo.Head.TrackedBranch != null)
                {
                    // Extract remote name from tracking branch (e.g., "origin/main" -> "origin")
                    var tracking = repo.Head.TrackedBranch.FriendlyName;
                    var slashIndex = tracking.IndexOf('/');
                    if (slashIndex > 0)
                    {
                        trackingRemoteName = tracking[..slashIndex];
                        trackingBranchName = tracking[(slashIndex + 1)..];
                    }
                }
            }

            if (!string.IsNullOrEmpty(trackingRemoteName))
            {
                var remotes = await GetRemotesAsync(repoPath);
                var remote = remotes.FirstOrDefault(r => r.Name.Equals(trackingRemoteName, StringComparison.OrdinalIgnoreCase));
                if (remote != null && !string.IsNullOrEmpty(remote.Url))
                {
                    var authUrl = BuildAuthenticatedUrl(remote.Url, password);
                    if (authUrl != null && !string.IsNullOrEmpty(trackingBranchName))
                    {
                        args = ["pull", authUrl, trackingBranchName];
                    }
                }
            }
        }

        var result = await _context.CommandRunner.RunAsync(repoPath, args);

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
    /// <param name="username">Optional username for authentication</param>
    /// <param name="password">Optional password/token for authentication</param>
    /// <param name="progress">Optional progress reporter</param>
    public async Task PushAsync(string repoPath, string? remoteName = null, string? username = null,
        string? password = null, IProgress<string>? progress = null)
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

        // Build push arguments
        string[] args;
        string pushTarget = targetRemote;

        // If password provided, try to use authenticated URL
        if (!string.IsNullOrEmpty(password))
        {
            var remotes = await GetRemotesAsync(repoPath);
            var remote = remotes.FirstOrDefault(r => r.Name.Equals(targetRemote, StringComparison.OrdinalIgnoreCase));
            if (remote != null)
            {
                // Use push URL if available, otherwise fetch URL
                var url = remote.PushUrl ?? remote.Url;
                if (!string.IsNullOrEmpty(url))
                {
                    var authUrl = BuildAuthenticatedUrl(url, password);
                    if (authUrl != null)
                    {
                        pushTarget = authUrl;
                    }
                }
            }
        }

        if (!hasTrackingBranch)
        {
            // No tracking branch - need to set upstream
            // When using URL, we can't use -u (upstream setting requires remote name)
            // So push with refspec instead
            args = pushTarget == targetRemote
                ? ["push", "-u", targetRemote, branchName]
                : ["push", pushTarget, $"{branchName}:{branchName}"];
        }
        else
        {
            // Has tracking branch - push to that remote
            args = pushTarget == targetRemote
                ? ["push"]
                : ["push", pushTarget, branchName];
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
    /// Builds a URL with embedded credentials for authentication.
    /// For HTTPS URLs: https://user:token@host/path
    /// </summary>
    /// <param name="url">The remote URL</param>
    /// <param name="password">The PAT/password to embed</param>
    /// <returns>URL with credentials embedded, or null if URL format not supported</returns>
    private static string? BuildAuthenticatedUrl(string url, string password)
    {
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(password))
            return null;

        // Only works with HTTPS URLs
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme != "https" && uri.Scheme != "http")
            return null;

        // Build URL with credentials
        // Use "x-access-token" as username for GitHub, or existing username if present
        var existingUser = !string.IsNullOrEmpty(uri.UserInfo) && uri.UserInfo.Contains('@')
            ? uri.UserInfo.Split('@')[0]
            : null;

        // Azure DevOps and GitHub both accept PAT as password with any username
        var username = existingUser ?? "x-access-token";

        // Encode password in case it contains special characters
        var encodedPassword = Uri.EscapeDataString(password);

        // Reconstruct URL with credentials
        var portPart = uri.IsDefaultPort ? "" : $":{uri.Port}";
        return $"{uri.Scheme}://{username}:{encodedPassword}@{uri.Host}{portPart}{uri.PathAndQuery}";
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
