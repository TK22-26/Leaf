using System.Collections.Concurrent;
using System.Diagnostics;
using Leaf.Models;
using Leaf.Utils;

namespace Leaf.Services.PullRequests;

/// <summary>
/// Routes pull request calls to the correct provider based on the repository's remote URL.
/// </summary>
public class PullRequestService : IPullRequestService
{
    private readonly CredentialService _credentialService;
    private readonly IGitService _gitService;

    private readonly ConcurrentDictionary<string, ResolvedRepo?> _resolvedRepos = new(StringComparer.OrdinalIgnoreCase);
    private GitHubPullRequestProvider? _gitHubProvider;

    public PullRequestService(CredentialService credentialService, IGitService gitService)
    {
        _credentialService = credentialService;
        _gitService = gitService;
    }

    public async Task<List<PullRequestInfo>> ListPullRequestsAsync(string repoPath, PullRequestState filter = PullRequestState.Open)
    {
        var (provider, owner, repo) = await ResolveOrThrowAsync(repoPath);
        return await provider.ListPullRequestsAsync(owner, repo, filter);
    }

    public async Task<PullRequestDetails?> GetPullRequestAsync(string repoPath, int number)
    {
        var (provider, owner, repo) = await ResolveOrThrowAsync(repoPath);
        return await provider.GetPullRequestAsync(owner, repo, number);
    }

    public async Task<PullRequestInfo> CreatePullRequestAsync(string repoPath, string title, string body, string sourceBranch, string targetBranch, bool isDraft = false)
    {
        var (provider, owner, repo) = await ResolveOrThrowAsync(repoPath);
        return await provider.CreatePullRequestAsync(owner, repo, title, body, sourceBranch, targetBranch, isDraft);
    }

    public async Task<PullRequestInfo> UpdatePullRequestAsync(string repoPath, int number, string? title = null, string? body = null)
    {
        var (provider, owner, repo) = await ResolveOrThrowAsync(repoPath);
        return await provider.UpdatePullRequestAsync(owner, repo, number, title, body);
    }

    public async Task<PullRequestMergeResult> MergePullRequestAsync(string repoPath, int number, MergeMethod method = MergeMethod.Merge, string? commitTitle = null)
    {
        var (provider, owner, repo) = await ResolveOrThrowAsync(repoPath);
        return await provider.MergePullRequestAsync(owner, repo, number, method, commitTitle);
    }

    public async Task ClosePullRequestAsync(string repoPath, int number)
    {
        var (provider, owner, repo) = await ResolveOrThrowAsync(repoPath);
        await provider.ClosePullRequestAsync(owner, repo, number);
    }

    public async Task<List<PullRequestFileInfo>> GetPullRequestFilesAsync(string repoPath, int number)
    {
        var (provider, owner, repo) = await ResolveOrThrowAsync(repoPath);
        return await provider.GetPullRequestFilesAsync(owner, repo, number);
    }

    public async Task<List<ReviewerInfo>> SearchReviewersAsync(string repoPath, string searchTerm)
    {
        var (provider, owner, repo) = await ResolveOrThrowAsync(repoPath);
        return await provider.SearchReviewersAsync(owner, repo, searchTerm);
    }

    public async Task RequestReviewersAsync(string repoPath, int number, IEnumerable<ReviewerInfo> reviewers)
    {
        var (provider, owner, repo) = await ResolveOrThrowAsync(repoPath);
        await provider.RequestReviewersAsync(owner, repo, number, reviewers);
    }

    public async Task<List<PullRequestStatusCheckInfo>> GetStatusChecksAsync(string repoPath, int number)
    {
        var (provider, owner, repo) = await ResolveOrThrowAsync(repoPath);
        return await provider.GetStatusChecksAsync(owner, repo, number);
    }

    public async Task<PullRequestInfo?> FindPullRequestForCommitAsync(string repoPath, string sha)
    {
        var (provider, owner, repo) = await ResolveOrThrowAsync(repoPath);
        return await provider.FindPullRequestForCommitAsync(owner, repo, sha);
    }

    public PullRequestCapabilities GetCapabilities(string repoPath)
    {
        var normalized = NormalizePath(repoPath);
        if (_resolvedRepos.TryGetValue(normalized, out var resolved) && resolved != null)
        {
            var provider = GetProviderForType(resolved.Provider);
            return provider?.Capabilities ?? PullRequestCapabilities.None;
        }
        return PullRequestCapabilities.None;
    }

    public bool IsSupported(string repoPath)
    {
        var normalized = NormalizePath(repoPath);
        return _resolvedRepos.TryGetValue(normalized, out var resolved) && resolved != null;
    }

    public async Task TryResolveAsync(string repoPath)
    {
        await ResolveAsync(repoPath);
    }

    // --- Private infrastructure ---

    private record ResolvedRepo(CredentialProvider Provider, string Owner, string RepoName);

    private async Task<(IPullRequestProvider Provider, string Owner, string Repo)> ResolveOrThrowAsync(string repoPath)
    {
        var resolved = await ResolveAsync(repoPath)
            ?? throw new InvalidOperationException($"No supported pull request provider found for repository: {repoPath}");

        var provider = GetProviderForType(resolved.Provider)
            ?? throw new InvalidOperationException($"Provider {resolved.Provider} is not yet implemented.");

        return (provider, resolved.Owner, resolved.RepoName);
    }

    private async Task<ResolvedRepo?> ResolveAsync(string repoPath)
    {
        var normalized = NormalizePath(repoPath);

        if (_resolvedRepos.TryGetValue(normalized, out var cached))
            return cached;

        try
        {
            var remotes = await _gitService.GetRemotesAsync(repoPath);
            var defaultRemote = remotes.FirstOrDefault(r => r.Name == "origin") ?? remotes.FirstOrDefault();

            if (defaultRemote == null)
            {
                _resolvedRepos.TryAdd(normalized, null);
                return null;
            }

            if (!CredentialHelper.TryGetProviderAndOrg(defaultRemote.Url, out var providerType, out var org) ||
                providerType == CredentialProvider.Unknown ||
                string.IsNullOrEmpty(org))
            {
                _resolvedRepos.TryAdd(normalized, null);
                return null;
            }

            // Extract repo name from URL path
            var repoName = ExtractRepoName(defaultRemote.Url, providerType);
            if (string.IsNullOrEmpty(repoName))
            {
                _resolvedRepos.TryAdd(normalized, null);
                return null;
            }

            // Verify PAT exists
            var pat = _credentialService.GetPat($"{providerType}:{org}");
            if (string.IsNullOrEmpty(pat))
            {
                Debug.WriteLine($"[PR] No PAT configured for {providerType}:{org}");
                _resolvedRepos.TryAdd(normalized, null);
                return null;
            }

            var resolved = new ResolvedRepo(providerType, org, repoName);
            _resolvedRepos.TryAdd(normalized, resolved);
            return resolved;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PR] Failed to resolve provider for {repoPath}: {ex.Message}");
            _resolvedRepos.TryAdd(normalized, null);
            return null;
        }
    }

    private IPullRequestProvider? GetProviderForType(CredentialProvider provider)
    {
        return provider switch
        {
            CredentialProvider.GitHub => _gitHubProvider ??= new GitHubPullRequestProvider(_credentialService),
            // Azure DevOps provider will be added in E1.1.6
            _ => null
        };
    }

    private static string? ExtractRepoName(string remoteUrl, CredentialProvider provider)
    {
        // HTTPS: https://github.com/owner/repo.git or https://dev.azure.com/org/project/_git/repo
        if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri))
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/');

            if (provider == CredentialProvider.GitHub && segments.Length >= 2)
                return StripGitSuffix(segments[1]);

            if (provider == CredentialProvider.AzureDevOps)
            {
                // dev.azure.com/org/project/_git/repo -> segments[3]
                var gitIndex = Array.IndexOf(segments, "_git");
                if (gitIndex >= 0 && gitIndex + 1 < segments.Length)
                    return StripGitSuffix(segments[gitIndex + 1]);
            }
        }

        // SSH: git@github.com:owner/repo.git
        var colonIndex = remoteUrl.IndexOf(':');
        if (colonIndex > 0)
        {
            var path = remoteUrl[(colonIndex + 1)..];
            var pathSegments = path.Split('/');

            if (provider == CredentialProvider.GitHub && pathSegments.Length >= 2)
                return StripGitSuffix(pathSegments[1]);

            if (provider == CredentialProvider.AzureDevOps)
            {
                // v3/org/project/repo -> last segment
                if (pathSegments.Length >= 4)
                    return StripGitSuffix(pathSegments[^1]);
            }
        }

        return null;
    }

    private static string StripGitSuffix(string name)
    {
        return name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
    }

    private static string NormalizePath(string path)
    {
        return System.IO.Path.GetFullPath(path);
    }
}
