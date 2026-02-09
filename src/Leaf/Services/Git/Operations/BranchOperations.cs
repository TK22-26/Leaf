using System.IO;
using Leaf.Models;
using Leaf.Services.Git.Core;
using LibGit2Sharp;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Operations for managing branches.
/// </summary>
internal class BranchOperations
{
    private readonly IGitOperationContext _context;

    public BranchOperations(IGitOperationContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Get all branches in the repository.
    /// </summary>
    public Task<List<BranchInfo>> GetBranchesAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var isDetached = repo.Info.IsHeadDetached;
            var currentBranch = isDetached ? null : repo.Head?.FriendlyName;

            return repo.Branches
                .Select(b => new BranchInfo
                {
                    FullName = b.CanonicalName,
                    Name = b.FriendlyName,
                    IsCurrent = !isDetached && string.Equals(b.FriendlyName, currentBranch, StringComparison.OrdinalIgnoreCase),
                    IsRemote = b.IsRemote,
                    RemoteName = b.RemoteName,
                    TrackingBranchName = b.TrackedBranch?.FriendlyName,
                    TipSha = b.Tip?.Sha ?? "",
                    AheadBy = b.TrackingDetails?.AheadBy ?? 0,
                    BehindBy = b.TrackingDetails?.BehindBy ?? 0
                })
                .ToList();
        });
    }

    /// <summary>
    /// Checkout a branch.
    /// </summary>
    public async Task CheckoutAsync(string repoPath, string branchName, bool allowConflicts = false)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            // Check if there's a merge in progress with conflicts
            if (repo.Index.Conflicts.Any())
            {
                throw new InvalidOperationException(
                    "Cannot switch branches: there are unresolved merge conflicts. " +
                    "Please resolve the conflicts or abort the merge first.");
            }

            // Check if repo is in a merge state
            var mergeHeadPath = Path.Combine(repoPath, ".git", "MERGE_HEAD");
            if (File.Exists(mergeHeadPath))
            {
                throw new InvalidOperationException(
                    "Cannot switch branches: a merge is in progress. " +
                    "Please complete or abort the merge first.");
            }

            // Find the branch (normalize remote names)
            var branch = repo.Branches[branchName];

            // Determine remote name and local short name by checking actual remotes
            // (naive first-slash split fails for branch names like "users/Jacob/feature")
            string shortName = branchName;
            if (branch != null && branch.IsRemote)
            {
                foreach (var remote in repo.Network.Remotes)
                {
                    var prefix = remote.Name + "/";
                    if (branchName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        shortName = branchName[prefix.Length..];
                        break;
                    }
                }

                var localBranch = repo.Branches[shortName];
                if (localBranch == null)
                {
                    localBranch = repo.CreateBranch(shortName, branch.Tip);
                    repo.Branches.Update(localBranch, b => b.TrackedBranch = branch.CanonicalName);
                }
                branch = localBranch;
            }

            if (branch == null)
            {
                // Try to find remote branch and create local tracking branch
                // Search all remotes since branchName may contain slashes
                Branch? remoteBranch = null;
                foreach (var remote in repo.Network.Remotes)
                {
                    var candidate = repo.Branches[$"{remote.Name}/{branchName}"];
                    if (candidate != null && candidate.IsRemote)
                    {
                        remoteBranch = candidate;
                        break;
                    }
                }

                if (remoteBranch != null)
                {
                    branch = repo.CreateBranch(branchName, remoteBranch.Tip);
                    repo.Branches.Update(branch, b => b.TrackedBranch = remoteBranch.CanonicalName);
                }
                else
                {
                    throw new InvalidOperationException($"Branch '{branchName}' not found");
                }
            }

            if (allowConflicts)
            {
                var result = GitCliHelpers.RunGit(repoPath, $"checkout -m \"{branch.FriendlyName}\"");
                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Checkout failed: {result.Error}");
                }
                return;
            }

            Commands.Checkout(repo, branch);
        });
    }

    /// <summary>
    /// Checkout a specific commit (detached HEAD).
    /// </summary>
    public Task CheckoutCommitAsync(string repoPath, string commitSha)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            // Check if there's a merge in progress with conflicts
            if (repo.Index.Conflicts.Any())
            {
                throw new InvalidOperationException(
                    "Cannot checkout commit: there are unresolved merge conflicts. " +
                    "Please resolve the conflicts or abort the merge first.");
            }

            // Check if repo is in a merge state
            var mergeHeadPath = Path.Combine(repoPath, ".git", "MERGE_HEAD");
            if (File.Exists(mergeHeadPath))
            {
                throw new InvalidOperationException(
                    "Cannot checkout commit: a merge is in progress. " +
                    "Please complete or abort the merge first.");
            }

            // Find the commit
            var commit = repo.Lookup<Commit>(commitSha);
            if (commit == null)
            {
                throw new InvalidOperationException($"Commit '{commitSha}' not found");
            }

            // Checkout the commit (detached HEAD)
            Commands.Checkout(repo, commit);
        });
    }

    /// <summary>
    /// Create a new branch.
    /// </summary>
    public Task CreateBranchAsync(string repoPath, string branchName, bool checkout = true)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var branch = repo.CreateBranch(branchName);
            if (checkout)
            {
                Commands.Checkout(repo, branch);
            }
        });
    }

    /// <summary>
    /// Create a new branch at a specific commit.
    /// </summary>
    public Task CreateBranchAtCommitAsync(string repoPath, string branchName, string commitSha, bool checkout = true)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var commit = repo.Lookup<Commit>(commitSha);
            if (commit == null)
            {
                throw new InvalidOperationException($"Commit '{commitSha}' not found.");
            }

            var branch = repo.CreateBranch(branchName, commit);
            if (checkout)
            {
                Commands.Checkout(repo, branch);
            }
        });
    }

    /// <summary>
    /// Delete a local branch.
    /// </summary>
    public Task DeleteBranchAsync(string repoPath, string branchName, bool force = false)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var branch = repo.Branches[branchName];
            if (branch == null)
            {
                throw new InvalidOperationException($"Branch '{branchName}' not found.");
            }

            if (branch.IsCurrentRepositoryHead)
            {
                throw new InvalidOperationException("Cannot delete the currently checked out branch.");
            }

            repo.Branches.Remove(branch);
        });
    }

    /// <summary>
    /// Delete a remote branch.
    /// </summary>
    public async Task DeleteRemoteBranchAsync(string repoPath, string remoteName, string branchName,
        string? username = null, string? password = null)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["push", remoteName, "--delete", branchName]);

        if (!result.Success)
        {
            throw new InvalidOperationException(result.StandardError);
        }
    }

    /// <summary>
    /// Rename a local branch.
    /// </summary>
    public async Task RenameBranchAsync(string repoPath, string oldName, string newName)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["branch", "-m", oldName, newName]);

        if (!result.Success)
        {
            throw new InvalidOperationException(result.StandardError);
        }
    }

    /// <summary>
    /// Set upstream tracking for a branch.
    /// </summary>
    public async Task SetUpstreamAsync(string repoPath, string branchName, string remoteName, string remoteBranchName)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["branch", "--set-upstream-to", $"{remoteName}/{remoteBranchName}", branchName]);

        if (!result.Success)
        {
            throw new InvalidOperationException(result.StandardError);
        }
    }

    /// <summary>
    /// Reset a branch to a specific commit.
    /// </summary>
    public async Task ResetBranchToCommitAsync(string repoPath, string branchName, string commitSha, bool updateWorkingTree)
    {
        var result = updateWorkingTree
            ? await _context.CommandRunner.RunAsync(repoPath, ["reset", "--hard", commitSha])
            : await _context.CommandRunner.RunAsync(repoPath, ["branch", "-f", branchName, commitSha]);

        if (!result.Success)
        {
            throw new InvalidOperationException(result.StandardError);
        }
    }
}
