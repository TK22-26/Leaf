using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.Services.PullRequests;

/// <summary>
/// GitHub REST API adapter for pull request operations.
/// </summary>
internal class GitHubPullRequestProvider : IPullRequestProvider
{
    private readonly HttpClient _httpClient;
    private readonly CredentialService _credentialService;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const string ApiVersion = "2022-11-28";
    private const int PerPage = 100;
    private const int MaxPages = 50;

    // Reviewer directory caches (per owner/repo key)
    private readonly Dictionary<string, (List<ReviewerInfo> Collaborators, List<ReviewerInfo> Teams, DateTime CachedAt)> _reviewerCache = [];
    private static readonly TimeSpan ReviewerCacheTtl = TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, string> _authenticatedUserCache = new(StringComparer.OrdinalIgnoreCase);

    public PullRequestCapabilities Capabilities =>
        PullRequestCapabilities.DraftPullRequests |
        PullRequestCapabilities.SquashMerge |
        PullRequestCapabilities.RebaseMerge |
        PullRequestCapabilities.MergeCommit |
        PullRequestCapabilities.StatusChecks |
        PullRequestCapabilities.Reviews |
        PullRequestCapabilities.TeamReviewers |
        PullRequestCapabilities.Labels |
        PullRequestCapabilities.Assignees;

    public GitHubPullRequestProvider(CredentialService credentialService)
    {
        _credentialService = credentialService;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Leaf", "1.0"));
    }

    public async Task<List<PullRequestInfo>> ListPullRequestsAsync(string owner, string? project, string repo, PullRequestState filter)
    {
        var stateParam = filter switch
        {
            PullRequestState.Open => "open",
            PullRequestState.Closed => "closed",
            PullRequestState.All => "all",
            // Merged and Draft are filtered client-side from the appropriate API state
            PullRequestState.Merged => "closed",
            PullRequestState.Draft => "open",
            _ => "open"
        };

        var allPrs = new List<PullRequestInfo>();
        var page = 1;

        while (true)
        {
            var url = $"https://api.github.com/repos/{owner}/{repo}/pulls?state={stateParam}&per_page={PerPage}&page={page}&sort=updated&direction=desc";
            var dtos = await GetAsync<List<GitHubPullRequestDto>>(url, owner);

            if (dtos == null || dtos.Count == 0)
                break;

            foreach (var dto in dtos)
            {
                var pr = MapPullRequestInfo(dto);

                // Client-side filtering for Merged and Draft
                if (filter == PullRequestState.Merged && pr.State != PullRequestState.Merged)
                    continue;
                if (filter == PullRequestState.Draft && !pr.IsDraft)
                    continue;

                allPrs.Add(pr);
            }

            if (dtos.Count < PerPage)
                break;

            page++;
            if (page > MaxPages)
                break;
        }

        return allPrs;
    }

    public async Task<PullRequestDetails?> GetPullRequestAsync(string owner, string? project, string repo, int number)
    {
        var dto = await GetAsync<GitHubPullRequestDto>($"https://api.github.com/repos/{owner}/{repo}/pulls/{number}", owner);
        if (dto == null)
            return null;

        var summary = MapPullRequestInfo(dto);

        // Fetch detail sections in parallel
        var filesTask = GetPullRequestFilesAsync(owner, project, repo, number);
        var reviewsTask = GetReviewsInternalAsync(owner, repo, number);
        var commentsTask = GetCommentsInternalAsync(owner, repo, number);
        var checksTask = GetStatusChecksAsync(owner, project, repo, number);
        var commitsTask = GetCommitsInternalAsync(owner, repo, number);
        var issueTask = GetIssueInternalAsync(owner, repo, number);

        await Task.WhenAll(filesTask, reviewsTask, commentsTask, checksTask, commitsTask, issueTask);
        var issue = await issueTask;

        // Map requested reviewers from the PR DTO
        var requestedReviewers = new List<ReviewerInfo>();
        if (dto.RequestedReviewers != null)
        {
            foreach (var user in dto.RequestedReviewers)
            {
                requestedReviewers.Add(new ReviewerInfo
                {
                    Identifier = user.Login ?? string.Empty,
                    DisplayName = user.Login ?? string.Empty,
                    AvatarUrl = user.AvatarUrl,
                    Kind = ReviewerKind.User
                });
            }
        }
        if (dto.RequestedTeams != null)
        {
            foreach (var team in dto.RequestedTeams)
            {
                requestedReviewers.Add(new ReviewerInfo
                {
                    Identifier = team.Slug ?? string.Empty,
                    DisplayName = team.Name ?? string.Empty,
                    SecondaryText = team.Slug,
                    Kind = ReviewerKind.Team
                });
            }
        }

        return new PullRequestDetails
        {
            Summary = summary,
            Body = dto.Body ?? string.Empty,
            Files = await filesTask,
            Reviews = await reviewsTask,
            Comments = await commentsTask,
            StatusChecks = await checksTask,
            RequestedReviewers = requestedReviewers,
            Assignees = issue?.Assignees?.Where(user => !string.IsNullOrWhiteSpace(user.Login))
                .Select(user => new ReviewerInfo
                {
                    Identifier = user.Login ?? string.Empty,
                    DisplayName = user.Login ?? string.Empty,
                    AvatarUrl = user.AvatarUrl,
                    Kind = ReviewerKind.User
                })
                .ToList() ?? [],
            Commits = await commitsTask,
            Labels = issue?.Labels?.Where(l => !string.IsNullOrWhiteSpace(l.Name))
                .Select(l => new PullRequestLabelInfo { Name = l.Name! })
                .ToList() ?? [],
            Updates = [],
            WorkItems = [],
            IsMergeable = dto.Mergeable ?? false,
            HasConflicts = string.Equals(dto.MergeableState, "dirty", StringComparison.OrdinalIgnoreCase),
            MergeStatusMessage = dto.MergeableState,
            HeadSha = dto.Head?.Sha ?? string.Empty,
            BaseSha = dto.Base?.Sha ?? string.Empty
        };
    }

    public async Task<PullRequestInfo> CreatePullRequestAsync(string owner, string? project, string repo, string title, string body, string sourceBranch, string targetBranch, bool isDraft)
    {
        var payload = new { title, body, head = sourceBranch, @base = targetBranch, draft = isDraft };
        var dto = await PostAsync<GitHubPullRequestDto>($"https://api.github.com/repos/{owner}/{repo}/pulls", owner, payload);
        return MapPullRequestInfo(dto);
    }

    public async Task<PullRequestInfo> UpdatePullRequestAsync(string owner, string? project, string repo, int number, string? title, string? body)
    {
        var payload = new Dictionary<string, string>();
        if (title != null) payload["title"] = title;
        if (body != null) payload["body"] = body;

        var dto = await PatchAsync<GitHubPullRequestDto>($"https://api.github.com/repos/{owner}/{repo}/pulls/{number}", owner, payload);
        return MapPullRequestInfo(dto);
    }

    public async Task<PullRequestMergeResult> MergePullRequestAsync(string owner, string? project, string repo, int number, MergeMethod method, string? commitTitle)
    {
        var mergeMethod = method switch
        {
            MergeMethod.Squash => "squash",
            MergeMethod.Rebase => "rebase",
            _ => "merge"
        };

        var payload = new Dictionary<string, string> { ["merge_method"] = mergeMethod };
        if (commitTitle != null) payload["commit_title"] = commitTitle;

        try
        {
            var result = await PutAsync<GitHubMergeResultDto>($"https://api.github.com/repos/{owner}/{repo}/pulls/{number}/merge", owner, payload);
            return new PullRequestMergeResult
            {
                Success = result.Merged,
                MergedSha = result.Sha,
                ErrorMessage = result.Message
            };
        }
        catch (HttpRequestException ex)
        {
            return new PullRequestMergeResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task ClosePullRequestAsync(string owner, string? project, string repo, int number)
    {
        await PatchAsync<GitHubPullRequestDto>(
            $"https://api.github.com/repos/{owner}/{repo}/pulls/{number}",
            owner,
            new { state = "closed" });
    }

    public async Task SubmitReviewAsync(string owner, string? project, string repo, int number, PullRequestReviewState state, string? body)
    {
        var eventName = state switch
        {
            PullRequestReviewState.Approved => "APPROVE",
            PullRequestReviewState.ChangesRequested => "REQUEST_CHANGES",
            _ => "COMMENT"
        };

        await PostAsync<object>(
            $"https://api.github.com/repos/{owner}/{repo}/pulls/{number}/reviews",
            owner,
            new
            {
                body = body ?? string.Empty,
                @event = eventName
            });
    }

    public async Task AddCommentAsync(string owner, string? project, string repo, int number, string body)
    {
        await PostAsync<object>(
            $"https://api.github.com/repos/{owner}/{repo}/issues/{number}/comments",
            owner,
            new { body });
    }

    public async Task<List<PullRequestFileInfo>> GetPullRequestFilesAsync(string owner, string? project, string repo, int number)
    {
        var allFiles = new List<PullRequestFileInfo>();
        var page = 1;

        while (true)
        {
            var dtos = await GetAsync<List<GitHubFileDto>>(
                $"https://api.github.com/repos/{owner}/{repo}/pulls/{number}/files?per_page={PerPage}&page={page}", owner);

            if (dtos == null || dtos.Count == 0)
                break;

            foreach (var dto in dtos)
            {
                allFiles.Add(new PullRequestFileInfo
                {
                    Path = dto.Filename ?? string.Empty,
                    Status = MapFileStatus(dto.Status),
                    Additions = dto.Additions,
                    Deletions = dto.Deletions,
                    PatchContent = dto.Patch
                });
            }

            if (dtos.Count < PerPage)
                break;

            page++;
            if (page > MaxPages)
                break;
        }

        return allFiles;
    }

    public async Task<List<ReviewerInfo>> SearchReviewersAsync(string owner, string? project, string repo, string searchTerm)
    {
        var cacheKey = $"{owner}/{repo}";
        var now = DateTime.UtcNow;

        // Check cache
        if (_reviewerCache.TryGetValue(cacheKey, out var cached) && now - cached.CachedAt < ReviewerCacheTtl)
        {
            return FilterReviewers(cached.Collaborators, cached.Teams, searchTerm);
        }

        // Fetch collaborators and teams in parallel
        var collaboratorsTask = FetchCollaboratorsAsync(owner, repo);
        var teamsTask = FetchTeamsAsync(owner, repo);

        await Task.WhenAll(collaboratorsTask, teamsTask);

        var collaborators = await collaboratorsTask;
        var teams = await teamsTask;

        _reviewerCache[cacheKey] = (collaborators, teams, now);

        return FilterReviewers(collaborators, teams, searchTerm);
    }

    public async Task RequestReviewersAsync(string owner, string? project, string repo, int number, IEnumerable<ReviewerInfo> reviewers)
    {
        var userReviewers = new List<string>();
        var teamReviewers = new List<string>();
        var assignees = new List<string>();
        var currentUserLogin = await GetAuthenticatedUserLoginAsync(owner);

        foreach (var reviewer in reviewers)
        {
            if (reviewer.Kind == ReviewerKind.Team)
            {
                teamReviewers.Add(reviewer.Identifier);
            }
            else if (!string.IsNullOrWhiteSpace(currentUserLogin) &&
                     string.Equals(reviewer.Identifier, currentUserLogin, StringComparison.OrdinalIgnoreCase))
            {
                assignees.Add(reviewer.Identifier);
            }
            else
            {
                userReviewers.Add(reviewer.Identifier);
            }
        }

        if (userReviewers.Count > 0 || teamReviewers.Count > 0)
        {
            var payload = new { reviewers = userReviewers, team_reviewers = teamReviewers };
            await PostAsync<object>($"https://api.github.com/repos/{owner}/{repo}/pulls/{number}/requested_reviewers", owner, payload);
        }

        if (assignees.Count > 0)
        {
            var payload = new { assignees };
            await PostAsync<object>($"https://api.github.com/repos/{owner}/{repo}/issues/{number}/assignees", owner, payload);
            Log.Info("PR", $"GitHub self-review fallback: assigned {string.Join(", ", assignees)} to PR #{number}");
        }
    }

    public async Task<List<ReviewerInfo>> SearchAssigneesAsync(string owner, string? project, string repo, string searchTerm)
    {
        var users = await SearchReviewersAsync(owner, project, repo, searchTerm);
        return users.Where(candidate => candidate.Kind == ReviewerKind.User).ToList();
    }

    public async Task AddAssigneesAsync(string owner, string? project, string repo, int number, IEnumerable<string> assignees)
    {
        var payload = new { assignees = assignees.ToList() };
        await PostAsync<GitHubIssueDto>($"https://api.github.com/repos/{owner}/{repo}/issues/{number}/assignees", owner, payload);
    }

    public async Task RemoveAssigneeAsync(string owner, string? project, string repo, int number, string assignee)
    {
        var payload = new { assignees = new[] { assignee } };
        await DeleteAsync($"https://api.github.com/repos/{owner}/{repo}/issues/{number}/assignees", owner, payload);
    }

    public async Task AddLabelsAsync(string owner, string? project, string repo, int number, IEnumerable<string> labels)
    {
        var payload = new { labels = labels.ToList() };
        await PostAsync<List<GitHubLabelDto>>($"https://api.github.com/repos/{owner}/{repo}/issues/{number}/labels", owner, payload);
    }

    public async Task RemoveLabelAsync(string owner, string? project, string repo, int number, string label)
    {
        await DeleteAsync($"https://api.github.com/repos/{owner}/{repo}/issues/{number}/labels/{Uri.EscapeDataString(label)}", owner);
    }

    public async Task<List<PullRequestStatusCheckInfo>> GetStatusChecksAsync(string owner, string? project, string repo, int number)
    {
        // First get the PR to find the head SHA
        var pr = await GetAsync<GitHubPullRequestDto>($"https://api.github.com/repos/{owner}/{repo}/pulls/{number}", owner);
        var headSha = pr?.Head?.Sha;
        if (string.IsNullOrEmpty(headSha))
            return [];

        var results = new List<PullRequestStatusCheckInfo>();

        // Fetch check runs (GitHub Actions, etc.)
        try
        {
            var checkRuns = await GetAsync<GitHubCheckRunsResponseDto>(
                $"https://api.github.com/repos/{owner}/{repo}/commits/{headSha}/check-runs", owner);
            if (checkRuns?.CheckRuns != null)
            {
                foreach (var run in checkRuns.CheckRuns)
                {
                    results.Add(new PullRequestStatusCheckInfo
                    {
                        Name = run.Name ?? string.Empty,
                        Description = run.Output?.Summary,
                        Status = MapCheckStatus(run.Status, run.Conclusion),
                        TargetUrl = run.HtmlUrl
                    });
                }
            }
        }
        catch (HttpRequestException ex)
        {
            Log.Error("PR", $"Failed to fetch check runs: {ex.Message}");
        }

        // Fetch legacy commit statuses
        try
        {
            var statuses = await GetAsync<GitHubCombinedStatusDto>(
                $"https://api.github.com/repos/{owner}/{repo}/commits/{headSha}/status", owner);
            if (statuses?.Statuses != null)
            {
                foreach (var status in statuses.Statuses)
                {
                    results.Add(new PullRequestStatusCheckInfo
                    {
                        Name = status.Context ?? string.Empty,
                        Description = status.Description,
                        Status = MapLegacyStatus(status.State),
                        TargetUrl = status.TargetUrl
                    });
                }
            }
        }
        catch (HttpRequestException ex)
        {
            Log.Error("PR", $"Failed to fetch commit statuses: {ex.Message}");
        }

        return results;
    }

    public async Task<PullRequestInfo?> FindPullRequestForCommitAsync(string owner, string? project, string repo, string sha)
    {
        try
        {
            var dtos = await GetAsync<List<GitHubPullRequestDto>>(
                $"https://api.github.com/repos/{owner}/{repo}/commits/{sha}/pulls", owner);
            if (dtos is { Count: > 0 })
                return MapPullRequestInfo(dtos[0]);
        }
        catch (HttpRequestException ex)
        {
            Log.Error("PR", $"Failed to find PR for commit {sha}: {ex.Message}");
        }

        return null;
    }

    // --- HTTP helpers ---

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string owner)
    {
        var pat = _credentialService.GetPat($"GitHub:{owner}")
            ?? throw new InvalidOperationException($"No GitHub PAT configured for '{owner}'. Please add your PAT in Settings.");

        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pat);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        return request;
    }

    private async Task<T?> GetAsync<T>(string url, string owner) where T : class
    {
        var sw = Log.StartTimer();
        using var request = CreateRequest(HttpMethod.Get, url, owner);
        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"GitHub API error ({response.StatusCode}): {error}");
        }

        var json = await response.Content.ReadAsStringAsync();
        Log.Perf("GitHub", $"GET {url}", sw.ElapsedMilliseconds);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private async Task<T> PostAsync<T>(string url, string owner, object payload) where T : class
    {
        var sw = Log.StartTimer();
        using var request = CreateRequest(HttpMethod.Post, url, owner);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"GitHub API error ({response.StatusCode}): {error}");
        }

        var json = await response.Content.ReadAsStringAsync();
        Log.Perf("GitHub", $"POST {url}", sw.ElapsedMilliseconds);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)!;
    }

    private async Task<T> PatchAsync<T>(string url, string owner, object payload) where T : class
    {
        var sw = Log.StartTimer();
        using var request = CreateRequest(HttpMethod.Patch, url, owner);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"GitHub API error ({response.StatusCode}): {error}");
        }

        var json = await response.Content.ReadAsStringAsync();
        Log.Perf("GitHub", $"PATCH {url}", sw.ElapsedMilliseconds);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)!;
    }

    private async Task<T> PutAsync<T>(string url, string owner, object payload) where T : class
    {
        var sw = Log.StartTimer();
        using var request = CreateRequest(HttpMethod.Put, url, owner);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"GitHub API error ({response.StatusCode}): {error}");
        }

        var json = await response.Content.ReadAsStringAsync();
        Log.Perf("GitHub", $"PUT {url}", sw.ElapsedMilliseconds);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)!;
    }

    private async Task DeleteAsync(string url, string owner, object? payload = null)
    {
        var sw = Log.StartTimer();
        using var request = CreateRequest(HttpMethod.Delete, url, owner);
        if (payload != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        }
        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"GitHub API error ({response.StatusCode}): {error}");
        }

        Log.Perf("GitHub", $"DELETE {url}", sw.ElapsedMilliseconds);
    }

    // --- Reviewer helpers ---

    private async Task<List<ReviewerInfo>> FetchCollaboratorsAsync(string owner, string repo)
    {
        try
        {
            var dtos = await GetAllPagesAsync<GitHubCollaboratorDto>(
                $"https://api.github.com/repos/{owner}/{repo}/collaborators", owner);
            return dtos?.Select(c => new ReviewerInfo
            {
                Identifier = c.Login ?? string.Empty,
                DisplayName = c.Login ?? string.Empty,
                AvatarUrl = c.AvatarUrl,
                Kind = ReviewerKind.User
            }).ToList() ?? [];
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("403"))
        {
            Log.Warn("PR", $"Cannot fetch collaborators (insufficient PAT scopes): {ex.Message}");
            return [];
        }
    }

    private async Task<List<ReviewerInfo>> FetchTeamsAsync(string owner, string repo)
    {
        try
        {
            var dtos = await GetAllPagesAsync<GitHubTeamDto>(
                $"https://api.github.com/repos/{owner}/{repo}/teams", owner);
            return dtos?.Select(t => new ReviewerInfo
            {
                Identifier = t.Slug ?? string.Empty,
                DisplayName = t.Name ?? string.Empty,
                SecondaryText = t.Slug,
                Kind = ReviewerKind.Team
            }).ToList() ?? [];
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("403"))
        {
            Log.Warn("PR", $"Cannot fetch teams (insufficient PAT scopes): {ex.Message}");
            return [];
        }
    }

    private async Task<string?> GetAuthenticatedUserLoginAsync(string owner)
    {
        if (_authenticatedUserCache.TryGetValue(owner, out var cachedLogin) && !string.IsNullOrWhiteSpace(cachedLogin))
        {
            return cachedLogin;
        }

        var user = await GetAsync<GitHubAuthenticatedUserDto>("https://api.github.com/user", owner);
        var login = user?.Login;
        if (!string.IsNullOrWhiteSpace(login))
        {
            _authenticatedUserCache[owner] = login;
        }

        return login;
    }

    private static List<ReviewerInfo> FilterReviewers(List<ReviewerInfo> collaborators, List<ReviewerInfo> teams, string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return [.. collaborators, .. teams];

        var term = searchTerm.Trim();
        return [.. collaborators.Where(c => Matches(c, term)), .. teams.Where(t => Matches(t, term))];

        static bool Matches(ReviewerInfo r, string term) =>
            r.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            r.Identifier.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (r.SecondaryText?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private async Task<List<T>> GetAllPagesAsync<T>(string baseUrl, string owner) where T : class
    {
        var results = new List<T>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var separator = baseUrl.Contains('?') ? "&" : "?";
            var pageUrl = $"{baseUrl}{separator}per_page={PerPage}&page={page}";
            var dtos = await GetAsync<List<T>>(pageUrl, owner);
            if (dtos == null || dtos.Count == 0)
                break;

            results.AddRange(dtos);

            if (dtos.Count < PerPage)
                break;
        }

        return results;
    }

    // --- Internal API fetches for detail loading ---

    private async Task<List<PullRequestReviewInfo>> GetReviewsInternalAsync(string owner, string repo, int number)
    {
        var dtos = await GetAsync<List<GitHubReviewDto>>(
            $"https://api.github.com/repos/{owner}/{repo}/pulls/{number}/reviews", owner);

        return dtos?.Select(r => new PullRequestReviewInfo
        {
            ReviewerLogin = r.User?.Login ?? string.Empty,
            ReviewerDisplayName = r.User?.Login,
            AvatarUrl = r.User?.AvatarUrl,
            State = MapReviewState(r.State),
            Body = r.Body,
            SubmittedAt = r.SubmittedAt
        }).ToList() ?? [];
    }

    private async Task<List<PullRequestCommentInfo>> GetCommentsInternalAsync(string owner, string repo, int number)
    {
        var dtos = await GetAsync<List<GitHubCommentDto>>(
            $"https://api.github.com/repos/{owner}/{repo}/pulls/{number}/comments", owner);

        return dtos?.Select(c => new PullRequestCommentInfo
        {
            Id = c.Id,
            AuthorLogin = c.User?.Login ?? string.Empty,
            AuthorDisplayName = c.User?.Login,
            AvatarUrl = c.User?.AvatarUrl,
            Body = c.Body ?? string.Empty,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
            FilePath = c.Path,
            Line = c.Line
        }).ToList() ?? [];
    }

    private async Task<List<PullRequestCommitInfo>> GetCommitsInternalAsync(string owner, string repo, int number)
    {
        var dtos = await GetAllPagesAsync<GitHubPullRequestCommitDto>(
            $"https://api.github.com/repos/{owner}/{repo}/pulls/{number}/commits", owner);

        return dtos.Select(MapCommit).ToList();
    }

    private async Task<GitHubIssueDto?> GetIssueInternalAsync(string owner, string repo, int number)
    {
        return await GetAsync<GitHubIssueDto>(
            $"https://api.github.com/repos/{owner}/{repo}/issues/{number}", owner);
    }

    // --- Mapping helpers ---

    private static PullRequestInfo MapPullRequestInfo(GitHubPullRequestDto dto)
    {
        var state = dto.State?.ToLowerInvariant() switch
        {
            "open" => dto.Draft ? PullRequestState.Draft : PullRequestState.Open,
            "closed" => dto.MergedAt != null ? PullRequestState.Merged : PullRequestState.Closed,
            _ => PullRequestState.Open
        };

        return new PullRequestInfo
        {
            Number = dto.Number,
            Title = dto.Title ?? string.Empty,
            AuthorLogin = dto.User?.Login ?? string.Empty,
            AuthorAvatarUrl = dto.User?.AvatarUrl,
            SourceBranch = dto.Head?.Ref ?? string.Empty,
            TargetBranch = dto.Base?.Ref ?? string.Empty,
            State = state,
            IsDraft = dto.Draft,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt,
            Url = dto.HtmlUrl ?? string.Empty,
            CommentCount = dto.Comments + dto.ReviewComments,
            ChangedFilesCount = dto.ChangedFiles,
            Additions = dto.Additions,
            Deletions = dto.Deletions
        };
    }

    private static PullRequestFileStatus MapFileStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "added" => PullRequestFileStatus.Added,
            "removed" => PullRequestFileStatus.Deleted,
            "modified" or "changed" => PullRequestFileStatus.Modified,
            "renamed" => PullRequestFileStatus.Renamed,
            "copied" => PullRequestFileStatus.Copied,
            _ => PullRequestFileStatus.Modified
        };
    }

    private static PullRequestCommitInfo MapCommit(GitHubPullRequestCommitDto dto)
    {
        var fullMessage = dto.Commit?.Message ?? string.Empty;
        var splitIndex = fullMessage.IndexOf('\n');
        var message = splitIndex >= 0 ? fullMessage[..splitIndex] : fullMessage;
        var description = splitIndex >= 0 ? fullMessage[(splitIndex + 1)..].Trim() : null;

        return new PullRequestCommitInfo
        {
            Sha = dto.Sha ?? string.Empty,
            Message = message,
            Description = string.IsNullOrWhiteSpace(description) ? null : description,
            AuthorDisplayName = dto.Commit?.Author?.Name ?? dto.Author?.Login ?? string.Empty,
            AuthorIdentity = dto.Commit?.Author?.Email ?? dto.Author?.Login,
            Timestamp = dto.Commit?.Author?.Date ?? DateTimeOffset.MinValue,
            Url = dto.HtmlUrl
        };
    }

    private static PullRequestReviewState MapReviewState(string? state)
    {
        return state?.ToUpperInvariant() switch
        {
            "APPROVED" => PullRequestReviewState.Approved,
            "CHANGES_REQUESTED" => PullRequestReviewState.ChangesRequested,
            "COMMENTED" => PullRequestReviewState.Commented,
            "DISMISSED" => PullRequestReviewState.Dismissed,
            "PENDING" => PullRequestReviewState.Pending,
            _ => PullRequestReviewState.Commented
        };
    }

    private static CheckStatus MapCheckStatus(string? status, string? conclusion)
    {
        if (status?.ToLowerInvariant() is "queued" or "in_progress")
            return CheckStatus.Pending;

        return conclusion?.ToLowerInvariant() switch
        {
            "success" => CheckStatus.Success,
            "failure" => CheckStatus.Failure,
            "cancelled" => CheckStatus.Cancelled,
            "neutral" or "skipped" => CheckStatus.Neutral,
            "timed_out" or "action_required" => CheckStatus.Error,
            _ => CheckStatus.Pending
        };
    }

    private static CheckStatus MapLegacyStatus(string? state)
    {
        return state?.ToLowerInvariant() switch
        {
            "success" => CheckStatus.Success,
            "failure" or "error" => CheckStatus.Failure,
            "pending" => CheckStatus.Pending,
            _ => CheckStatus.Pending
        };
    }

    // --- GitHub API DTOs ---

    private class GitHubPullRequestDto
    {
        [JsonPropertyName("number")] public int Number { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("user")] public GitHubUserDto? User { get; set; }
        [JsonPropertyName("head")] public GitHubBranchRefDto? Head { get; set; }
        [JsonPropertyName("base")] public GitHubBranchRefDto? Base { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
        [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
        [JsonPropertyName("merged_at")] public DateTimeOffset? MergedAt { get; set; }
        [JsonPropertyName("closed_at")] public DateTimeOffset? ClosedAt { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("comments")] public int Comments { get; set; }
        [JsonPropertyName("review_comments")] public int ReviewComments { get; set; }
        [JsonPropertyName("changed_files")] public int ChangedFiles { get; set; }
        [JsonPropertyName("additions")] public int Additions { get; set; }
        [JsonPropertyName("deletions")] public int Deletions { get; set; }
        [JsonPropertyName("mergeable")] public bool? Mergeable { get; set; }
        [JsonPropertyName("mergeable_state")] public string? MergeableState { get; set; }
        [JsonPropertyName("requested_reviewers")] public List<GitHubUserDto>? RequestedReviewers { get; set; }
        [JsonPropertyName("requested_teams")] public List<GitHubTeamDto>? RequestedTeams { get; set; }
    }

    private class GitHubUserDto
    {
        [JsonPropertyName("login")] public string? Login { get; set; }
        [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; set; }
    }

    private class GitHubAuthenticatedUserDto
    {
        [JsonPropertyName("login")] public string? Login { get; set; }
    }

    private class GitHubBranchRefDto
    {
        [JsonPropertyName("ref")] public string? Ref { get; set; }
        [JsonPropertyName("sha")] public string? Sha { get; set; }
    }

    private class GitHubFileDto
    {
        [JsonPropertyName("filename")] public string? Filename { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("additions")] public int Additions { get; set; }
        [JsonPropertyName("deletions")] public int Deletions { get; set; }
        [JsonPropertyName("patch")] public string? Patch { get; set; }
    }

    private class GitHubReviewDto
    {
        [JsonPropertyName("user")] public GitHubUserDto? User { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("submitted_at")] public DateTimeOffset SubmittedAt { get; set; }
    }

    private class GitHubCommentDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("user")] public GitHubUserDto? User { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
        [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; set; }
        [JsonPropertyName("path")] public string? Path { get; set; }
        [JsonPropertyName("line")] public int? Line { get; set; }
    }

    private class GitHubCheckRunsResponseDto
    {
        [JsonPropertyName("check_runs")] public List<GitHubCheckRunDto>? CheckRuns { get; set; }
    }

    private class GitHubCheckRunDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("conclusion")] public string? Conclusion { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("output")] public GitHubCheckRunOutputDto? Output { get; set; }
    }

    private class GitHubCheckRunOutputDto
    {
        [JsonPropertyName("summary")] public string? Summary { get; set; }
    }

    private class GitHubPullRequestCommitDto
    {
        [JsonPropertyName("sha")] public string? Sha { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("author")] public GitHubUserDto? Author { get; set; }
        [JsonPropertyName("commit")] public GitHubCommitDto? Commit { get; set; }
    }

    private class GitHubCommitDto
    {
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("author")] public GitHubCommitAuthorDto? Author { get; set; }
    }

    private class GitHubCommitAuthorDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }
        [JsonPropertyName("date")] public DateTimeOffset Date { get; set; }
    }

    private class GitHubCombinedStatusDto
    {
        [JsonPropertyName("statuses")] public List<GitHubStatusDto>? Statuses { get; set; }
    }

    private class GitHubStatusDto
    {
        [JsonPropertyName("context")] public string? Context { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("target_url")] public string? TargetUrl { get; set; }
    }

    private class GitHubCollaboratorDto
    {
        [JsonPropertyName("login")] public string? Login { get; set; }
        [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; set; }
    }

    private class GitHubTeamDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("slug")] public string? Slug { get; set; }
    }

    private class GitHubIssueDto
    {
        [JsonPropertyName("labels")] public List<GitHubLabelDto>? Labels { get; set; }
        [JsonPropertyName("assignees")] public List<GitHubUserDto>? Assignees { get; set; }
    }

    private class GitHubLabelDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private class GitHubMergeResultDto
    {
        [JsonPropertyName("merged")] public bool Merged { get; set; }
        [JsonPropertyName("sha")] public string? Sha { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }
}
