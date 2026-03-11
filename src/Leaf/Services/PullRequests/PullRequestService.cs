using System.Collections.Concurrent;
using System.Text;
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
    private AzureDevOpsPullRequestProvider? _azureDevOpsProvider;

    public PullRequestService(CredentialService credentialService, IGitService gitService)
    {
        _credentialService = credentialService;
        _gitService = gitService;
    }

    public async Task<List<PullRequestInfo>> ListPullRequestsAsync(string repoPath, PullRequestState filter = PullRequestState.Open)
    {
        var (provider, owner, project, repo) = await ResolveOrThrowAsync(repoPath);
        return await provider.ListPullRequestsAsync(owner, project, repo, filter);
    }

    public async Task<PullRequestDetails?> GetPullRequestAsync(string repoPath, int number)
    {
        var (provider, owner, project, repo) = await ResolveOrThrowAsync(repoPath);
        var details = await provider.GetPullRequestAsync(owner, project, repo, number);
        if (details == null)
            return null;

        EnsureCommitFallback(details);
        await EnsureLocalCommitFallbackAsync(repoPath, details);
        await EnrichFileDiffsAsync(repoPath, details);
        return details;
    }

    public async Task<PullRequestInfo> CreatePullRequestAsync(string repoPath, string title, string body, string sourceBranch, string targetBranch, bool isDraft = false)
    {
        var (provider, owner, project, repo) = await ResolveOrThrowAsync(repoPath);
        return await provider.CreatePullRequestAsync(owner, project, repo, title, body, sourceBranch, targetBranch, isDraft);
    }

    public async Task<PullRequestInfo> UpdatePullRequestAsync(string repoPath, int number, string? title = null, string? body = null)
    {
        var (provider, owner, project, repo) = await ResolveOrThrowAsync(repoPath);
        return await provider.UpdatePullRequestAsync(owner, project, repo, number, title, body);
    }

    public async Task<PullRequestMergeResult> MergePullRequestAsync(string repoPath, int number, MergeMethod method = MergeMethod.Merge, string? commitTitle = null)
    {
        var (provider, owner, project, repo) = await ResolveOrThrowAsync(repoPath);
        return await provider.MergePullRequestAsync(owner, project, repo, number, method, commitTitle);
    }

    public async Task ClosePullRequestAsync(string repoPath, int number)
    {
        var (provider, owner, project, repo) = await ResolveOrThrowAsync(repoPath);
        await provider.ClosePullRequestAsync(owner, project, repo, number);
    }

    public async Task SubmitReviewAsync(string repoPath, int number, PullRequestReviewState state, string? body = null)
    {
        var (provider, owner, project, repo) = await ResolveOrThrowAsync(repoPath);
        await provider.SubmitReviewAsync(owner, project, repo, number, state, body);
    }

    public async Task AddCommentAsync(string repoPath, int number, string body)
    {
        var (provider, owner, project, repo) = await ResolveOrThrowAsync(repoPath);
        await provider.AddCommentAsync(owner, project, repo, number, body);
    }

    public async Task<List<PullRequestFileInfo>> GetPullRequestFilesAsync(string repoPath, int number)
    {
        var (provider, owner, project, repo) = await ResolveOrThrowAsync(repoPath);
        return await provider.GetPullRequestFilesAsync(owner, project, repo, number);
    }

    public async Task<List<ReviewerInfo>> SearchReviewersAsync(string repoPath, string searchTerm)
    {
        var (provider, owner, project, repo) = await ResolveOrThrowAsync(repoPath);
        return await provider.SearchReviewersAsync(owner, project, repo, searchTerm);
    }

    public async Task RequestReviewersAsync(string repoPath, int number, IEnumerable<ReviewerInfo> reviewers)
    {
        var (provider, owner, project, repo) = await ResolveOrThrowAsync(repoPath);
        await provider.RequestReviewersAsync(owner, project, repo, number, reviewers);
    }

    public async Task<List<ReviewerInfo>> SearchAssigneesAsync(string repoPath, string searchTerm)
    {
        var (provider, owner, project, repo) = await ResolveOrThrowAsync(repoPath);
        return await provider.SearchAssigneesAsync(owner, project, repo, searchTerm);
    }

    public async Task AddAssigneesAsync(string repoPath, int number, IEnumerable<string> assignees)
    {
        var normalizedAssignees = assignees
            .Where(assignee => !string.IsNullOrWhiteSpace(assignee))
            .Select(assignee => assignee.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedAssignees.Count == 0)
            return;

        var (provider, owner, project, repo) = await ResolveOrThrowAsync(repoPath);
        await provider.AddAssigneesAsync(owner, project, repo, number, normalizedAssignees);
    }

    public async Task RemoveAssigneeAsync(string repoPath, int number, string assignee)
    {
        if (string.IsNullOrWhiteSpace(assignee))
            return;

        var normalizedAssignee = assignee.Trim();
        if (normalizedAssignee.Length == 0)
            return;

        var (provider, owner, project, repo) = await ResolveOrThrowAsync(repoPath);
        await provider.RemoveAssigneeAsync(owner, project, repo, number, normalizedAssignee);
    }

    public async Task AddLabelsAsync(string repoPath, int number, IEnumerable<string> labels)
    {
        var normalizedLabels = labels
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedLabels.Count == 0)
            return;

        var (provider, owner, project, repo) = await ResolveOrThrowAsync(repoPath);
        await provider.AddLabelsAsync(owner, project, repo, number, normalizedLabels);
    }

    public async Task RemoveLabelAsync(string repoPath, int number, string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return;

        var normalizedLabel = label.Trim();
        if (normalizedLabel.Length == 0)
            return;

        var (provider, owner, project, repo) = await ResolveOrThrowAsync(repoPath);
        await provider.RemoveLabelAsync(owner, project, repo, number, normalizedLabel);
    }

    public async Task<List<PullRequestStatusCheckInfo>> GetStatusChecksAsync(string repoPath, int number)
    {
        var (provider, owner, project, repo) = await ResolveOrThrowAsync(repoPath);
        return await provider.GetStatusChecksAsync(owner, project, repo, number);
    }

    public async Task<PullRequestInfo?> FindPullRequestForCommitAsync(string repoPath, string sha)
    {
        var (provider, owner, project, repo) = await ResolveOrThrowAsync(repoPath);
        return await provider.FindPullRequestForCommitAsync(owner, project, repo, sha);
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

    public string? GetCreatePullRequestUrl(string repoPath, string sourceBranch)
    {
        var normalized = NormalizePath(repoPath);
        if (!_resolvedRepos.TryGetValue(normalized, out var resolved) || resolved == null)
            return null;

        var encodedBranch = Uri.EscapeDataString(sourceBranch);

        return resolved.Provider switch
        {
            CredentialProvider.GitHub =>
                $"https://github.com/{resolved.Owner}/{resolved.RepoName}/compare/{encodedBranch}?expand=1",
            CredentialProvider.AzureDevOps =>
                resolved.ProjectName == null
                    ? null
                    : $"https://dev.azure.com/{resolved.Owner}/{resolved.ProjectName}/_git/{resolved.RepoName}/pullrequestcreate?sourceRef={Uri.EscapeDataString($"refs/heads/{sourceBranch}")}",
            _ => null
        };
    }

    public async Task TryResolveAsync(string repoPath)
    {
        await ResolveAsync(repoPath);
    }

    // --- Private infrastructure ---

    private record ResolvedRepo(CredentialProvider Provider, string Owner, string? ProjectName, string RepoName);

    private async Task<(IPullRequestProvider Provider, string Owner, string? Project, string Repo)> ResolveOrThrowAsync(string repoPath)
    {
        var resolved = await ResolveAsync(repoPath)
            ?? throw new InvalidOperationException($"No supported pull request provider found for repository: {repoPath}");

        var provider = GetProviderForType(resolved.Provider)
            ?? throw new InvalidOperationException($"Provider {resolved.Provider} is not yet implemented.");

        return (provider, resolved.Owner, resolved.ProjectName, resolved.RepoName);
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
            var projectName = ExtractProjectName(defaultRemote.Url, providerType);
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
                Log.Warn("PR", $"No PAT configured for {providerType}:{org}");
                _resolvedRepos.TryAdd(normalized, null);
                return null;
            }

            var resolved = new ResolvedRepo(providerType, org, projectName, repoName);
            Log.Info("PR", $"Resolved {providerType} provider for {repoPath}: owner={org}, project={projectName ?? "<none>"}, repo={repoName}");
            _resolvedRepos.TryAdd(normalized, resolved);
            return resolved;
        }
        catch (Exception ex)
        {
            Log.Error("PR", $"Failed to resolve provider for {repoPath}: {ex.Message}");
            _resolvedRepos.TryAdd(normalized, null);
            return null;
        }
    }

    private IPullRequestProvider? GetProviderForType(CredentialProvider provider)
    {
        return provider switch
        {
            CredentialProvider.GitHub => _gitHubProvider ??= new GitHubPullRequestProvider(_credentialService),
            CredentialProvider.AzureDevOps => _azureDevOpsProvider ??= new AzureDevOpsPullRequestProvider(_credentialService),
            _ => null
        };
    }

    private static string? ExtractProjectName(string remoteUrl, CredentialProvider provider)
    {
        if (provider != CredentialProvider.AzureDevOps)
            return null;

        if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri))
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/');

            if (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase) && segments.Length >= 2)
                return Uri.UnescapeDataString(segments[1]);

            if (uri.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase) && segments.Length >= 1)
                return Uri.UnescapeDataString(segments[0]);
        }

        var colonIndex = remoteUrl.IndexOf(':');
        if (colonIndex > 0)
        {
            var path = remoteUrl[(colonIndex + 1)..];
            var pathSegments = path.Split('/');

            if (pathSegments.Length >= 3 &&
                string.Equals(pathSegments[0], "v3", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pathSegments[2]);
            }
        }

        return null;
    }

    private static string? ExtractRepoName(string remoteUrl, CredentialProvider provider)
    {
        // HTTPS: https://github.com/owner/repo.git or https://dev.azure.com/org/project/_git/repo
        if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri))
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/');

            if (provider == CredentialProvider.GitHub && segments.Length >= 2)
                return DecodeRepoSegment(segments[1]);

            if (provider == CredentialProvider.AzureDevOps)
            {
                // dev.azure.com/org/project/_git/repo -> segments[3]
                var gitIndex = Array.IndexOf(segments, "_git");
                if (gitIndex >= 0 && gitIndex + 1 < segments.Length)
                    return DecodeRepoSegment(segments[gitIndex + 1]);
            }
        }

        // SSH: git@github.com:owner/repo.git
        var colonIndex = remoteUrl.IndexOf(':');
        if (colonIndex > 0)
        {
            var path = remoteUrl[(colonIndex + 1)..];
            var pathSegments = path.Split('/');

            if (provider == CredentialProvider.GitHub && pathSegments.Length >= 2)
                return DecodeRepoSegment(pathSegments[1]);

            if (provider == CredentialProvider.AzureDevOps)
            {
                // v3/org/project/repo -> last segment
                if (pathSegments.Length >= 4)
                    return DecodeRepoSegment(pathSegments[^1]);
            }
        }

        return null;
    }

    private static string DecodeRepoSegment(string name)
        => Uri.UnescapeDataString(StripGitSuffix(name));

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

    private static void EnsureCommitFallback(PullRequestDetails details)
    {
        if (details.Commits.Count > 0 || details.Updates.Count == 0)
            return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var commit in details.Updates
                     .SelectMany(update => update.Commits)
                     .OrderByDescending(commit => commit.Timestamp))
        {
            var key = !string.IsNullOrWhiteSpace(commit.Sha)
                ? commit.Sha
                : $"{commit.Timestamp:O}:{commit.Message}";

            if (seen.Add(key))
            {
                details.Commits.Add(commit);
            }
        }

        Log.Info("PR", $"Applied commit fallback from updates: commits={details.Commits.Count}");
    }

    private async Task EnsureLocalCommitFallbackAsync(string repoPath, PullRequestDetails details)
    {
        if (details.Commits.Count > 0 ||
            string.IsNullOrWhiteSpace(details.Summary.TargetBranch) ||
            string.IsNullOrWhiteSpace(details.Summary.SourceBranch))
        {
            return;
        }

        try
        {
            var commits = await _gitService.GetCommitsBetweenAsync(
                repoPath,
                details.Summary.TargetBranch,
                details.Summary.SourceBranch);

            foreach (var commit in commits)
            {
                details.Commits.Add(new PullRequestCommitInfo
                {
                    Sha = commit.Sha,
                    Message = !string.IsNullOrWhiteSpace(commit.MessageShort) ? commit.MessageShort : commit.Message,
                    Description = string.IsNullOrWhiteSpace(commit.Description) ? null : commit.Description,
                    AuthorDisplayName = commit.Author,
                    AuthorIdentity = commit.AuthorEmail,
                    Timestamp = commit.Date
                });
            }

            if (details.Commits.Count > 0)
                Log.Info("PR", $"Applied local commit fallback from refs: commits={details.Commits.Count}");
        }
        catch (Exception ex)
        {
            Log.Warn("PR", $"Failed to build local commit fallback for {repoPath}: {ex.Message}");
        }
    }

    private async Task EnrichFileDiffsAsync(string repoPath, PullRequestDetails details)
    {
        var shouldBuildFromDiff = details.Files.Count == 0;
        var needsDiffEnrichment = shouldBuildFromDiff || details.Files.Any(file =>
            string.IsNullOrWhiteSpace(file.PatchContent) || (file.Additions == 0 && file.Deletions == 0));

        if (!needsDiffEnrichment ||
            string.IsNullOrWhiteSpace(details.Summary.TargetBranch) ||
            string.IsNullOrWhiteSpace(details.Summary.SourceBranch))
        {
            UpdateSummaryTotals(details);
            return;
        }

        try
        {
            var diffText = await _gitService.GetRefToRefDiffAsync(
                repoPath,
                details.Summary.TargetBranch,
                details.Summary.SourceBranch);

            var diffByPath = ParseUnifiedDiff(diffText)
                .ToDictionary(patch => NormalizeDiffPath(patch.Path), StringComparer.OrdinalIgnoreCase);

            if (details.Files.Count == 0)
            {
                details.Files.AddRange(diffByPath.Values.Select(patch => new PullRequestFileInfo
                {
                    Path = patch.Path,
                    Status = patch.Status,
                    Additions = patch.Additions,
                    Deletions = patch.Deletions,
                    PatchContent = patch.Text
                }));

                if (details.Files.Count > 0)
                    Log.Info("PR", $"Applied local file fallback from refs: files={details.Files.Count}");
            }

            foreach (var file in details.Files)
            {
                if (diffByPath.TryGetValue(NormalizeDiffPath(file.Path), out var patch))
                {
                    file.PatchContent ??= patch.Text;

                    if (file.Additions == 0 && file.Deletions == 0)
                    {
                        file.Additions = patch.Additions;
                        file.Deletions = patch.Deletions;
                    }
                }
            }

            UpdateSummaryTotals(details);
        }
        catch (Exception ex)
        {
            Log.Warn("PR", $"Failed to enrich PR file diffs for {repoPath}: {ex.Message}");
        }
    }

    private static void UpdateSummaryTotals(PullRequestDetails details)
    {
        details.Summary.ChangedFilesCount = details.Files.Count;
        details.Summary.Additions = details.Files.Sum(file => file.Additions);
        details.Summary.Deletions = details.Files.Sum(file => file.Deletions);
    }

    private static List<ParsedDiffPatch> ParseUnifiedDiff(string diffText)
    {
        var patches = new List<ParsedDiffPatch>();
        if (string.IsNullOrWhiteSpace(diffText))
            return patches;

        var lines = diffText.Replace("\r\n", "\n").Split('\n');
        var buffer = new StringBuilder();
        string? oldPath = null;
        string? newPath = null;
        var additions = 0;
        var deletions = 0;
        var status = PullRequestFileStatus.Modified;
        var hasActivePatch = false;

        void FlushPatch()
        {
            if (!hasActivePatch)
                return;

            var chosenPath = ChooseDiffPath(oldPath, newPath);
            if (!string.IsNullOrWhiteSpace(chosenPath))
            {
                patches.Add(new ParsedDiffPatch(chosenPath!, buffer.ToString().TrimEnd('\n'), additions, deletions, status));
            }

            buffer.Clear();
            oldPath = null;
            newPath = null;
            additions = 0;
            deletions = 0;
            status = PullRequestFileStatus.Modified;
            hasActivePatch = false;
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                FlushPatch();
                hasActivePatch = true;
            }

            if (!hasActivePatch)
                continue;

            buffer.AppendLine(line);

            if (line.StartsWith("--- ", StringComparison.Ordinal))
            {
                oldPath = ParsePatchPath(line[4..]);
            }
            else if (line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                newPath = ParsePatchPath(line[4..]);
            }
            else if (line.StartsWith("rename from ", StringComparison.Ordinal))
            {
                oldPath = NormalizeDiffPath(line["rename from ".Length..]);
                status = PullRequestFileStatus.Renamed;
            }
            else if (line.StartsWith("rename to ", StringComparison.Ordinal))
            {
                newPath = NormalizeDiffPath(line["rename to ".Length..]);
                status = PullRequestFileStatus.Renamed;
            }
            else if (line.StartsWith("new file mode ", StringComparison.Ordinal))
            {
                status = PullRequestFileStatus.Added;
            }
            else if (line.StartsWith("deleted file mode ", StringComparison.Ordinal))
            {
                status = PullRequestFileStatus.Deleted;
            }
            else if (line.StartsWith("+", StringComparison.Ordinal) && !line.StartsWith("+++", StringComparison.Ordinal))
            {
                additions++;
            }
            else if (line.StartsWith("-", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal))
            {
                deletions++;
            }
        }

        FlushPatch();
        return patches;
    }

    private static string? ChooseDiffPath(string? oldPath, string? newPath)
    {
        if (!string.IsNullOrWhiteSpace(newPath) && !string.Equals(newPath, "/dev/null", StringComparison.OrdinalIgnoreCase))
            return newPath;

        if (!string.IsNullOrWhiteSpace(oldPath) && !string.Equals(oldPath, "/dev/null", StringComparison.OrdinalIgnoreCase))
            return oldPath;

        return null;
    }

    private static string ParsePatchPath(string rawPath)
    {
        var path = rawPath.Trim();

        if (path.StartsWith("\"", StringComparison.Ordinal) && path.EndsWith("\"", StringComparison.Ordinal) && path.Length >= 2)
            path = path[1..^1];

        if (string.Equals(path, "/dev/null", StringComparison.Ordinal) || string.Equals(path, "dev/null", StringComparison.Ordinal))
            return "/dev/null";

        if (path.StartsWith("a/", StringComparison.Ordinal) || path.StartsWith("b/", StringComparison.Ordinal))
            path = path[2..];

        return NormalizeDiffPath(path);
    }

    private static string NormalizeDiffPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (string.Equals(normalized, "/dev/null", StringComparison.Ordinal) || string.Equals(normalized, "dev/null", StringComparison.Ordinal))
            return "/dev/null";

        return normalized.TrimStart('/');
    }

    private sealed record ParsedDiffPatch(string Path, string Text, int Additions, int Deletions, PullRequestFileStatus Status);
}
