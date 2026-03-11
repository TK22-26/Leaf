using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Leaf.Models;

namespace Leaf.Services.PullRequests;

internal sealed class AzureDevOpsPullRequestProvider : IPullRequestProvider
{
    private const string ApiVersion = "7.1";
    private const string GraphApiVersion = "7.1-preview.1";
    private const string ConnectionDataApiVersion = "7.1-preview.1";
    private const int PageSize = 100;
    private const string PullRequestsSegment = "/pullrequests";
    private const string PullRequestSubresourcesSegment = "/pullRequests";

    private readonly HttpClient _httpClient;
    private readonly CredentialService _credentialService;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly Dictionary<string, (List<ReviewerCandidate> Candidates, DateTime CachedAt)> _reviewerCache = [];
    private readonly Dictionary<string, string> _storageKeyCache = [];
    private readonly Dictionary<string, string> _currentUserIdCache = [];
    private static readonly TimeSpan ReviewerCacheTtl = TimeSpan.FromMinutes(5);

    public PullRequestCapabilities Capabilities =>
        PullRequestCapabilities.DraftPullRequests |
        PullRequestCapabilities.SquashMerge |
        PullRequestCapabilities.RebaseMerge |
        PullRequestCapabilities.MergeCommit |
        PullRequestCapabilities.StatusChecks |
        PullRequestCapabilities.Reviews |
        PullRequestCapabilities.TeamReviewers |
        PullRequestCapabilities.AutoComplete |
        PullRequestCapabilities.RequiredReviewers |
        PullRequestCapabilities.Labels;

    public AzureDevOpsPullRequestProvider(CredentialService credentialService)
    {
        _credentialService = credentialService;
        _httpClient = new HttpClient();
    }

    public async Task<List<PullRequestInfo>> ListPullRequestsAsync(string owner, string? project, string repo, PullRequestState filter)
    {
        project = RequireProject(project);
        var status = filter switch
        {
            PullRequestState.Open or PullRequestState.Draft => "active",
            PullRequestState.Closed => "abandoned",
            PullRequestState.Merged => "completed",
            PullRequestState.All => "all",
            _ => "active"
        };

        var allPrs = new List<PullRequestInfo>();
        for (var skip = 0; ; skip += PageSize)
        {
            var url = BuildRepoApiUrl(owner, project, repo, PullRequestsSegment,
                $"searchCriteria.status={status}&$top={PageSize}&$skip={skip}&api-version={ApiVersion}");

            var response = await GetAsync<AdoListResponse<AdoPullRequestDto>>(url, owner);
            var values = response?.Value ?? [];
            if (values.Count == 0)
                break;

            foreach (var dto in values)
            {
                var pr = MapPullRequestInfo(dto, owner, project, repo);

                if (filter == PullRequestState.Draft && !pr.IsDraft)
                    continue;
                if (filter == PullRequestState.Merged && pr.State != PullRequestState.Merged)
                    continue;

                allPrs.Add(pr);
            }

            if (values.Count < PageSize)
                break;
        }

        return allPrs;
    }

    public async Task<PullRequestDetails?> GetPullRequestAsync(string owner, string? project, string repo, int number)
    {
        project = RequireProject(project);
        var dto = await GetPullRequestDtoAsync(owner, project, repo, number);
        if (dto == null)
            return null;

        var summary = MapPullRequestInfo(dto, owner, project, repo);
        var filesTask = LoadOptionalSectionAsync("files", () => GetPullRequestFilesAsync(owner, project, repo, number), []);
        var commentsTask = LoadOptionalSectionAsync("comments", () => GetCommentsInternalAsync(owner, project, repo, number), []);
        var checksTask = LoadOptionalSectionAsync("checks", () => GetStatusChecksAsync(owner, project, repo, number), []);
        var commitsTask = LoadOptionalSectionAsync("commits", () => GetCommitsInternalAsync(owner, project, repo, number), []);
        var updatesTask = LoadOptionalSectionAsync("updates", () => GetUpdatesInternalAsync(owner, project, repo, number), []);
        var workItemsTask = LoadOptionalSectionAsync("work items", () => GetWorkItemsInternalAsync(owner, project, repo, number), []);
        var labelsTask = LoadOptionalSectionAsync("labels", () => GetLabelsInternalAsync(owner, project, repo, number), []);

        await Task.WhenAll(filesTask, commentsTask, checksTask, commitsTask, updatesTask, workItemsTask, labelsTask);

        var reviewers = dto.Reviewers ?? [];
        return new PullRequestDetails
        {
            Summary = summary,
            Body = dto.Description ?? string.Empty,
            Files = await filesTask,
            Reviews = reviewers.Where(r => r.Vote != 0 || r.HasDeclined).Select(MapReview).ToList(),
            Comments = await commentsTask,
            StatusChecks = await checksTask,
            RequestedReviewers = reviewers.Select(MapReviewer).ToList(),
            Commits = await commitsTask,
            Updates = await updatesTask,
            Labels = await labelsTask,
            WorkItems = await workItemsTask,
            IsMergeable = IsMergeable(dto),
            HasConflicts = string.Equals(dto.MergeStatus, "conflicts", StringComparison.OrdinalIgnoreCase),
            MergeStatusMessage = dto.MergeFailureMessage ?? dto.MergeStatus,
            HeadSha = dto.LastMergeSourceCommit?.CommitId ?? string.Empty,
            BaseSha = dto.LastMergeTargetCommit?.CommitId ?? string.Empty
        };
    }

    public async Task<PullRequestInfo> CreatePullRequestAsync(string owner, string? project, string repo, string title, string body, string sourceBranch, string targetBranch, bool isDraft)
    {
        project = RequireProject(project);
        var payload = new
        {
            title,
            description = body,
            sourceRefName = NormalizeBranchRef(sourceBranch),
            targetRefName = NormalizeBranchRef(targetBranch),
            isDraft
        };

        var url = BuildRepoApiUrl(owner, project, repo, PullRequestsSegment, $"api-version={ApiVersion}");
        var dto = await PostAsync<AdoPullRequestDto>(url, owner, payload);
        return MapPullRequestInfo(dto, owner, project, repo);
    }

    public async Task<PullRequestInfo> UpdatePullRequestAsync(string owner, string? project, string repo, int number, string? title, string? body)
    {
        project = RequireProject(project);
        var payload = new Dictionary<string, object>();
        if (title != null) payload["title"] = title;
        if (body != null) payload["description"] = body;

        var url = BuildRepoApiUrl(owner, project, repo, $"{PullRequestsSegment}/{number}", $"api-version={ApiVersion}");
        var dto = await PatchAsync<AdoPullRequestDto>(url, owner, payload);
        return MapPullRequestInfo(dto, owner, project, repo);
    }

    public async Task<PullRequestMergeResult> MergePullRequestAsync(string owner, string? project, string repo, int number, MergeMethod method, string? commitTitle)
    {
        project = RequireProject(project);

        try
        {
            var current = await GetPullRequestDtoAsync(owner, project, repo, number)
                ?? throw new InvalidOperationException($"Pull request #{number} was not found.");

            var payload = new
            {
                status = "completed",
                lastMergeSourceCommit = new { commitId = current.LastMergeSourceCommit?.CommitId ?? string.Empty },
                completionOptions = new
                {
                    mergeCommitMessage = commitTitle,
                    mergeStrategy = MapMergeStrategy(method)
                }
            };

            var url = BuildRepoApiUrl(owner, project, repo, $"{PullRequestsSegment}/{number}", $"api-version={ApiVersion}");
            var dto = await PatchAsync<AdoPullRequestDto>(url, owner, payload);

            return new PullRequestMergeResult
            {
                Success = dto.Status?.Equals("completed", StringComparison.OrdinalIgnoreCase) == true,
                MergedSha = dto.LastMergeCommit?.CommitId,
                ErrorMessage = dto.MergeFailureMessage
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
        project = RequireProject(project);
        var url = BuildRepoApiUrl(owner, project, repo, $"{PullRequestsSegment}/{number}", $"api-version={ApiVersion}");
        await PatchAsync<AdoPullRequestDto>(url, owner, new { status = "abandoned" });
    }

    public async Task SubmitReviewAsync(string owner, string? project, string repo, int number, PullRequestReviewState state, string? body)
    {
        project = RequireProject(project);
        var reviewerId = await GetCurrentUserIdAsync(owner);
        var url = BuildRepoApiUrl(owner, project, repo, $"{PullRequestSubresourcesSegment}/{number}/reviewers/{Uri.EscapeDataString(reviewerId)}", $"api-version={ApiVersion}");

        await PutAsync<AdoIdentityRefWithVoteDto>(url, owner, new
        {
            id = reviewerId,
            vote = MapReviewVote(state)
        });

        if (!string.IsNullOrWhiteSpace(body))
        {
            await AddCommentAsync(owner, project, repo, number, body);
        }
    }

    public async Task AddCommentAsync(string owner, string? project, string repo, int number, string body)
    {
        project = RequireProject(project);
        var url = BuildRepoApiUrl(owner, project, repo, $"{PullRequestSubresourcesSegment}/{number}/threads", $"api-version={ApiVersion}");
        await PostAsync<AdoThreadDto>(url, owner, new
        {
            comments = new[]
            {
                new
                {
                    content = body
                }
            },
            status = "active"
        });
    }

    public async Task<List<PullRequestFileInfo>> GetPullRequestFilesAsync(string owner, string? project, string repo, int number)
    {
        project = RequireProject(project);
        var iterationsUrl = BuildRepoApiUrl(owner, project, repo, $"{PullRequestSubresourcesSegment}/{number}/iterations", $"api-version={ApiVersion}");
        var iterations = await GetAsync<AdoListResponse<AdoIterationDto>>(iterationsUrl, owner);
        var latestIterationId = iterations?.Value?.Max(i => i.Id) ?? 0;
        if (latestIterationId <= 0)
            return [];

        var changesUrl = BuildRepoApiUrl(owner, project, repo, $"{PullRequestSubresourcesSegment}/{number}/iterations/{latestIterationId}/changes", $"$top=2000&api-version={ApiVersion}");
        var changes = await GetAsync<AdoIterationChangesResponse>(changesUrl, owner);

        return (changes?.ChangeEntries ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Item?.Path))
            .Select(c => new PullRequestFileInfo
            {
                Path = c.Item?.Path ?? string.Empty,
                Status = MapFileStatus(c.ChangeType),
                Additions = 0,
                Deletions = 0
            })
            .ToList();
    }

    public async Task<List<ReviewerInfo>> SearchReviewersAsync(string owner, string? project, string repo, string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return [];

        var cacheKey = owner;
        var now = DateTime.UtcNow;

        if (!_reviewerCache.TryGetValue(cacheKey, out var cached) || now - cached.CachedAt >= ReviewerCacheTtl)
        {
            var usersTask = GetGraphSubjectsAsync(owner, "users");
            var groupsTask = GetGraphSubjectsAsync(owner, "groups");
            await Task.WhenAll(usersTask, groupsTask);

            cached = ([.. await usersTask, .. await groupsTask], now);
            _reviewerCache[cacheKey] = cached;
        }

        var term = searchTerm.Trim();
        var matches = cached.Candidates
            .Where(c =>
                c.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (c.SecondaryText?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
            .Take(50)
            .ToList();

        if (matches.Count == 0)
            return [];

        var reviewers = await Task.WhenAll(matches.Select(c => ToReviewerInfoAsync(owner, c)));
        return reviewers.ToList();
    }

    public async Task RequestReviewersAsync(string owner, string? project, string repo, int number, IEnumerable<ReviewerInfo> reviewers)
    {
        project = RequireProject(project);

        foreach (var reviewer in reviewers)
        {
            var url = BuildRepoApiUrl(owner, project, repo, $"{PullRequestSubresourcesSegment}/{number}/reviewers/{Uri.EscapeDataString(reviewer.Identifier)}", $"api-version={ApiVersion}");
            await PutAsync<AdoIdentityRefWithVoteDto>(url, owner, new
            {
                id = reviewer.Identifier,
                isRequired = reviewer.IsRequired
            });
        }
    }

    public Task<List<ReviewerInfo>> SearchAssigneesAsync(string owner, string? project, string repo, string searchTerm)
    {
        return Task.FromResult(new List<ReviewerInfo>());
    }

    public Task AddAssigneesAsync(string owner, string? project, string repo, int number, IEnumerable<string> assignees)
    {
        return Task.CompletedTask;
    }

    public Task RemoveAssigneeAsync(string owner, string? project, string repo, int number, string assignee)
    {
        return Task.CompletedTask;
    }

    public async Task AddLabelsAsync(string owner, string? project, string repo, int number, IEnumerable<string> labels)
    {
        project = RequireProject(project);

        foreach (var label in labels)
        {
            var url = BuildRepoApiUrl(owner, project, repo, $"{PullRequestSubresourcesSegment}/{number}/labels", $"api-version={ApiVersion}");
            await PostAsync<AdoWebApiTagDefinition>(url, owner, new { name = label });
        }
    }

    public async Task RemoveLabelAsync(string owner, string? project, string repo, int number, string label)
    {
        project = RequireProject(project);
        var url = BuildRepoApiUrl(owner, project, repo, $"{PullRequestSubresourcesSegment}/{number}/labels/{Uri.EscapeDataString(label)}", $"api-version={ApiVersion}");
        await DeleteAsync(url, owner);
    }

    public async Task<List<PullRequestStatusCheckInfo>> GetStatusChecksAsync(string owner, string? project, string repo, int number)
    {
        project = RequireProject(project);
        var url = BuildRepoApiUrl(owner, project, repo, $"{PullRequestSubresourcesSegment}/{number}/statuses", $"api-version={ApiVersion}");
        var response = await GetAsync<AdoListResponse<AdoPullRequestStatusDto>>(url, owner);

        return (response?.Value ?? [])
            .Select(s => new PullRequestStatusCheckInfo
            {
                Name = s.Context?.Name ?? s.Context?.Genre ?? "Status",
                Description = s.Description,
                Status = MapStatus(s.State),
                TargetUrl = s.TargetUrl
            })
            .ToList();
    }

    public async Task<PullRequestInfo?> FindPullRequestForCommitAsync(string owner, string? project, string repo, string sha)
    {
        var all = await ListPullRequestsAsync(owner, project, repo, PullRequestState.All);
        foreach (var pr in all.Take(100))
        {
            var details = await GetPullRequestAsync(owner, project, repo, pr.Number);
            if (details == null)
                continue;

            if (string.Equals(details.HeadSha, sha, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(details.BaseSha, sha, StringComparison.OrdinalIgnoreCase))
            {
                return pr;
            }
        }

        return null;
    }

    private async Task<AdoPullRequestDto?> GetPullRequestDtoAsync(string owner, string project, string repo, int number)
    {
        var url = BuildRepoApiUrl(owner, project, repo, $"{PullRequestsSegment}/{number}", $"api-version={ApiVersion}");
        return await GetAsync<AdoPullRequestDto>(url, owner);
    }

    private async Task<List<PullRequestCommentInfo>> GetCommentsInternalAsync(string owner, string project, string repo, int number)
    {
        var url = BuildRepoApiUrl(owner, project, repo, $"{PullRequestSubresourcesSegment}/{number}/threads", $"api-version={ApiVersion}");
        var response = await GetAsync<AdoListResponse<AdoThreadDto>>(url, owner);
        var comments = new List<PullRequestCommentInfo>();

        foreach (var thread in response?.Value ?? [])
        {
            foreach (var comment in thread.Comments ?? [])
            {
                if (string.IsNullOrWhiteSpace(comment.Content))
                    continue;

                comments.Add(new PullRequestCommentInfo
                {
                    Id = comment.Id,
                    AuthorLogin = comment.Author?.UniqueName ?? comment.Author?.DisplayName ?? string.Empty,
                    AuthorDisplayName = comment.Author?.DisplayName,
                    AvatarUrl = comment.Author?.ImageUrl,
                    Body = comment.Content,
                    CreatedAt = comment.PublishedDate,
                    UpdatedAt = comment.LastUpdatedDate,
                    FilePath = thread.ThreadContext?.FilePath,
                    Line = thread.ThreadContext?.RightFileStart?.Line ?? thread.ThreadContext?.LeftFileStart?.Line,
                    IsResolved = IsResolvedThread(thread.Status)
                });
            }
        }

        return comments;
    }

    private async Task<List<PullRequestCommitInfo>> GetCommitsInternalAsync(string owner, string project, string repo, int number)
    {
        var url = BuildRepoApiUrl(owner, project, repo, $"{PullRequestSubresourcesSegment}/{number}/commits", $"api-version={ApiVersion}");
        var response = await GetAsync<AdoListResponse<AdoGitCommitDto>>(url, owner);
        return (response?.Value ?? []).Select(commit => MapCommit(commit, owner, project, repo)).ToList();
    }

    private async Task<List<PullRequestUpdateInfo>> GetUpdatesInternalAsync(string owner, string project, string repo, int number)
    {
        var iterationsUrl = BuildRepoApiUrl(owner, project, repo, $"{PullRequestSubresourcesSegment}/{number}/iterations", $"api-version={ApiVersion}");
        var iterations = await GetAsync<AdoListResponse<AdoPullRequestIterationDto>>(iterationsUrl, owner);
        if (iterations?.Value == null || iterations.Value.Count == 0)
            return [];

        var tasks = iterations.Value.Select(async iteration =>
        {
            var commits = await GetIterationCommitsAsync(owner, project, repo, number, iteration.Id);
            var actor = iteration.Author?.DisplayName ?? iteration.Author?.UniqueName ?? "Someone";
            var commitCount = commits.Count;
            var commitLabel = commitCount == 1 ? "commit" : "commits";

            return new PullRequestUpdateInfo
            {
                Id = iteration.Id,
                Title = commitCount > 0
                    ? $"{actor} pushed {commitCount} {commitLabel}"
                    : $"{actor} updated the pull request",
                Description = string.IsNullOrWhiteSpace(iteration.Reason) ? null : iteration.Reason,
                AuthorDisplayName = actor,
                Timestamp = iteration.UpdatedDate ?? iteration.CreatedDate,
                BaseCommitSha = iteration.CommonRefCommit?.CommitId,
                Commits = commits
            };
        });

        return [.. (await Task.WhenAll(tasks)).OrderByDescending(update => update.Timestamp)];
    }

    private async Task<List<PullRequestCommitInfo>> GetIterationCommitsAsync(string owner, string project, string repo, int number, int iterationId)
    {
        var url = BuildRepoApiUrl(owner, project, repo, $"{PullRequestSubresourcesSegment}/{number}/iterations/{iterationId}/commits", $"api-version={ApiVersion}");
        var response = await GetAsync<AdoListResponse<AdoGitCommitDto>>(url, owner);
        return (response?.Value ?? []).Select(commit => MapCommit(commit, owner, project, repo)).ToList();
    }

    private async Task<List<PullRequestWorkItemInfo>> GetWorkItemsInternalAsync(string owner, string project, string repo, int number)
    {
        var url = BuildRepoApiUrl(owner, project, repo, $"{PullRequestSubresourcesSegment}/{number}/workitems", $"api-version={ApiVersion}");
        var response = await GetAsync<AdoListResponse<AdoWorkItemRefDto>>(url, owner);
        if (response?.Value == null || response.Value.Count == 0)
            return [];

        var workItemTasks = response.Value
            .Where(item => item.Id > 0)
            .Select(item => GetWorkItemAsync(owner, project, item));

        var items = await Task.WhenAll(workItemTasks);
        return items.Where(item => item != null).Select(item => item!).ToList();
    }

    private async Task<List<PullRequestLabelInfo>> GetLabelsInternalAsync(string owner, string project, string repo, int number)
    {
        var url = BuildRepoApiUrl(owner, project, repo, $"{PullRequestSubresourcesSegment}/{number}/labels", $"api-version={ApiVersion}");
        var response = await GetAsync<AdoListResponse<AdoWebApiTagDefinition>>(url, owner);

        return (response?.Value ?? [])
            .Where(label => !string.IsNullOrWhiteSpace(label.Name))
            .Select(label => new PullRequestLabelInfo { Name = label.Name! })
            .ToList();
    }

    private async Task<PullRequestWorkItemInfo?> GetWorkItemAsync(string owner, string project, AdoWorkItemRefDto reference)
    {
        var detailUrl = $"https://dev.azure.com/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(project)}/_apis/wit/workitems/{reference.Id}?api-version={ApiVersion}";
        var dto = await GetAsync<AdoWorkItemDto>(detailUrl, owner);
        if (dto == null)
            return null;

        dto.Fields ??= [];
        return new PullRequestWorkItemInfo
        {
            Id = dto.Id,
            Title = GetWorkItemField(dto.Fields, "System.Title") ?? $"Work item {dto.Id}",
            Type = GetWorkItemField(dto.Fields, "System.WorkItemType"),
            State = GetWorkItemField(dto.Fields, "System.State"),
            Url = dto.Links?.Html?.Href ?? $"https://dev.azure.com/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(project)}/_workitems/edit/{dto.Id}"
        };
    }

    private async Task<string> GetCurrentUserIdAsync(string owner)
    {
        if (_currentUserIdCache.TryGetValue(owner, out var cached))
            return cached;

        var url = $"https://dev.azure.com/{Uri.EscapeDataString(owner)}/_apis/connectionData?connectOptions=IncludeAuthenticatedUser&lastChangeId=-1&lastChangeId64=-1&api-version={ConnectionDataApiVersion}";
        var response = await GetAsync<AdoConnectionDataDto>(url, owner)
            ?? throw new InvalidOperationException("Unable to resolve the current Azure DevOps identity.");

        var id = response.AuthenticatedUser?.Id;
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("Azure DevOps did not return an authenticated user id.");

        _currentUserIdCache[owner] = id;
        return id;
    }

    private async Task<T> LoadOptionalSectionAsync<T>(string sectionName, Func<Task<T>> loader, T fallback)
    {
        try
        {
            return await loader();
        }
        catch (Exception ex)
        {
            Log.Error("AzureDevOps", $"Failed to load PR {sectionName}: {ex.Message}", ex);
            return fallback;
        }
    }

    private async Task<List<ReviewerCandidate>> GetGraphSubjectsAsync(string owner, string subjectType)
    {
        var results = new List<ReviewerCandidate>();
        string? continuationToken = null;

        do
        {
            var query = $"api-version={GraphApiVersion}";
            if (!string.IsNullOrEmpty(continuationToken))
                query += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";

            var url = $"https://vssps.dev.azure.com/{Uri.EscapeDataString(owner)}/_apis/graph/{subjectType}?{query}";
            var (response, nextToken) = await GetWithContinuationAsync<AdoListResponse<AdoGraphSubjectDto>>(url, owner);

            results.AddRange((response?.Value ?? []).Select(subject => new ReviewerCandidate(
                subject.Descriptor ?? string.Empty,
                subject.DisplayName ?? subject.PrincipalName ?? subject.MailAddress ?? string.Empty,
                subject.MailAddress ?? subject.PrincipalName,
                subject.Links?.Avatar?.Href,
                subjectType == "groups" ? ReviewerKind.Group : ReviewerKind.User)));

            continuationToken = nextToken;
        }
        while (!string.IsNullOrEmpty(continuationToken));

        return results
            .Where(r => !string.IsNullOrWhiteSpace(r.Descriptor) && !string.IsNullOrWhiteSpace(r.DisplayName))
            .ToList();
    }

    private async Task<ReviewerInfo> ToReviewerInfoAsync(string owner, ReviewerCandidate candidate)
    {
        var storageKey = await ResolveStorageKeyAsync(owner, candidate.Descriptor);
        return new ReviewerInfo
        {
            Identifier = storageKey,
            DisplayName = candidate.DisplayName,
            SecondaryText = candidate.SecondaryText,
            AvatarUrl = candidate.AvatarUrl,
            Kind = candidate.Kind
        };
    }

    private async Task<string> ResolveStorageKeyAsync(string owner, string descriptor)
    {
        var cacheKey = $"{owner}:{descriptor}";
        if (_storageKeyCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var url = $"https://vssps.dev.azure.com/{Uri.EscapeDataString(owner)}/_apis/graph/storagekeys/{Uri.EscapeDataString(descriptor)}?api-version={GraphApiVersion}";
        var response = await GetAsync<AdoStorageKeyDto>(url, owner)
            ?? throw new InvalidOperationException($"Unable to resolve Azure DevOps storage key for reviewer '{descriptor}'.");

        var storageKey = response.Value ?? descriptor;
        _storageKeyCache[cacheKey] = storageKey;
        return storageKey;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string organization)
    {
        var pat = _credentialService.GetPat($"AzureDevOps:{organization}")
            ?? throw new InvalidOperationException($"No Azure DevOps PAT configured for '{organization}'. Please add it in Settings.");

        var request = new HttpRequestMessage(method, url);
        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<T?> GetAsync<T>(string url, string organization) where T : class
    {
        var sw = Log.StartTimer();
        using var request = CreateRequest(HttpMethod.Get, url, organization);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log.Error("AzureDevOps", $"GET {url} failed before response: {ex.Message}", ex);
            throw;
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Log.Error("AzureDevOps", $"GET {url} failed ({response.StatusCode}): {FormatErrorBody(error)}");
                throw new HttpRequestException($"Azure DevOps API error ({response.StatusCode}) at {url}: {FormatErrorBody(error)}");
            }

            var json = await response.Content.ReadAsStringAsync();
            Log.Perf("AzureDevOps", $"GET {url}", sw.ElapsedMilliseconds);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
    }

    private async Task<(T? Payload, string? ContinuationToken)> GetWithContinuationAsync<T>(string url, string organization) where T : class
    {
        var sw = Log.StartTimer();
        using var request = CreateRequest(HttpMethod.Get, url, organization);
        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Log.Error("AzureDevOps", $"GET {url} failed ({response.StatusCode}): {FormatErrorBody(error)}");
            throw new HttpRequestException($"Azure DevOps API error ({response.StatusCode}) at {url}: {FormatErrorBody(error)}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var continuation = response.Headers.TryGetValues("X-MS-ContinuationToken", out var values)
            ? values.FirstOrDefault()
            : null;

        Log.Perf("AzureDevOps", $"GET {url}", sw.ElapsedMilliseconds);
        return (JsonSerializer.Deserialize<T>(json, JsonOptions), continuation);
    }

    private async Task<T> PostAsync<T>(string url, string organization, object payload) where T : class
    {
        var sw = Log.StartTimer();
        using var request = CreateRequest(HttpMethod.Post, url, organization);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Log.Error("AzureDevOps", $"POST {url} failed ({response.StatusCode}): {FormatErrorBody(error)}");
            throw new HttpRequestException($"Azure DevOps API error ({response.StatusCode}) at {url}: {FormatErrorBody(error)}");
        }

        var json = await response.Content.ReadAsStringAsync();
        Log.Perf("AzureDevOps", $"POST {url}", sw.ElapsedMilliseconds);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)!;
    }

    private async Task<T> PatchAsync<T>(string url, string organization, object payload) where T : class
    {
        var sw = Log.StartTimer();
        using var request = CreateRequest(HttpMethod.Patch, url, organization);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Log.Error("AzureDevOps", $"PATCH {url} failed ({response.StatusCode}): {FormatErrorBody(error)}");
            throw new HttpRequestException($"Azure DevOps API error ({response.StatusCode}) at {url}: {FormatErrorBody(error)}");
        }

        var json = await response.Content.ReadAsStringAsync();
        Log.Perf("AzureDevOps", $"PATCH {url}", sw.ElapsedMilliseconds);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)!;
    }

    private async Task<T> PutAsync<T>(string url, string organization, object payload) where T : class
    {
        var sw = Log.StartTimer();
        using var request = CreateRequest(HttpMethod.Put, url, organization);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Log.Error("AzureDevOps", $"PUT {url} failed ({response.StatusCode}): {FormatErrorBody(error)}");
            throw new HttpRequestException($"Azure DevOps API error ({response.StatusCode}) at {url}: {FormatErrorBody(error)}");
        }

        var json = await response.Content.ReadAsStringAsync();
        Log.Perf("AzureDevOps", $"PUT {url}", sw.ElapsedMilliseconds);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)!;
    }

    private async Task DeleteAsync(string url, string organization)
    {
        var sw = Log.StartTimer();
        using var request = CreateRequest(HttpMethod.Delete, url, organization);
        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Log.Error("AzureDevOps", $"DELETE {url} failed ({response.StatusCode}): {FormatErrorBody(error)}");
            throw new HttpRequestException($"Azure DevOps API error ({response.StatusCode}) at {url}: {FormatErrorBody(error)}");
        }

        Log.Perf("AzureDevOps", $"DELETE {url}", sw.ElapsedMilliseconds);
    }

    private static string RequireProject(string? project)
    {
        if (string.IsNullOrWhiteSpace(project))
            throw new InvalidOperationException("Azure DevOps repository URL is missing its project name.");
        return project;
    }

    private static string BuildRepoApiUrl(string owner, string project, string repo, string suffix, string query)
        => $"https://dev.azure.com/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repo)}{suffix}?{query}";

    private static string FormatErrorBody(string? error)
        => string.IsNullOrWhiteSpace(error) ? "<empty response body>" : error.Trim();

    private static string NormalizeBranchRef(string branchName)
        => branchName.StartsWith("refs/", StringComparison.OrdinalIgnoreCase) ? branchName : $"refs/heads/{branchName}";

    private static string MapMergeStrategy(MergeMethod method) => method switch
    {
        MergeMethod.Squash => "squash",
        MergeMethod.Rebase => "rebase",
        _ => "noFastForward"
    };

    private static PullRequestInfo MapPullRequestInfo(AdoPullRequestDto dto, string owner, string project, string repo)
    {
        return new PullRequestInfo
        {
            Number = dto.PullRequestId,
            Title = dto.Title ?? string.Empty,
            AuthorLogin = dto.CreatedBy?.UniqueName ?? dto.CreatedBy?.DisplayName ?? string.Empty,
            AuthorAvatarUrl = dto.CreatedBy?.ImageUrl,
            SourceBranch = StripRefPrefix(dto.SourceRefName),
            TargetBranch = StripRefPrefix(dto.TargetRefName),
            State = MapPullRequestState(dto),
            IsDraft = dto.IsDraft,
            CreatedAt = dto.CreationDate,
            UpdatedAt = dto.ClosedDate ?? dto.CreationDate,
            Url = dto.Links?.Web?.Href ?? BuildPullRequestWebUrl(owner, project, repo, dto.PullRequestId),
            CommentCount = 0,
            ChangedFilesCount = 0,
            Additions = 0,
            Deletions = 0
        };
    }

    private static PullRequestState MapPullRequestState(AdoPullRequestDto dto) => dto.Status?.ToLowerInvariant() switch
    {
        "active" => dto.IsDraft ? PullRequestState.Draft : PullRequestState.Open,
        "completed" => dto.MergeStatus?.Equals("succeeded", StringComparison.OrdinalIgnoreCase) == true ? PullRequestState.Merged : PullRequestState.Closed,
        "abandoned" => PullRequestState.Closed,
        _ => PullRequestState.Open
    };

    private static ReviewerInfo MapReviewer(AdoIdentityRefWithVoteDto reviewer)
    {
        return new ReviewerInfo
        {
            Identifier = reviewer.Id ?? reviewer.Descriptor ?? string.Empty,
            DisplayName = reviewer.DisplayName ?? reviewer.UniqueName ?? string.Empty,
            SecondaryText = reviewer.UniqueName,
            AvatarUrl = reviewer.ImageUrl,
            Kind = reviewer.IsContainer ? ReviewerKind.Group : ReviewerKind.User,
            IsRequired = reviewer.IsRequired
        };
    }

    private static PullRequestReviewInfo MapReview(AdoIdentityRefWithVoteDto reviewer)
    {
        return new PullRequestReviewInfo
        {
            ReviewerLogin = reviewer.UniqueName ?? reviewer.DisplayName ?? string.Empty,
            ReviewerDisplayName = reviewer.DisplayName,
            AvatarUrl = reviewer.ImageUrl,
            State = MapVote(reviewer),
            Body = reviewer.IsRequired ? "Required reviewer" : null,
            SubmittedAt = DateTimeOffset.MinValue
        };
    }

    private static PullRequestCommitInfo MapCommit(AdoGitCommitDto commit, string owner, string project, string repo)
    {
        var fullMessage = commit.Comment ?? string.Empty;
        var splitIndex = fullMessage.IndexOf('\n');
        var message = splitIndex >= 0 ? fullMessage[..splitIndex] : fullMessage;
        var description = splitIndex >= 0 ? fullMessage[(splitIndex + 1)..].Trim() : null;

        return new PullRequestCommitInfo
        {
            Sha = commit.CommitId ?? string.Empty,
            Message = message,
            Description = string.IsNullOrWhiteSpace(description) ? null : description,
            AuthorDisplayName = commit.Author?.Name ?? string.Empty,
            AuthorIdentity = commit.Author?.Email,
            Timestamp = commit.Author?.Date ?? DateTimeOffset.MinValue,
            Url = commit.RemoteUrl ?? BuildCommitWebUrl(owner, project, repo, commit.CommitId)
        };
    }

    private static PullRequestReviewState MapVote(AdoIdentityRefWithVoteDto reviewer)
    {
        if (reviewer.HasDeclined)
            return PullRequestReviewState.Dismissed;

        return reviewer.Vote switch
        {
            >= 5 => PullRequestReviewState.Approved,
            <= -5 => PullRequestReviewState.ChangesRequested,
            0 => PullRequestReviewState.Pending,
            _ => PullRequestReviewState.Commented
        };
    }

    private static int MapReviewVote(PullRequestReviewState state) => state switch
    {
        PullRequestReviewState.Approved => 10,
        PullRequestReviewState.ChangesRequested => -10,
        PullRequestReviewState.Commented => 0,
        PullRequestReviewState.Pending => 0,
        PullRequestReviewState.Dismissed => 0,
        _ => 0
    };

    private static PullRequestFileStatus MapFileStatus(string? changeType)
    {
        var normalized = changeType?.ToLowerInvariant() ?? string.Empty;
        if (normalized.Contains("rename", StringComparison.Ordinal))
            return PullRequestFileStatus.Renamed;
        if (normalized.Contains("delete", StringComparison.Ordinal))
            return PullRequestFileStatus.Deleted;
        if (normalized.Contains("add", StringComparison.Ordinal))
            return PullRequestFileStatus.Added;
        return PullRequestFileStatus.Modified;
    }

    private static CheckStatus MapStatus(string? state) => state?.ToLowerInvariant() switch
    {
        "succeeded" => CheckStatus.Success,
        "failed" => CheckStatus.Failure,
        "error" => CheckStatus.Error,
        "pending" or "queued" or "notset" => CheckStatus.Pending,
        "notapplicable" => CheckStatus.Neutral,
        _ => CheckStatus.Pending
    };

    private static bool IsMergeable(AdoPullRequestDto dto)
    {
        if (!string.Equals(dto.Status, "active", StringComparison.OrdinalIgnoreCase))
            return false;

        return dto.MergeStatus?.ToLowerInvariant() switch
        {
            "conflicts" or "failure" or "rejectedbypolicy" => false,
            _ => true
        };
    }

    private static bool IsResolvedThread(string? status) => status?.ToLowerInvariant() switch
    {
        "fixed" or "closed" or "wontfix" or "bydesign" => true,
        _ => false
    };

    private static string? GetWorkItemField(Dictionary<string, JsonElement> fields, string key)
    {
        if (!fields.TryGetValue(key, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => value.ToString()
        };
    }

    private static string StripRefPrefix(string? value)
        => value?.StartsWith("refs/heads/", StringComparison.OrdinalIgnoreCase) == true ? value["refs/heads/".Length..] : value ?? string.Empty;

    private static string BuildPullRequestWebUrl(string owner, string project, string repo, int pullRequestId)
        => $"https://dev.azure.com/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(project)}/_git/{Uri.EscapeDataString(repo)}/pullrequest/{pullRequestId}";

    private static string? BuildCommitWebUrl(string owner, string project, string repo, string? commitId)
        => string.IsNullOrWhiteSpace(commitId)
            ? null
            : $"https://dev.azure.com/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(project)}/_git/{Uri.EscapeDataString(repo)}/commit/{Uri.EscapeDataString(commitId)}";

    private sealed record ReviewerCandidate(string Descriptor, string DisplayName, string? SecondaryText, string? AvatarUrl, ReviewerKind Kind);

    private sealed class AdoListResponse<T>
    {
        [JsonPropertyName("count")] public int Count { get; set; }
        [JsonPropertyName("value")] public List<T>? Value { get; set; }
    }

    private sealed class AdoPullRequestDto
    {
        [JsonPropertyName("pullRequestId")] public int PullRequestId { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("isDraft")] public bool IsDraft { get; set; }
        [JsonPropertyName("creationDate")] public DateTimeOffset CreationDate { get; set; }
        [JsonPropertyName("closedDate")] public DateTimeOffset? ClosedDate { get; set; }
        [JsonPropertyName("createdBy")] public AdoIdentityRefWithVoteDto? CreatedBy { get; set; }
        [JsonPropertyName("sourceRefName")] public string? SourceRefName { get; set; }
        [JsonPropertyName("targetRefName")] public string? TargetRefName { get; set; }
        [JsonPropertyName("reviewers")] public List<AdoIdentityRefWithVoteDto>? Reviewers { get; set; }
        [JsonPropertyName("lastMergeSourceCommit")] public AdoCommitRefDto? LastMergeSourceCommit { get; set; }
        [JsonPropertyName("lastMergeTargetCommit")] public AdoCommitRefDto? LastMergeTargetCommit { get; set; }
        [JsonPropertyName("lastMergeCommit")] public AdoCommitRefDto? LastMergeCommit { get; set; }
        [JsonPropertyName("mergeStatus")] public string? MergeStatus { get; set; }
        [JsonPropertyName("mergeFailureMessage")] public string? MergeFailureMessage { get; set; }
        [JsonPropertyName("_links")] public AdoLinksDto? Links { get; set; }
    }

    private sealed class AdoCommitRefDto
    {
        [JsonPropertyName("commitId")] public string? CommitId { get; set; }
    }

    private sealed class AdoGitCommitDto
    {
        [JsonPropertyName("commitId")] public string? CommitId { get; set; }
        [JsonPropertyName("comment")] public string? Comment { get; set; }
        [JsonPropertyName("author")] public AdoGitUserDateDto? Author { get; set; }
        [JsonPropertyName("remoteUrl")] public string? RemoteUrl { get; set; }
    }

    private sealed class AdoGitUserDateDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }
        [JsonPropertyName("date")] public DateTimeOffset Date { get; set; }
    }

    private sealed class AdoIdentityRefWithVoteDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("descriptor")] public string? Descriptor { get; set; }
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("uniqueName")] public string? UniqueName { get; set; }
        [JsonPropertyName("imageUrl")] public string? ImageUrl { get; set; }
        [JsonPropertyName("vote")] public int Vote { get; set; }
        [JsonPropertyName("isRequired")] public bool IsRequired { get; set; }
        [JsonPropertyName("hasDeclined")] public bool HasDeclined { get; set; }
        [JsonPropertyName("isContainer")] public bool IsContainer { get; set; }
    }

    private sealed class AdoLinksDto
    {
        [JsonPropertyName("web")] public AdoHrefDto? Web { get; set; }
    }

    private sealed class AdoHrefDto
    {
        [JsonPropertyName("href")] public string? Href { get; set; }
    }

    private sealed class AdoWebApiTagDefinition
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class AdoIterationDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
    }

    private sealed class AdoIterationChangesResponse
    {
        [JsonPropertyName("changeEntries")] public List<AdoChangeEntryDto>? ChangeEntries { get; set; }
    }

    private sealed class AdoChangeEntryDto
    {
        [JsonPropertyName("changeType")] public string? ChangeType { get; set; }
        [JsonPropertyName("item")] public AdoGitItemDto? Item { get; set; }
    }

    private sealed class AdoGitItemDto
    {
        [JsonPropertyName("path")] public string? Path { get; set; }
    }

    private sealed class AdoPullRequestStatusDto
    {
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("targetUrl")] public string? TargetUrl { get; set; }
        [JsonPropertyName("context")] public AdoStatusContextDto? Context { get; set; }
    }

    private sealed class AdoStatusContextDto
    {
        [JsonPropertyName("genre")] public string? Genre { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class AdoThreadDto
    {
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("comments")] public List<AdoCommentDto>? Comments { get; set; }
        [JsonPropertyName("threadContext")] public AdoThreadContextDto? ThreadContext { get; set; }
    }

    private sealed class AdoCommentDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("publishedDate")] public DateTimeOffset PublishedDate { get; set; }
        [JsonPropertyName("lastUpdatedDate")] public DateTimeOffset? LastUpdatedDate { get; set; }
        [JsonPropertyName("author")] public AdoIdentityRefWithVoteDto? Author { get; set; }
    }

    private sealed class AdoThreadContextDto
    {
        [JsonPropertyName("filePath")] public string? FilePath { get; set; }
        [JsonPropertyName("leftFileStart")] public AdoCommentPositionDto? LeftFileStart { get; set; }
        [JsonPropertyName("rightFileStart")] public AdoCommentPositionDto? RightFileStart { get; set; }
    }

    private sealed class AdoCommentPositionDto
    {
        [JsonPropertyName("line")] public int? Line { get; set; }
    }

    private sealed class AdoGraphSubjectDto
    {
        [JsonPropertyName("descriptor")] public string? Descriptor { get; set; }
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("principalName")] public string? PrincipalName { get; set; }
        [JsonPropertyName("mailAddress")] public string? MailAddress { get; set; }
        [JsonPropertyName("_links")] public AdoGraphLinksDto? Links { get; set; }
    }

    private sealed class AdoGraphLinksDto
    {
        [JsonPropertyName("avatar")] public AdoHrefDto? Avatar { get; set; }
    }

    private sealed class AdoStorageKeyDto
    {
        [JsonPropertyName("value")] public string? Value { get; set; }
    }

    private sealed class AdoPullRequestIterationDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("reason")] public string? Reason { get; set; }
        [JsonPropertyName("createdDate")] public DateTimeOffset CreatedDate { get; set; }
        [JsonPropertyName("updatedDate")] public DateTimeOffset? UpdatedDate { get; set; }
        [JsonPropertyName("author")] public AdoIdentityRefWithVoteDto? Author { get; set; }
        [JsonPropertyName("commonRefCommit")] public AdoCommitRefDto? CommonRefCommit { get; set; }
    }

    private sealed class AdoWorkItemRefDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    }

    private sealed class AdoWorkItemDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("fields")] public Dictionary<string, JsonElement>? Fields { get; set; }
        [JsonPropertyName("_links")] public AdoWorkItemLinksDto? Links { get; set; }
    }

    private sealed class AdoWorkItemLinksDto
    {
        [JsonPropertyName("html")] public AdoHrefDto? Html { get; set; }
    }

    private sealed class AdoConnectionDataDto
    {
        [JsonPropertyName("authenticatedUser")] public AdoIdentityRefWithVoteDto? AuthenticatedUser { get; set; }
    }
}
