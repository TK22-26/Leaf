using System.IO;
using Leaf.Services.Git.Core;
using LibGit2Sharp;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Operations for diff retrieval.
/// </summary>
internal class DiffOperations
{
    private readonly IGitOperationContext _context;

    public DiffOperations(IGitOperationContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Get diff content for a specific file in a commit.
    /// </summary>
    public Task<(string oldContent, string newContent)> GetFileDiffAsync(string repoPath, string sha, string filePath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var commit = repo.Lookup<Commit>(sha);
            if (commit == null) return ("", "");

            var parent = commit.Parents.FirstOrDefault();

            string oldContent = "";
            string newContent = "";

            // Get old content from parent
            if (parent != null)
            {
                var oldEntry = parent[filePath];
                if (oldEntry?.Target is Blob oldBlob && !oldBlob.IsBinary)
                {
                    oldContent = oldBlob.GetContentText();
                }
            }

            // Get new content from commit
            var newEntry = commit[filePath];
            if (newEntry?.Target is Blob newBlob && !newBlob.IsBinary)
            {
                newContent = newBlob.GetContentText();
            }

            return (oldContent, newContent);
        });
    }

    /// <summary>
    /// Get diff content for an unstaged file (working directory vs index).
    /// </summary>
    public Task<(string oldContent, string newContent)> GetUnstagedFileDiffAsync(string repoPath, string filePath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            string oldContent = "";
            string newContent = "";

            // Get content from index (staged version)
            var indexEntry = repo.Index[filePath];
            if (indexEntry != null)
            {
                var blob = repo.Lookup<Blob>(indexEntry.Id);
                if (blob != null && !blob.IsBinary)
                {
                    oldContent = blob.GetContentText();
                }
            }
            else
            {
                // File not in index, check HEAD
                var headCommit = repo.Head?.Tip;
                if (headCommit != null)
                {
                    var headEntry = headCommit[filePath];
                    if (headEntry?.Target is Blob headBlob && !headBlob.IsBinary)
                    {
                        oldContent = headBlob.GetContentText();
                    }
                }
            }

            // Get content from working directory
            var fullPath = Path.Combine(repoPath, filePath);
            if (File.Exists(fullPath))
            {
                try
                {
                    newContent = File.ReadAllText(fullPath);
                }
                catch
                {
                    // File might be locked or binary
                }
            }

            return (oldContent, newContent);
        });
    }

    /// <summary>
    /// Get diff content for a staged file (index vs HEAD).
    /// </summary>
    public Task<(string oldContent, string newContent)> GetStagedFileDiffAsync(string repoPath, string filePath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            string oldContent = "";
            string newContent = "";

            // Get content from HEAD
            var headCommit = repo.Head?.Tip;
            if (headCommit != null)
            {
                var headEntry = headCommit[filePath];
                if (headEntry?.Target is Blob headBlob && !headBlob.IsBinary)
                {
                    oldContent = headBlob.GetContentText();
                }
            }

            // Get content from index (staged version)
            var indexEntry = repo.Index[filePath];
            if (indexEntry != null)
            {
                var blob = repo.Lookup<Blob>(indexEntry.Id);
                if (blob != null && !blob.IsBinary)
                {
                    newContent = blob.GetContentText();
                }
            }

            return (oldContent, newContent);
        });
    }

    /// <summary>
    /// Get a unified diff between a commit and the working tree.
    /// </summary>
    public async Task<string> GetCommitToWorkingTreeDiffAsync(string repoPath, string commitSha)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["diff", commitSha]);

        if (!result.Success)
        {
            var message = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            throw new InvalidOperationException(message);
        }

        return result.StandardOutput;
    }
}
