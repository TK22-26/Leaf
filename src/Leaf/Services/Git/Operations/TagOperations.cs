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
    public Task<List<TagInfo>> GetTagsAsync(string repoPath, CancellationToken cancellationToken = default)
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
        }, cancellationToken);
    }

    /// <summary>
    /// Create a new tag. When the repo's <c>tag.gpgsign</c> is true and a
    /// message is supplied, the tag is signed via <c>git tag -s</c> — that
    /// path goes through the git CLI rather than LibGit2Sharp because
    /// libgit2 doesn't expose a signing hook compatible with the
    /// installed GPG/SSH agent. Lightweight tags can't be signed, so the
    /// signing flag is silently ignored when no message is supplied.
    /// </summary>
    public async Task CreateTagAsync(string repoPath, string tagName, string? message = null, string? targetSha = null, CancellationToken cancellationToken = default)
    {
        // Pre-flight: check tag.gpgsign before we decide which code path
        // to take. Reading config here keeps the decision local to the
        // operation — callers don't need to know about signing config.
        var shouldSign = !string.IsNullOrEmpty(message)
            && await IsTagSigningEnabledAsync(repoPath, cancellationToken).ConfigureAwait(false);

        if (shouldSign)
        {
            await CreateSignedAnnotatedTagAsync(repoPath, tagName, message!, targetSha, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // Unsigned path — keep the existing LibGit2Sharp implementation
        // for parity with the pre-§5.8 behaviour.
        await Task.Run(() =>
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
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Read <c>tag.gpgsign</c> from the repo's effective config (local
    /// then global). Returns false on any error — signing should be
    /// opt-in, never accidentally turned on by a config-read failure.
    /// </summary>
    private async Task<bool> IsTagSigningEnabledAsync(string repoPath, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _context.CommandRunner.RunAsync(
                repoPath,
                ["config", "--get", "--bool", "tag.gpgsign"],
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return result.Success
                && string.Equals(result.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task CreateSignedAnnotatedTagAsync(string repoPath, string tagName, string message, string? targetSha, CancellationToken cancellationToken)
    {
        // git tag -s <name> -m <msg> [<sha>]. Putting -m before the
        // optional positional sha matches git's argument parser (which
        // is forgiving) and avoids the message-vs-target ambiguity that
        // a future maintainer might trip on.
        var args = new List<string> { "tag", "-s", tagName, "-m", message };
        if (!string.IsNullOrEmpty(targetSha)) args.Add(targetSha);

        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            args.ToArray(),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            // git tag -s prints helpful messages on stderr ("gpg: signing
            // failed: No secret key", "error: gpg failed to sign the
            // data"). Surface them verbatim — the user can act on them.
            var detail = string.IsNullOrEmpty(result.StandardError)
                ? "Failed to create signed tag (unknown error)"
                : result.StandardError.Trim();
            throw new InvalidOperationException(detail);
        }
    }

    /// <summary>
    /// Delete a local tag.
    /// </summary>
    public Task DeleteTagAsync(string repoPath, string tagName, CancellationToken cancellationToken = default)
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
        }, cancellationToken);
    }

    /// <summary>
    /// Push a tag to remote.
    /// </summary>
    /// <param name="credentialKey">Optional credential storage key for GIT_ASKPASS auth.</param>
    public async Task PushTagAsync(string repoPath, string tagName, string remoteName = "origin",
        string? credentialKey = null, CancellationToken cancellationToken = default)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["push", remoteName, $"refs/tags/{tagName}"],
            input: null,
            credentialKey: credentialKey, cancellationToken: cancellationToken);

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
    /// <param name="credentialKey">Optional credential storage key for GIT_ASKPASS auth.</param>
    public async Task DeleteRemoteTagAsync(string repoPath, string tagName, string remoteName = "origin",
        string? credentialKey = null, CancellationToken cancellationToken = default)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["push", remoteName, "--delete", $"refs/tags/{tagName}"],
            input: null,
            credentialKey: credentialKey, cancellationToken: cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException(result.StandardError);
        }
    }
}
