using Leaf.Models;
using Leaf.Services.Git.Core;
using LibGit2Sharp;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Operations for managing git tags.
/// </summary>
internal class TagOperations
{
    private readonly IGitOperationContext _context;

    public TagOperations(IGitOperationContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Get all tags in the repository.
    /// </summary>
    public Task<List<TagInfo>> GetTagsAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var tags = new List<TagInfo>();

            foreach (var tag in repo.Tags)
            {
                var tagInfo = new TagInfo
                {
                    Name = tag.FriendlyName,
                    TargetSha = tag.Target.Sha,
                    IsAnnotated = tag.IsAnnotated
                };

                if (tag.IsAnnotated && tag.Annotation != null)
                {
                    tagInfo.Message = tag.Annotation.Message;
                    tagInfo.TaggerName = tag.Annotation.Tagger?.Name;
                    tagInfo.TaggerEmail = tag.Annotation.Tagger?.Email;
                    tagInfo.TaggedAt = tag.Annotation.Tagger?.When;
                }

                tags.Add(tagInfo);
            }

            return tags.OrderByDescending(t => t.TaggedAt ?? DateTimeOffset.MinValue).ToList();
        });
    }

    /// <summary>
    /// Create a new tag.
    /// </summary>
    public Task CreateTagAsync(string repoPath, string tagName, string? message = null, string? targetSha = null)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            var target = string.IsNullOrEmpty(targetSha)
                ? repo.Head.Tip
                : repo.Lookup<Commit>(targetSha);

            if (target == null)
            {
                throw new InvalidOperationException($"Target commit '{targetSha}' not found.");
            }

            if (repo.Tags[tagName] != null)
            {
                throw new InvalidOperationException($"Tag '{tagName}' already exists.");
            }

            if (!string.IsNullOrEmpty(message))
            {
                // Create annotated tag
                var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
                repo.ApplyTag(tagName, target.Sha, signature, message);
            }
            else
            {
                // Create lightweight tag
                repo.ApplyTag(tagName, target.Sha);
            }
        });
    }

    /// <summary>
    /// Delete a local tag.
    /// </summary>
    public Task DeleteTagAsync(string repoPath, string tagName)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            var tag = repo.Tags[tagName];
            if (tag == null)
            {
                throw new InvalidOperationException($"Tag '{tagName}' not found.");
            }

            repo.Tags.Remove(tag);
        });
    }

    /// <summary>
    /// Push a tag to remote.
    /// </summary>
    public async Task PushTagAsync(string repoPath, string tagName, string remoteName = "origin",
        string? username = null, string? password = null)
    {
        // Always use git command line - it properly integrates with Git Credential Manager
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["push", remoteName, $"refs/tags/{tagName}"]);

        if (!result.Success)
        {
            throw new InvalidOperationException(string.IsNullOrEmpty(result.StandardError)
                ? "Push tag failed"
                : result.StandardError);
        }
    }

    /// <summary>
    /// Delete a remote tag.
    /// </summary>
    public async Task DeleteRemoteTagAsync(string repoPath, string tagName, string remoteName = "origin",
        string? username = null, string? password = null)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["push", remoteName, "--delete", $"refs/tags/{tagName}"]);

        if (!result.Success)
        {
            throw new InvalidOperationException(result.StandardError);
        }
    }
}
