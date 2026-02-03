# Pull Request Implementation Plan (Leaf)

## Goals
- Add full pull request support for GitHub and Azure DevOps (list, view, create, update, merge).
- Integrate PRs into the existing UI without disrupting graph/diff workflows.
- Use existing auth (PAT + GitHub OAuth device flow) and remote detection already in Leaf.

## Scope
- Providers: GitHub and Azure DevOps.
- PR features: list/filter, details, commits, files/diffs, comments, review state, status checks, merge, close.
- Update PR: title, body, draft toggle (labels/reviewers/milestones deferred to future phase).
- Local repo actions: checkout PR branch, fetch PR refs, open related file diff, create PR from local branch.
- Find PR: lookup PRs by commit SHA (for "Find Pull Request..." context menu).

## Architecture Overview
- Add a provider-agnostic PR domain layer with provider-specific adapters.
- Surface PRs in a dedicated `PullRequestPanel` control (separate from the Diff/Blame/History viewer).
- Keep all provider access behind a single interface (`IPullRequestService`) so we can add more providers later.
- Use dependency injection for all services to enable testing and avoid tight coupling.

---

## Data Models

**Location:** `src/Leaf/Models/` (follows existing pattern of POCO classes)

**Pattern Notes:**
- Follow `CommitInfo.cs` pattern: plain properties with computed display helpers (`ShortSha`, `DateDisplay`, etc.)
- Use `ObservableObject` base class from CommunityToolkit.Mvvm when UI binding is needed
- Keep models thread-safe and serializable (no provider-specific types exposed)
- Use `[ObservableProperty]` for properties that need change notification

### PullRequestInfo.cs

**Design Decision: Immutable Data + Observable Selection State**

PR data from the API doesn't change while viewing (we re-fetch on refresh).
Only selection state needs to be observable. This matches the `CommitInfo` pattern.

If future requirements need in-place updates (e.g., real-time comment count),
convert the affected fields to `[ObservableProperty]`.

```csharp
namespace Leaf.Models;

public partial class PullRequestInfo : ObservableObject
{
    // ============================================
    // IMMUTABLE FIELDS (set once from API response)
    // ============================================
    // These don't change after creation. If the PR is updated,
    // we replace the entire object in the collection.

    /// <summary>
    /// PR number used for display and API calls.
    /// GitHub: "number" field. Azure DevOps: "pullRequestId" field.
    /// </summary>
    public int Number { get; init; }

    /// <summary>
    /// Provider-specific identifier (string to handle Azure DevOps GUIDs if needed).
    /// Usually same as Number.ToString() but kept separate for provider flexibility.
    /// </summary>
    public string ProviderId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string AuthorEmail { get; init; } = string.Empty;
    public string AuthorAvatarUrl { get; init; } = string.Empty;
    public string SourceBranch { get; init; } = string.Empty;
    public string TargetBranch { get; init; } = string.Empty;
    public string SourceBranchName { get; init; } = string.Empty;
    public string TargetBranchName { get; init; } = string.Empty;
    public PullRequestStatus Status { get; init; }
    public bool IsDraft { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string Url { get; init; } = string.Empty;
    public RemoteType Provider { get; init; }
    public string RepoFullName { get; init; } = string.Empty;
    public int CommitCount { get; init; }
    public int CommentCount { get; init; }
    public int AdditionsCount { get; init; }
    public int DeletionsCount { get; init; }

    /// <summary>
    /// SHA of the PR head commit. Used for status checks and checkout.
    /// GitHub: head.sha. Azure DevOps: lastMergeSourceCommit.commitId.
    /// Avoids extra API calls when fetching checks or checking out.
    /// </summary>
    public string HeadSha { get; init; } = string.Empty;

    /// <summary>
    /// SHA of the PR base commit (merge target). Used for diff base.
    /// GitHub: base.sha. Azure DevOps: lastMergeTargetCommit.commitId.
    /// </summary>
    public string BaseSha { get; init; } = string.Empty;

    // ============================================
    // COMPUTED PROPERTIES (derived from immutable data)
    // ============================================

    public string NumberDisplay => $"#{Number}";
    public string AvatarKey => !string.IsNullOrWhiteSpace(AuthorEmail) ? AuthorEmail : Author;

    /// <summary>
    /// Friendly date string for display (follows CommitInfo.DateDisplay pattern).
    /// </summary>
    public string DateDisplay
    {
        get
        {
            var now = DateTimeOffset.Now;
            var diff = now - UpdatedAt;

            if (diff.TotalMinutes < 1)
                return "Just now";
            if (diff.TotalHours < 1)
                return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalDays < 1)
                return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays}d ago";
            if (UpdatedAt.Year == now.Year)
                return UpdatedAt.ToString("MMM d");
            return UpdatedAt.ToString("MMM d, yyyy");
        }
    }

    // ============================================
    // OBSERVABLE STATE (changes during UI interaction)
    // ============================================

    /// <summary>
    /// Selection state - observable because it changes via UI interaction.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;
}

// ============================================
// UPDATE STRATEGY
// ============================================
// When PR data changes (after merge, new comments, etc.):
// 1. Re-fetch from API
// 2. Create new PullRequestInfo instance
// 3. Replace in ObservableCollection (triggers UI update)
//
// Do NOT mutate existing instances - this ensures UI consistency
// and avoids race conditions with async operations.

/// <summary>
/// PR status for filtering and display.
/// Note: Draft is NOT included here - use IsDraft property instead.
/// A PR can be both Open AND Draft simultaneously.
/// </summary>
public enum PullRequestStatus
{
    Open,
    Closed,
    Merged,
    All  // For filtering only
}
```

### PullRequestFileInfo.cs
```csharp
namespace Leaf.Models;

// Reuses existing FileChangeStatus enum from FileChangeInfo.cs
public class PullRequestFileInfo
{
    public string Path { get; set; } = string.Empty;
    public string? OldPath { get; set; }                 // For renames
    public FileChangeStatus Status { get; set; }        // Reuse existing enum
    public int Additions { get; set; }
    public int Deletions { get; set; }

    /// <summary>
    /// True if this is a binary file.
    /// When true, Patch will be null and diff must use binary comparison.
    /// </summary>
    public bool IsBinary { get; set; }

    /// <summary>
    /// Unified diff patch content from API.
    /// NULL when:
    /// - File is binary (IsBinary == true)
    /// - File is too large (GitHub omits patch for files > ~1MB)
    /// - API doesn't provide patches (Azure DevOps)
    ///
    /// When null, use GetPullRequestFileDiffAsync which fetches file contents.
    /// </summary>
    public string? Patch { get; set; }

    /// <summary>
    /// True if patch is unavailable (binary, too large, or not provided by API).
    /// Use this to decide whether to show "View Diff" or fetch content separately.
    /// </summary>
    public bool HasPatch => !string.IsNullOrEmpty(Patch);

    /// <summary>
    /// True if diff cannot be displayed inline (binary or patch unavailable for large file).
    /// </summary>
    public bool IsDiffUnavailable => IsBinary || (!HasPatch && (Additions + Deletions) > 0);

    // Computed (follow FileChangeInfo pattern)
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
    public string StatusIndicator => Status switch
    {
        FileChangeStatus.Added => "+",
        FileChangeStatus.Modified => "M",
        FileChangeStatus.Deleted => "-",
        FileChangeStatus.Renamed => "R",
        FileChangeStatus.Copied => "C",
        _ => " "
    };
}
```

### PullRequestReviewInfo.cs
```csharp
namespace Leaf.Models;

public class PullRequestReviewInfo
{
    public string ReviewId { get; set; } = string.Empty;
    public string Reviewer { get; set; } = string.Empty;
    public string ReviewerAvatarUrl { get; set; } = string.Empty;
    public PullRequestReviewState State { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public string Body { get; set; } = string.Empty;
}

public enum PullRequestReviewState
{
    Pending,
    Approved,
    ApprovedWithSuggestions,  // Azure DevOps vote=5
    WaitingForAuthor,         // Azure DevOps vote=-5
    ChangesRequested,
    Commented,
    Dismissed
}

// ============================================
// PROVIDER-SPECIFIC REVIEW STATE MAPPING
// ============================================
// GitHub states map directly to enum values:
//   "PENDING"           -> Pending
//   "APPROVED"          -> Approved
//   "CHANGES_REQUESTED" -> ChangesRequested
//   "COMMENTED"         -> Commented
//   "DISMISSED"         -> Dismissed
//
// Azure DevOps uses integer votes (must be mapped in adapter):
//   10  -> Approved
//    5  -> ApprovedWithSuggestions
//    0  -> Pending (No vote)
//   -5  -> WaitingForAuthor
//  -10  -> ChangesRequested (Rejected)
//
// See MapAzureDevOpsVote() helper in AzureDevOpsPullRequestService
```

### PullRequestCommentInfo.cs
```csharp
namespace Leaf.Models;

public class PullRequestCommentInfo
{
    public string CommentId { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty; // For Azure DevOps threads
    public string Author { get; set; } = string.Empty;
    public string AuthorAvatarUrl { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? FilePath { get; set; }                // null if general comment
    public int? LineNumber { get; set; }
    public bool IsResolved { get; set; }                 // Azure DevOps thread status

    /// <summary>
    /// Friendly date string for display (follows CommitInfo.DateDisplay pattern).
    /// </summary>
    public string DateDisplay
    {
        get
        {
            var displayDate = UpdatedAt ?? CreatedAt;
            var now = DateTimeOffset.Now;
            var diff = now - displayDate;

            if (diff.TotalMinutes < 1)
                return "Just now";
            if (diff.TotalHours < 1)
                return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalDays < 1)
                return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays}d ago";
            if (displayDate.Year == now.Year)
                return displayDate.ToString("MMM d");
            return displayDate.ToString("MMM d, yyyy");
        }
    }
}
```

### PullRequestStatusCheckInfo.cs
```csharp
namespace Leaf.Models;

public class PullRequestStatusCheckInfo
{
    public string Name { get; set; } = string.Empty;     // Check/pipeline name
    public string Context { get; set; } = string.Empty;  // CI context
    public PullRequestCheckState State { get; set; }
    public string Description { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty; // URL to CI details
    public DateTimeOffset? CompletedAt { get; set; }
}

public enum PullRequestCheckState
{
    Pending,
    Success,
    Failure,
    Error,
    Cancelled
}
```

### PullRequestMergeResult.cs
```csharp
namespace Leaf.Models;

public class PullRequestMergeResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? MergeSha { get; set; }
    public PullRequestMergeError? Error { get; set; }
}

public enum PullRequestMergeMethod
{
    Merge,      // Regular merge commit
    Squash,     // Squash and merge
    Rebase      // Rebase and merge
}

public enum PullRequestMergeError
{
    None,
    NotMergeable,
    ChecksFailed,
    ReviewRequired,
    BranchProtection,
    Unknown
}
```

---

## Services

**Location:** `src/Leaf/Services/`

### 1) Provider Interface

**File:** `src/Leaf/Services/IPullRequestService.cs`

```csharp
namespace Leaf.Services;

public interface IPullRequestService
{
    /// <summary>
    /// Check if the repository remote supports pull requests.
    /// </summary>
    bool IsPullRequestSupported(string repoPath);

    /// <summary>
    /// Get the provider type for the repository.
    /// </summary>
    RemoteType GetProviderType(string repoPath);

    /// <summary>
    /// List pull requests with optional filtering.
    /// </summary>
    Task<List<PullRequestInfo>> ListPullRequestsAsync(
        string repoPath,
        PullRequestStatus state = PullRequestStatus.Open,
        string? author = null,
        string? searchQuery = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get detailed information about a specific PR.
    /// </summary>
    Task<PullRequestInfo?> GetPullRequestAsync(
        string repoPath,
        int prNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get files changed in a PR.
    /// </summary>
    Task<List<PullRequestFileInfo>> GetPullRequestFilesAsync(
        string repoPath,
        int prNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get commits in a PR.
    /// </summary>
    Task<List<CommitInfo>> GetPullRequestCommitsAsync(
        string repoPath,
        int prNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get reviews for a PR.
    /// </summary>
    Task<List<PullRequestReviewInfo>> GetPullRequestReviewsAsync(
        string repoPath,
        int prNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get comments on a PR.
    /// </summary>
    Task<List<PullRequestCommentInfo>> GetPullRequestCommentsAsync(
        string repoPath,
        int prNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get status checks for a PR.
    /// </summary>
    Task<List<PullRequestStatusCheckInfo>> GetPullRequestChecksAsync(
        string repoPath,
        int prNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new pull request.
    /// </summary>
    Task<PullRequestInfo> CreatePullRequestAsync(
        string repoPath,
        string title,
        string body,
        string sourceBranch,
        string targetBranch,
        bool isDraft = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Merge a pull request.
    /// </summary>
    Task<PullRequestMergeResult> MergePullRequestAsync(
        string repoPath,
        int prNumber,
        PullRequestMergeMethod method = PullRequestMergeMethod.Merge,
        string? commitTitle = null,
        string? commitMessage = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Close a pull request without merging.
    /// </summary>
    Task ClosePullRequestAsync(
        string repoPath,
        int prNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a pull request (title, body, draft state).
    /// </summary>
    Task<PullRequestInfo> UpdatePullRequestAsync(
        string repoPath,
        int prNumber,
        string? title = null,           // null = no change
        string? body = null,            // null = no change
        bool? isDraft = null,           // null = no change
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Find pull requests associated with a commit SHA.
    /// Returns PRs where the commit is in the PR's commit list.
    /// </summary>
    Task<List<PullRequestInfo>> FindPullRequestsForCommitAsync(
        string repoPath,
        string commitSha,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checkout a PR locally for review.
    /// </summary>
    Task CheckoutPullRequestAsync(
        string repoPath,
        int prNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the file diff for a PR file.
    /// </summary>
    Task<FileDiffResult?> GetPullRequestFileDiffAsync(
        string repoPath,
        int prNumber,
        string filePath,
        CancellationToken cancellationToken = default);
}
```

### 2) GitHub Implementation

**File:** `src/Leaf/Services/GitHubPullRequestService.cs`

**Pattern:** Follow `GitHubService.cs` for HTTP client usage, auth, and error handling.

**HttpClient Lifecycle:** Register as singleton or inject a shared `IHttpClientFactory` to avoid
socket exhaustion. Do NOT create new HttpClient instances per request.

```csharp
namespace Leaf.Services;

public class GitHubPullRequestService : IGitHubPullRequestService
{
    private readonly HttpClient _httpClient;
    private readonly CredentialService _credentialService;
    private readonly IGitService _gitService;

    // RECOMMENDED: Inject HttpClient via IHttpClientFactory for proper socket management
    // Alternative: Register this service as a singleton so the HttpClient is reused
    public GitHubPullRequestService(
        HttpClient httpClient,  // Injected, not created here
        CredentialService credentialService,
        IGitService gitService)
    {
        _httpClient = httpClient;
        _credentialService = credentialService;
        _gitService = gitService;
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Leaf", "1.0"));
    }

    // API Endpoints (base: https://api.github.com)
    // GET  /repos/{owner}/{repo}/pulls
    // GET  /repos/{owner}/{repo}/pulls/{pull_number}
    // GET  /repos/{owner}/{repo}/pulls/{pull_number}/files
    // GET  /repos/{owner}/{repo}/pulls/{pull_number}/commits
    // GET  /repos/{owner}/{repo}/pulls/{pull_number}/reviews
    // GET  /repos/{owner}/{repo}/pulls/{pull_number}/comments (review comments)
    // GET  /repos/{owner}/{repo}/issues/{issue_number}/comments (issue comments)
    // GET  /repos/{owner}/{repo}/commits/{sha}/check-runs  (see Check-Runs section below)
    // GET  /repos/{owner}/{repo}/commits/{sha}/pulls       (find PRs containing commit)
    // POST /repos/{owner}/{repo}/pulls
    // PUT  /repos/{owner}/{repo}/pulls/{pull_number}/merge
    // PATCH /repos/{owner}/{repo}/pulls/{pull_number}       (update title/body/state - NOT draft)
    // POST /repos/{owner}/{repo}/pulls/{pull_number}/convert_to_draft
    // POST /repos/{owner}/{repo}/pulls/{pull_number}/ready_for_review

    private string? GetToken(string repoPath)
    {
        // Try OAuth token first, then PAT
        // Pattern from GitHubService: _credentialService.GetCredential("GitHub")
        return _credentialService.GetCredential("GitHub");
    }

    private (string owner, string repo)? ParseRemoteUrl(string repoPath)
    {
        // Get origin remote URL from repo
        // Parse: https://github.com/{owner}/{repo}.git
        // Parse: git@github.com:{owner}/{repo}.git
    }

    // Checkout implementation - MUST handle fork PRs:
    // See CheckoutPullRequestAsync section below for full implementation
}
```

### Checkout Implementation (Fork-Aware)

**Problem:** Simple `git fetch origin pull/{number}/head` fails for:
- Fork PRs (head repo is different from base repo)
- Non-origin default remotes
- Private fork repos where user lacks access

**Additional Edge Cases to Handle:**
- **Local branch collision:** `pr-{number}` may already exist from a previous checkout
- **Dirty working directory:** User has uncommitted changes that would be overwritten

**Solution:** Use PR API to get head repo info and fetch from correct remote:

```csharp
public async Task CheckoutPullRequestAsync(string repoPath, int prNumber, CancellationToken ct)
{
    // 0. Pre-flight check: Dirty working directory
    // Git will abort checkout if there are uncommitted changes that would be overwritten.
    // Check proactively to give a better error message.
    if (await _gitService.HasUncommittedChangesAsync(repoPath, ct))
    {
        throw new InvalidOperationException(
            "Cannot checkout PR. You have uncommitted changes. Please stash or commit them first.");
    }

    // 1. Get PR details including head repo info
    var pr = await GetPullRequestAsync(repoPath, prNumber, ct);
    if (pr == null) throw new InvalidOperationException($"PR #{prNumber} not found");

    // 2. Determine fetch source based on head repo
    var headRepoUrl = await GetPrHeadRepoUrlAsync(repoPath, prNumber, ct);
    var baseRemoteName = await GetDefaultRemoteNameAsync(repoPath);  // Usually "origin"

    string fetchRemote;
    bool needsTempRemote = false;

    if (IsSameRepo(headRepoUrl, await GetRemoteUrlAsync(repoPath, baseRemoteName)))
    {
        // Same repo PR - fetch from default remote
        fetchRemote = baseRemoteName;
    }
    else
    {
        // Fork PR - need to fetch from head repo
        // Check if we already have a remote for this URL
        fetchRemote = await FindRemoteByUrlAsync(repoPath, headRepoUrl);

        if (fetchRemote == null)
        {
            // Add temporary remote for the fork
            fetchRemote = $"pr-{prNumber}-temp";
            await _gitService.AddRemoteAsync(repoPath, fetchRemote, headRepoUrl);
            needsTempRemote = true;
        }
    }

    try
    {
        // 3. Handle local branch naming
        var localBranch = $"pr-{prNumber}";
        var headRef = pr.SourceBranch;  // e.g., "refs/heads/feature-branch" or just "feature-branch"

        // Check if local branch already exists
        var existingBranch = await _gitService.GetBranchAsync(repoPath, localBranch, ct);

        if (existingBranch != null)
        {
            // Branch exists - check if it's tracking the correct PR
            var trackingRef = await _gitService.GetTrackingBranchAsync(repoPath, localBranch, ct);

            if (trackingRef != null && IsTrackingPrRef(trackingRef, prNumber, fetchRemote))
            {
                // Same PR - just checkout and pull to update
                await _gitService.CheckoutBranchAsync(repoPath, localBranch);
                await _gitService.PullAsync(repoPath, ct);
                return;
            }
            else
            {
                // Branch exists but tracks something else (edge case)
                // Use alternative name to avoid confusion
                localBranch = $"pr-{prNumber}-leaf";

                // If even that exists, throw helpful error
                if (await _gitService.BranchExistsAsync(repoPath, localBranch, ct))
                {
                    throw new InvalidOperationException(
                        $"Cannot checkout PR #{prNumber}. Local branches 'pr-{prNumber}' and " +
                        $"'pr-{prNumber}-leaf' already exist. Please delete one of them first.");
                }
            }
        }

        // 4. Fetch the PR head ref
        // For GitHub: can also use pull/{number}/head
        // For forks: must use the actual branch name from head repo
        await _gitService.FetchRefAsync(repoPath, fetchRemote, headRef, localBranch);

        // 5. Checkout the local branch
        await _gitService.CheckoutBranchAsync(repoPath, localBranch);
    }
    finally
    {
        // 6. Clean up temp remote if we added one
        if (needsTempRemote)
        {
            await _gitService.RemoveRemoteAsync(repoPath, fetchRemote);
        }
    }
}

/// <summary>
/// Check if the tracking ref corresponds to the PR we're trying to checkout.
/// </summary>
private static bool IsTrackingPrRef(string trackingRef, int prNumber, string remoteName)
{
    // GitHub: refs/remotes/{remote}/pull/{number}/head or refs/pull/{number}/head
    // Azure DevOps: refs/remotes/{remote}/pull/{number}/merge
    return trackingRef.Contains($"pull/{prNumber}/") ||
           trackingRef.Contains($"pr-{prNumber}");
}

// Helper to get head repo URL from PR API
private async Task<string> GetPrHeadRepoUrlAsync(string repoPath, int prNumber, CancellationToken ct)
{
    // GitHub: GET /repos/{owner}/{repo}/pulls/{number} -> head.repo.clone_url
    // Azure DevOps: pullRequest.repository (for same-repo) or sourceRefName parsing
}
```

**Key GitHub API Notes:**
- Use `Accept: application/vnd.github+json` header
- Pagination: `per_page=100`, follow `Link` header
- Rate limits: Check `X-RateLimit-Remaining` header
- Required scopes: `repo` (for private repos), `read:org` (for org repos)
- Draft PRs: `draft: true` in POST body (create only)
- Merge methods: `merge`, `squash`, `rebase` in PUT body
- Update PR: PATCH with `{ title?, body?, state? }` - **NOTE: draft NOT supported via PATCH**

**Draft Toggle (GitHub-specific):**

GitHub does NOT support changing draft state via PATCH. Use dedicated endpoints:
- **Convert to draft:** `POST /repos/{owner}/{repo}/pulls/{pull_number}/convert_to_draft`
- **Mark ready for review:** `POST /repos/{owner}/{repo}/pulls/{pull_number}/ready_for_review`

```csharp
// UpdatePullRequestAsync implementation must handle draft separately
public async Task<PullRequestInfo> UpdatePullRequestAsync(
    string repoPath, int prNumber, string? title, string? body, bool? isDraft, CancellationToken ct)
{
    var (owner, repo) = ParseRemoteUrl(repoPath) ?? throw new InvalidOperationException("Cannot parse remote");

    // 1. Update title/body via PATCH (if provided)
    if (title != null || body != null)
    {
        var patchUrl = $"https://api.github.com/repos/{owner}/{repo}/pulls/{prNumber}";
        var patchBody = new Dictionary<string, string>();
        if (title != null) patchBody["title"] = title;
        if (body != null) patchBody["body"] = body;

        var patchRequest = new HttpRequestMessage(HttpMethod.Patch, patchUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(patchBody), Encoding.UTF8, "application/json")
        };
        patchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        patchRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        var patchResponse = await _httpClient.SendAsync(patchRequest, ct);
        patchResponse.EnsureSuccessStatusCode();
    }

    // 2. Handle draft toggle via dedicated endpoints (if provided)
    if (isDraft.HasValue)
    {
        // Get current PR state to check if toggle is needed
        var currentPr = await GetPullRequestAsync(repoPath, prNumber, ct);
        if (currentPr != null && currentPr.IsDraft != isDraft.Value)
        {
            var draftEndpoint = isDraft.Value
                ? $"https://api.github.com/repos/{owner}/{repo}/pulls/{prNumber}/convert_to_draft"
                : $"https://api.github.com/repos/{owner}/{repo}/pulls/{prNumber}/ready_for_review";

            var draftRequest = new HttpRequestMessage(HttpMethod.Post, draftEndpoint);
            draftRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            draftRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var draftResponse = await _httpClient.SendAsync(draftRequest, ct);
            draftResponse.EnsureSuccessStatusCode();
        }
    }

    // 3. Return updated PR
    return await GetPullRequestAsync(repoPath, prNumber, ct)
        ?? throw new InvalidOperationException($"PR #{prNumber} not found after update");
}
```

**Check-Runs API (for PR status checks):**
```csharp
// IMPORTANT: Must get head SHA from PR, then query check-runs
public async Task<List<PullRequestStatusCheckInfo>> GetPullRequestChecksAsync(
    string repoPath, int prNumber, CancellationToken ct)
{
    // 1. Get PR to obtain head SHA
    var pr = await GetPullRequestAsync(repoPath, prNumber, ct);
    if (pr == null) return [];

    var headSha = await GetPrHeadShaAsync(repoPath, prNumber, ct);

    // 2. Query check-runs for that SHA
    // GET /repos/{owner}/{repo}/commits/{sha}/check-runs
    var url = $"https://api.github.com/repos/{owner}/{repo}/commits/{headSha}/check-runs";

    var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

    // Response: { total_count, check_runs: [{ name, status, conclusion, ... }] }
}

// Get head SHA from PR API response
private async Task<string> GetPrHeadShaAsync(string repoPath, int prNumber, CancellationToken ct)
{
    // GET /repos/{owner}/{repo}/pulls/{number}
    // Response includes: head.sha
}
```

**Find PRs for Commit API:**

**IMPORTANT:** This endpoint requires a preview header. Without it, the API returns 415 or empty results.

```csharp
public async Task<List<PullRequestInfo>> FindPullRequestsForCommitAsync(
    string repoPath, string commitSha, CancellationToken ct)
{
    // GET /repos/{owner}/{repo}/commits/{sha}/pulls
    var url = $"https://api.github.com/repos/{owner}/{repo}/commits/{commitSha}/pulls";

    var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

    // REQUIRED: Preview header for commits/{sha}/pulls endpoint
    // See: https://docs.github.com/en/rest/commits/commits#list-pull-requests-associated-with-a-commit
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.groot-preview+json"));

    // Returns array of PR objects
    // Note: Only returns PRs where commit is directly in the PR branch
    // May not find PRs if commit was squash-merged
}
```

### 3) Azure DevOps Implementation

**File:** `src/Leaf/Services/AzureDevOpsPullRequestService.cs`

**Pattern:** Follow `AzureDevOpsService.cs` for Basic auth with PAT.

**HttpClient Lifecycle:** Same as GitHub service—inject HttpClient or use IHttpClientFactory.
Register as singleton to reuse connections and avoid socket exhaustion.

**Iterations API:** When loading files/diffs, call the PR iterations endpoint to get the latest
iteration, then use that iteration's `sourceRefCommit` and `commonRefCommit` for accurate
file content instead of relying on branch head (which may have changed since PR creation).

```csharp
namespace Leaf.Services;

public class AzureDevOpsPullRequestService : IAzureDevOpsPullRequestService
{
    private readonly HttpClient _httpClient;
    private readonly CredentialService _credentialService;
    private readonly IGitService _gitService;

    // RECOMMENDED: Inject HttpClient for proper lifecycle management
    public AzureDevOpsPullRequestService(
        HttpClient httpClient,
        CredentialService credentialService,
        IGitService gitService)
    {
        _httpClient = httpClient;
        _credentialService = credentialService;
        _gitService = gitService;
    }

    // Base URL: https://dev.azure.com/{organization}/{project}/_apis/git/repositories/{repositoryId}
    // API version: api-version=7.0

    // API Endpoints:
    // GET  /pullrequests
    // GET  /pullrequests/{pullRequestId}
    // GET  /pullrequests/{pullRequestId}/commits
    // GET  /pullrequests/{pullRequestId}/iterations (for files)
    // GET  /pullrequests/{pullRequestId}/threads (comments)
    // GET  /pullrequests/{pullRequestId}/statuses
    // POST /pullrequests
    // PATCH /pullrequests/{pullRequestId} (update, complete, abandon)

    private string? GetToken(string repoPath)
    {
        // Pattern from AzureDevOpsService: _credentialService.GetCredential("AzureDevOps")
        return _credentialService.GetCredential("AzureDevOps");
    }

    private (string org, string project, string repo)? ParseRemoteUrl(string repoPath)
    {
        // Parse: https://dev.azure.com/{org}/{project}/_git/{repo}
        // Parse: https://{org}.visualstudio.com/{project}/_git/{repo}
        // Parse: {org}@vs-ssh.visualstudio.com:v3/{org}/{project}/{repo}
    }

    // Checkout implementation:
    // Azure DevOps PRs are typically same-repo (forks less common in enterprise)
    // Use the fork-aware CheckoutPullRequestAsync pattern from GitHub section
    //
    // Azure DevOps refs:
    //   refs/pull/{id}/merge - merge commit (if auto-complete enabled)
    //   refs/pull/{id}/head  - PR head commit
    //
    // For cross-repo PRs (forks), use sourceRefName from PR API and
    // fetch from the fork repository URL (forkSource.repository.remoteUrl)

    /// <summary>
    /// Map Azure DevOps vote integer to unified PullRequestReviewState.
    /// Azure DevOps uses integers instead of named states like GitHub.
    /// This mapping ensures the UI doesn't need to know provider differences.
    /// </summary>
    private static PullRequestReviewState MapAzureDevOpsVote(int vote) => vote switch
    {
        10 => PullRequestReviewState.Approved,
        5 => PullRequestReviewState.ApprovedWithSuggestions,
        0 => PullRequestReviewState.Pending,           // No vote
        -5 => PullRequestReviewState.WaitingForAuthor,
        -10 => PullRequestReviewState.ChangesRequested, // Rejected
        _ => PullRequestReviewState.Pending            // Unknown vote value
    };

    /// <summary>
    /// Parse reviewer from Azure DevOps PR response.
    /// </summary>
    private PullRequestReviewInfo ParseAzureDevOpsReviewer(JsonElement reviewer)
    {
        return new PullRequestReviewInfo
        {
            ReviewId = reviewer.GetProperty("id").GetString() ?? string.Empty,
            Reviewer = reviewer.GetProperty("displayName").GetString() ?? string.Empty,
            ReviewerAvatarUrl = reviewer.TryGetProperty("imageUrl", out var img)
                ? img.GetString() ?? string.Empty
                : string.Empty,
            State = MapAzureDevOpsVote(reviewer.GetProperty("vote").GetInt32()),
            // Azure DevOps doesn't have per-review timestamps in the same way
            SubmittedAt = DateTimeOffset.UtcNow,
            Body = string.Empty // Vote doesn't include body; comments are separate
        };
    }
}
```

**Key Azure DevOps API Notes:**
- Auth: Basic with empty username and PAT as password
- Use `Content-Type: application/json`
- Response wrapper: `{ count: N, value: [...] }`
- Merge: Use PATCH with `status: "completed"` and `completionOptions`
- Thread resolution: Update thread with `status: "fixed"`

### Patch to FileDiffResult Conversion

The GitHub API returns file changes with a `patch` field containing unified diff format.
Azure DevOps requires fetching file contents via iterations API.

**Conversion Strategy:**

**Option 1: Parse patch directly (GitHub):**

**Note:** Preserve hunk headers for accurate line number mapping and navigation.
The existing `DiffLine` model may need extension for line numbers.

```csharp
// Add to DiffService or create PatchParser utility
public FileDiffResult ParseUnifiedDiff(string patch, string fileName, string filePath)
{
    var lines = new List<DiffLine>();
    var patchLines = patch.Split('\n');

    // Track line numbers for navigation
    int oldLineNum = 0;
    int newLineNum = 0;

    foreach (var line in patchLines)
    {
        if (line.StartsWith("@@"))
        {
            // Parse hunk header: @@ -oldStart,oldCount +newStart,newCount @@
            // Example: @@ -10,7 +10,8 @@ optional context
            var hunkInfo = ParseHunkHeader(line);
            if (hunkInfo != null)
            {
                oldLineNum = hunkInfo.Value.oldStart;
                newLineNum = hunkInfo.Value.newStart;

                // Add hunk header as a special line type for navigation
                lines.Add(new DiffLine
                {
                    Type = DiffLineType.Hunk,
                    Content = line,
                    OldLineNumber = null,
                    NewLineNumber = null,
                    HunkOldStart = hunkInfo.Value.oldStart,
                    HunkNewStart = hunkInfo.Value.newStart
                });
            }
            continue;
        }

        if (line.Length == 0)
        {
            // Empty context line
            lines.Add(new DiffLine
            {
                Type = DiffLineType.Unchanged,
                Content = string.Empty,
                OldLineNumber = oldLineNum++,
                NewLineNumber = newLineNum++
            });
            continue;
        }

        var prefix = line[0];
        var content = line.Length > 1 ? line[1..] : string.Empty;

        var diffLine = prefix switch
        {
            '+' => new DiffLine
            {
                Type = DiffLineType.Added,
                Content = content,
                OldLineNumber = null,           // No old line for additions
                NewLineNumber = newLineNum++
            },
            '-' => new DiffLine
            {
                Type = DiffLineType.Deleted,
                Content = content,
                OldLineNumber = oldLineNum++,
                NewLineNumber = null            // No new line for deletions
            },
            ' ' => new DiffLine
            {
                Type = DiffLineType.Unchanged,
                Content = content,
                OldLineNumber = oldLineNum++,
                NewLineNumber = newLineNum++
            },
            '\\' => new DiffLine
            {
                Type = DiffLineType.Meta,       // "No newline at end of file"
                Content = content,
                OldLineNumber = null,
                NewLineNumber = null
            },
            _ => new DiffLine
            {
                Type = DiffLineType.Unchanged,
                Content = line,
                OldLineNumber = oldLineNum++,
                NewLineNumber = newLineNum++
            }
        };

        lines.Add(diffLine);
    }

    return new FileDiffResult
    {
        FileName = fileName,
        FilePath = filePath,
        Lines = lines,
        InlineContent = string.Join('\n', lines
            .Where(l => l.Type != DiffLineType.Hunk && l.Type != DiffLineType.Meta)
            .Select(l => l.Content)),
        LinesAddedCount = lines.Count(l => l.Type == DiffLineType.Added),
        LinesDeletedCount = lines.Count(l => l.Type == DiffLineType.Deleted)
    };
}

/// <summary>
/// Parse unified diff hunk header.
/// Format: @@ -oldStart,oldCount +newStart,newCount @@ optional context
/// </summary>
private static (int oldStart, int oldCount, int newStart, int newCount)? ParseHunkHeader(string line)
{
    // Regex: @@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@
    var match = Regex.Match(line, @"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@");
    if (!match.Success)
        return null;

    return (
        oldStart: int.Parse(match.Groups[1].Value),
        oldCount: match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1,
        newStart: int.Parse(match.Groups[3].Value),
        newCount: match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 1
    );
}
```

**DiffLine Model Extension:**

The existing `DiffLine` model may need these additional properties:

```csharp
// In src/Leaf/Models/DiffLine.cs - add if not present
public class DiffLine
{
    public DiffLineType Type { get; set; }
    public string Content { get; set; } = string.Empty;

    // Line numbers for navigation (null for hunks/meta lines)
    public int? OldLineNumber { get; set; }
    public int? NewLineNumber { get; set; }

    // Hunk info (only set for Type == Hunk)
    public int? HunkOldStart { get; set; }
    public int? HunkNewStart { get; set; }
}

public enum DiffLineType
{
    Unchanged,
    Added,
    Deleted,
    Modified,   // Existing
    Imaginary,  // Existing
    Hunk,       // NEW: Hunk header line
    Meta        // NEW: Meta info like "No newline at end"
}
```

**Option 2: Fetch file contents and use DiffService (Azure DevOps or fallback):**
```csharp
public async Task<FileDiffResult?> GetPullRequestFileDiffAsync(
    string repoPath, int prNumber, string filePath, CancellationToken ct)
{
    // If patch is available (GitHub), use Option 1
    var fileInfo = await GetFileInfoAsync(repoPath, prNumber, filePath, ct);
    if (!string.IsNullOrEmpty(fileInfo?.Patch))
    {
        return ParseUnifiedDiff(fileInfo.Patch, fileInfo.FileName, filePath);
    }

    // Otherwise, fetch base and head file contents
    var baseContent = await FetchFileContentAsync(repoPath, prNumber, filePath, isBase: true, ct);
    var headContent = await FetchFileContentAsync(repoPath, prNumber, filePath, isBase: false, ct);

    // Use existing DiffService.ComputeDiff()
    return _diffService.ComputeDiff(baseContent, headContent,
        System.IO.Path.GetFileName(filePath), filePath);
}
```

**Recommended:** Implement both options. Use patch parsing for GitHub (faster, no extra API calls),
and content-based diff for Azure DevOps (which doesn't provide patches in the same format).

### 4) PR Router Service

**File:** `src/Leaf/Services/PullRequestService.cs`

**DI Pattern:** Inject provider services rather than instantiating internally. This enables:
- Unit testing with mocked providers
- Single source of truth for service lifetimes
- Consistent with existing Leaf service patterns

```csharp
namespace Leaf.Services;

public class PullRequestService : IPullRequestService
{
    private readonly IGitHubPullRequestService _githubService;
    private readonly IAzureDevOpsPullRequestService _azureService;
    private readonly IGitService _gitService;
    private readonly DiffService _diffService;

    // Constructor injection for all dependencies
    public PullRequestService(
        IGitHubPullRequestService githubService,
        IAzureDevOpsPullRequestService azureService,
        IGitService gitService,
        DiffService diffService)
    {
        _githubService = githubService;
        _azureService = azureService;
        _gitService = gitService;
        _diffService = diffService;
    }

    public bool IsPullRequestSupported(string repoPath)
    {
        var (provider, _) = GetProviderAndRemote(repoPath);
        return provider == RemoteType.GitHub || provider == RemoteType.AzureDevOps;
    }

    public RemoteType GetProviderType(string repoPath)
    {
        var (provider, _) = GetProviderAndRemote(repoPath);
        return provider;
    }

    /// <summary>
    /// Determines which PR provider to use and which remote to target.
    /// Returns (RemoteType, remoteName) tuple.
    /// </summary>
    private (RemoteType provider, string remoteName) GetProviderAndRemote(string repoPath)
    {
        var remotes = _gitService.GetRemotesAsync(repoPath).GetAwaiter().GetResult();

        // Provider selection rules (in priority order):
        //
        // 1. Prefer "origin" remote if it's a supported provider
        // 2. Fall back to first supported remote found
        // 3. Return (Other, null) if no supported remotes

        string? selectedRemote = null;
        RemoteType selectedProvider = RemoteType.Other;

        foreach (var remote in remotes)
        {
            var provider = DetectProviderFromUrl(remote.Url);

            if (provider != RemoteType.Other)
            {
                // First supported remote found
                if (selectedRemote == null)
                {
                    selectedRemote = remote.Name;
                    selectedProvider = provider;
                }

                // Prefer "origin" over other remotes
                if (string.Equals(remote.Name, "origin", StringComparison.OrdinalIgnoreCase))
                {
                    return (provider, remote.Name);
                }
            }
        }

        return (selectedProvider, selectedRemote ?? string.Empty);
    }

    /// <summary>
    /// Detects provider type from remote URL.
    /// Supports GitHub.com, GitHub Enterprise, Azure DevOps, and Azure DevOps Server.
    /// </summary>
    private static RemoteType DetectProviderFromUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return RemoteType.Other;

        // GitHub detection (includes GitHub Enterprise)
        // - github.com (public)
        // - github.{company}.com or {company}.github.com (Enterprise Cloud)
        // - Custom domains with /api/v3/ path (Enterprise Server)
        if (url.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("github.", StringComparison.OrdinalIgnoreCase) ||
            IsGitHubEnterpriseUrl(url))
        {
            return RemoteType.GitHub;
        }

        // Azure DevOps detection
        // - dev.azure.com/{org} (modern)
        // - {org}.visualstudio.com (legacy)
        // - vs-ssh.visualstudio.com (SSH)
        // - Azure DevOps Server on-prem (custom domains with _git in path)
        if (url.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("/_git/", StringComparison.OrdinalIgnoreCase))
        {
            return RemoteType.AzureDevOps;
        }

        return RemoteType.Other;
    }

    /// <summary>
    /// Heuristic for GitHub Enterprise Server detection.
    /// Checks for common GHE patterns when github.com isn't in the URL.
    /// </summary>
    private static bool IsGitHubEnterpriseUrl(string url)
    {
        // Common GHE URL patterns:
        // - https://github.mycompany.com/owner/repo
        // - git@github.mycompany.com:owner/repo
        // GHE URLs typically have 'github' in subdomain or path structure owner/repo
        // This is a heuristic - could be improved with explicit GHE host configuration

        // Look for git@ prefix with github in hostname
        if (url.StartsWith("git@", StringComparison.OrdinalIgnoreCase) &&
            url.Contains("github", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    // Delegate all methods to appropriate provider based on GetProviderType()
    // Use GetProviderAndRemote() to also get the correct remote name for API calls
}
```

### 5) Service Registration (App.xaml.cs)

Add to the service registration in `App.xaml.cs`:

```csharp
// In App.xaml.cs constructor or OnStartup
var credentialService = new CredentialService();
var gitService = new GitService();
var diffService = new DiffService();

// PR provider services
var githubPrService = new GitHubPullRequestService(credentialService, gitService, diffService);
var azurePrService = new AzureDevOpsPullRequestService(credentialService, gitService, diffService);

// Composite PR service
var pullRequestService = new PullRequestService(
    githubPrService,
    azurePrService,
    gitService,
    diffService);

// Pass to MainViewModel
var mainViewModel = new MainViewModel(
    gitService,
    credentialService,
    settingsService,
    gitFlowService,
    repositoryService,
    autoFetchService,
    pullRequestService,  // Add this parameter
    mainWindow);
```

---

## ViewModels

**Location:** `src/Leaf/ViewModels/`

### PullRequestViewModel.cs

**Pattern:** Follow `DiffViewerViewModel.cs` for mode switching, cancellation, and loading states.

```csharp
namespace Leaf.ViewModels;

public partial class PullRequestViewModel : ObservableObject
{
    private readonly IPullRequestService _prService;
    private readonly IGitService _gitService;
    private CancellationTokenSource? _loadCts;

    public PullRequestViewModel(
        IPullRequestService prService,
        IGitService gitService)
    {
        _prService = prService;
        _gitService = gitService;
    }

    // Repository context
    [ObservableProperty]
    private string _repositoryPath = string.Empty;

    [ObservableProperty]
    private RemoteType _providerType;

    [ObservableProperty]
    private bool _isPullRequestSupported;

    // PR List
    [ObservableProperty]
    private ObservableCollection<PullRequestInfo> _pullRequests = [];

    [ObservableProperty]
    private PullRequestStatus _filterStatus = PullRequestStatus.Open;

    [ObservableProperty]
    private string _searchText = string.Empty;

    // Selected PR Details
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPullRequest))]
    private PullRequestInfo? _selectedPullRequest;

    public bool HasSelectedPullRequest => SelectedPullRequest != null;

    [ObservableProperty]
    private ObservableCollection<PullRequestFileInfo> _selectedPrFiles = [];

    [ObservableProperty]
    private ObservableCollection<CommitInfo> _selectedPrCommits = [];

    [ObservableProperty]
    private ObservableCollection<PullRequestReviewInfo> _selectedPrReviews = [];

    [ObservableProperty]
    private ObservableCollection<PullRequestCommentInfo> _selectedPrComments = [];

    [ObservableProperty]
    private ObservableCollection<PullRequestStatusCheckInfo> _selectedPrChecks = [];

    // Loading states
    [ObservableProperty]
    private bool _isLoadingList;

    [ObservableProperty]
    private bool _isLoadingDetails;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    // Commands
    [RelayCommand]
    private async Task RefreshAsync() { }

    [RelayCommand]
    private async Task LoadPullRequestDetailsAsync(PullRequestInfo pr) { }

    [RelayCommand]
    private async Task CheckoutPullRequestAsync()
    {
        if (SelectedPullRequest == null || string.IsNullOrEmpty(RepositoryPath))
            return;

        try
        {
            IsLoadingDetails = true;
            StatusMessage = $"Checking out PR #{SelectedPullRequest.Number}...";
            ErrorMessage = null;

            await _prService.CheckoutPullRequestAsync(
                RepositoryPath,
                SelectedPullRequest.Number);

            StatusMessage = $"Checked out PR #{SelectedPullRequest.Number}";
            GraphRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (InvalidOperationException ex)
        {
            // User-friendly errors from our service (dirty working dir, branch collision)
            ErrorMessage = ex.Message;
            StatusMessage = "Checkout failed";
        }
        catch (Exception ex)
        {
            // Generic Git errors - extract meaningful message
            var message = ex.Message;

            // Improve common Git error messages
            if (message.Contains("exit code 1") || message.Contains("would be overwritten"))
            {
                message = "Cannot checkout PR. You have uncommitted changes. " +
                          "Please stash or commit them first.";
            }
            else if (message.Contains("already exists"))
            {
                message = $"Branch 'pr-{SelectedPullRequest.Number}' already exists. " +
                          "Delete it manually or use a different branch name.";
            }

            ErrorMessage = message;
            StatusMessage = "Checkout failed";
        }
        finally
        {
            IsLoadingDetails = false;
        }
    }

    [RelayCommand]
    private async Task MergePullRequestAsync(PullRequestMergeMethod method) { }

    [RelayCommand]
    private async Task ClosePullRequestAsync() { }

    [RelayCommand]
    private async Task UpdatePullRequestAsync(string? title, string? body, bool? isDraft) { }

    [RelayCommand]
    private void OpenInBrowser()
    {
        if (SelectedPullRequest?.Url != null)
            Process.Start(new ProcessStartInfo(SelectedPullRequest.Url) { UseShellExecute = true });
    }

    [RelayCommand]
    private void CopyPrUrl()
    {
        if (SelectedPullRequest?.Url != null)
            Clipboard.SetText(SelectedPullRequest.Url);
    }

    // Event for opening file diff (handled by MainViewModel)
    public event EventHandler<PullRequestFileInfo>? OpenFileDiffRequested;

    // Event for graph refresh after merge/checkout (handled by MainViewModel)
    public event EventHandler? GraphRefreshRequested;

    // Cancellation pattern from DiffViewerViewModel
    private CancellationToken ResetActiveLoad()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        return _loadCts.Token;
    }

    public void SetRepository(string repoPath)
    {
        RepositoryPath = repoPath;
        ProviderType = _prService.GetProviderType(repoPath);
        IsPullRequestSupported = _prService.IsPullRequestSupported(repoPath);

        if (IsPullRequestSupported)
        {
            _ = RefreshAsync();
        }
    }
}
```

### MainViewModel Integration

**File:** `src/Leaf/ViewModels/MainViewModel.cs`

Add to existing class:

```csharp
// Add to fields
private readonly IPullRequestService _pullRequestService;

[ObservableProperty]
private PullRequestViewModel? _pullRequestViewModel;

[ObservableProperty]
private bool _isPullRequestPanelVisible;

// Update constructor signature to accept IPullRequestService
public MainViewModel(
    IGitService gitService,
    CredentialService credentialService,
    SettingsService settingsService,
    IGitFlowService gitFlowService,
    IRepositoryManagementService repositoryService,
    IAutoFetchService autoFetchService,
    IPullRequestService pullRequestService,  // Add this
    Window ownerWindow)
{
    // ... existing initialization ...
    _pullRequestService = pullRequestService;

    // Create PullRequestViewModel with injected service (after _diffViewerViewModel, line ~272)
    _pullRequestViewModel = new PullRequestViewModel(_pullRequestService, gitService);
    _pullRequestViewModel.OpenFileDiffRequested += OnPrFileDiffRequested;
    _pullRequestViewModel.GraphRefreshRequested += OnPrGraphRefreshRequested;
}

// Add event handler for file diff
private async void OnPrFileDiffRequested(object? sender, PullRequestFileInfo file)
{
    if (SelectedRepository == null || DiffViewerViewModel == null || PullRequestViewModel == null)
        return;

    var prNumber = PullRequestViewModel.SelectedPullRequest?.Number ?? 0;
    if (prNumber == 0) return;

    DiffViewerViewModel.IsLoading = true;
    IsDiffViewerVisible = true;

    try
    {
        var diffResult = await _pullRequestService.GetPullRequestFileDiffAsync(
            SelectedRepository.Path, prNumber, file.Path);

        if (diffResult != null)
        {
            DiffViewerViewModel.RepositoryPath = SelectedRepository.Path;
            DiffViewerViewModel.LoadDiff(diffResult);
        }
    }
    catch (Exception ex)
    {
        StatusMessage = $"Failed to load PR diff: {ex.Message}";
        IsDiffViewerVisible = false;
    }
    finally
    {
        DiffViewerViewModel.IsLoading = false;
    }
}

// Add event handler for graph refresh after PR operations (merge, checkout)
// IMPORTANT: async void handlers MUST wrap in try/catch to prevent UI-thread crashes
private async void OnPrGraphRefreshRequested(object? sender, EventArgs e)
{
    try
    {
        // Use existing refresh pattern - FileWatcherService will detect .git changes,
        // but we trigger explicit refresh for immediate feedback
        if (SelectedRepository != null && GitGraphViewModel != null)
        {
            await GitGraphViewModel.LoadCommitsAsync(SelectedRepository.Path);
            await RefreshBranchesAsync();
        }
    }
    catch (Exception ex)
    {
        StatusMessage = $"Failed to refresh after PR operation: {ex.Message}";
    }
}

// Add to OnSelectedRepositoryChanged (line ~212)
PullRequestViewModel?.SetRepository(value?.Path ?? string.Empty);

// Add command to toggle PR panel visibility
[RelayCommand]
private void TogglePullRequestPanel()
{
    IsPullRequestPanelVisible = !IsPullRequestPanelVisible;
}

// Add commands for branch context menu
[RelayCommand]
private void CreatePullRequestFromBranch(BranchInfo branch)
{
    // Open CreatePullRequestDialog with source branch pre-selected
}

// Add command for commit context menu - Find PR for commit
[RelayCommand]
private async Task FindPullRequestForCommitAsync(CommitInfo commit)
{
    if (SelectedRepository == null || commit == null)
        return;

    try
    {
        IsBusy = true;
        StatusMessage = $"Finding PRs for {commit.ShortSha}...";

        var prs = await _pullRequestService.FindPullRequestsForCommitAsync(
            SelectedRepository.Path,
            commit.Sha);

        if (prs.Count == 0)
        {
            StatusMessage = $"No pull requests found for commit {commit.ShortSha}";
            return;
        }

        if (prs.Count == 1)
        {
            // Single PR - select it directly
            IsPullRequestPanelVisible = true;
            PullRequestViewModel?.SelectPullRequest(prs[0]);
            StatusMessage = $"Found PR #{prs[0].Number}";
        }
        else
        {
            // Multiple PRs - show panel with filtered list
            IsPullRequestPanelVisible = true;
            PullRequestViewModel?.ShowPullRequestsForCommit(prs, commit.ShortSha);
            StatusMessage = $"Found {prs.Count} PRs for commit {commit.ShortSha}";
        }
    }
    catch (Exception ex)
    {
        StatusMessage = $"Failed to find PRs: {ex.Message}";
    }
    finally
    {
        IsBusy = false;
    }
}
```

---

## UI/UX

### Design Decision: Separate Panel (Not a DiffViewer Mode)

PRs have a fundamentally different UI pattern than Diff/Blame/History:
- **Diff/Blame/History:** Single-file focused, same content area with different visualizations
- **PRs:** List + details pattern, multiple files, reviews, comments, actions

Therefore, we use a **separate `PullRequestPanel`** that:
- Lives alongside (not inside) the DiffViewer
- Can be toggled via toolbar button or keyboard shortcut
- Opens the DiffViewer when clicking on a PR file

**MainWindow Layout Integration:**

```xml
<!-- In MainWindow.xaml, add PR panel toggle and visibility -->
<Grid>
    <!-- Existing layout -->
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto" /> <!-- Repo/Branch panel -->
        <ColumnDefinition Width="*" />    <!-- Graph + Details -->
    </Grid.ColumnDefinitions>

    <!-- Right panel: either CommitDetail or PullRequestPanel -->
    <Grid Grid.Column="1">
        <!-- Graph view -->
        <views:GitGraphView ... />

        <!-- Commit detail (when PR panel hidden) -->
        <views:CommitDetailView
            Visibility="{Binding IsPullRequestPanelVisible, Converter={StaticResource InverseBoolToVisibilityConverter}}" />

        <!-- PR panel (when visible) -->
        <controls:PullRequestPanel
            DataContext="{Binding PullRequestViewModel}"
            Visibility="{Binding IsPullRequestPanelVisible, Converter={StaticResource BoolToVisibilityConverter}}" />

        <!-- DiffViewer overlays both -->
        <controls:DiffViewerControl
            Visibility="{Binding IsDiffViewerVisible, Converter={StaticResource BoolToVisibilityConverter}}" />
    </Grid>
</Grid>
```

### 1) PullRequestPanel.xaml (New File)

**Location:** `src/Leaf/Controls/PullRequestPanel.xaml`

```xml
<UserControl x:Class="Leaf.Controls.PullRequestPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             ...>
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="350" MinWidth="250" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>

        <!-- Left: PR List -->
        <Border Grid.Column="0" Background="#1C2024">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" /> <!-- Header/Filters -->
                    <RowDefinition Height="*" />    <!-- List -->
                </Grid.RowDefinitions>

                <!-- Filter bar -->
                <StackPanel Grid.Row="0" Margin="12">
                    <ComboBox ItemsSource="{Binding StatusFilters}"
                              SelectedItem="{Binding FilterStatus}" />
                    <TextBox Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}"
                             Margin="0,8,0,0" />
                </StackPanel>

                <!-- PR List (follow CommitDetailView pattern for ItemsControl) -->
                <ListBox Grid.Row="1"
                         ItemsSource="{Binding PullRequests}"
                         SelectedItem="{Binding SelectedPullRequest}"
                         VirtualizingPanel.IsVirtualizing="True">
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <!-- PR item: #123 title, author avatar, draft badge, date -->
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </Grid>
        </Border>

        <!-- Splitter -->
        <GridSplitter Grid.Column="1" Width="4" />

        <!-- Right: PR Details -->
        <Border Grid.Column="2" Background="#202428">
            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <StackPanel Margin="16" Visibility="{Binding HasSelectedPullRequest, Converter={...}}">
                    <!-- Title, author, branches -->
                    <TextBlock Text="{Binding SelectedPullRequest.Title}"
                               FontSize="18" FontWeight="SemiBold" />

                    <!-- Branch info: source -> target -->
                    <StackPanel Orientation="Horizontal" Margin="0,8">
                        <Border Background="#22342A" CornerRadius="4" Padding="6,2">
                            <TextBlock Text="{Binding SelectedPullRequest.SourceBranchName}" />
                        </Border>
                        <TextBlock Text="→" Margin="8,0" />
                        <Border Background="#2D3A47" CornerRadius="4" Padding="6,2">
                            <TextBlock Text="{Binding SelectedPullRequest.TargetBranchName}" />
                        </Border>
                    </StackPanel>

                    <!-- Status checks -->
                    <ItemsControl ItemsSource="{Binding SelectedPrChecks}" />

                    <!-- Reviewers -->
                    <ItemsControl ItemsSource="{Binding SelectedPrReviews}" />

                    <!-- Description (with Markdown rendering) -->
                    <!-- Option A (preferred): Use MdXaml or Markdig.Wpf for full markdown -->
                    <!-- Option B (fallback): Use MarkdownToPlainText converter for basic readability -->
                    <!-- See Markdown Rendering section below for implementation details -->
                    <controls:MarkdownViewer
                        Markdown="{Binding SelectedPullRequest.Body}"
                        Margin="0,16" />
                    <!-- Fallback if markdown not implemented yet:
                    <TextBlock Text="{Binding SelectedPullRequest.Body, Converter={StaticResource MarkdownToPlainTextConverter}}"
                               TextWrapping="Wrap" Margin="0,16" />
                    -->

                    <!-- File list (click to open diff) -->
                    <!-- Supports flat list or directory-grouped TreeView (toggle via toolbar button) -->
                    <Expander Header="Changed Files" IsExpanded="True">
                        <Grid>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="*" />
                            </Grid.RowDefinitions>

                            <!-- View toggle: flat list vs tree -->
                            <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,8">
                                <ToggleButton Content="List" IsChecked="{Binding IsFileListFlat}" />
                                <ToggleButton Content="Tree" IsChecked="{Binding IsFileListTree}" Margin="4,0,0,0" />
                            </StackPanel>

                            <!-- Flat list view -->
                            <ListBox Grid.Row="1"
                                     ItemsSource="{Binding SelectedPrFiles}"
                                     SelectionChanged="OnFileSelected"
                                     Visibility="{Binding IsFileListFlat, Converter={StaticResource BoolToVisibilityConverter}}">
                                <!-- File item: status icon, path, +/-stats -->
                            </ListBox>

                            <!-- Tree view (directory-grouped) -->
                            <TreeView Grid.Row="1"
                                      ItemsSource="{Binding SelectedPrFilesTree}"
                                      Visibility="{Binding IsFileListTree, Converter={StaticResource BoolToVisibilityConverter}}">
                                <TreeView.ItemTemplate>
                                    <HierarchicalDataTemplate ItemsSource="{Binding Children}">
                                        <!-- PullRequestFileNode: IsDirectory, Name, File (if leaf) -->
                                        <TextBlock Text="{Binding DisplayName}" />
                                    </HierarchicalDataTemplate>
                                </TreeView.ItemTemplate>
                            </TreeView>
                        </Grid>
                    </Expander>

                    <!-- Commits -->
                    <Expander Header="Commits">
                        <ItemsControl ItemsSource="{Binding SelectedPrCommits}" />
                    </Expander>

                    <!-- Actions -->
                    <StackPanel Orientation="Horizontal" Margin="0,16">
                        <Button Content="Checkout" Command="{Binding CheckoutPullRequestCommand}" />
                        <Button Content="Merge" Margin="8,0" Command="{Binding MergePullRequestCommand}" />
                        <Button Content="Close" Margin="8,0" Command="{Binding ClosePullRequestCommand}" />
                        <Button Content="Open in Browser" Margin="8,0" Command="{Binding OpenInBrowserCommand}" />
                    </StackPanel>
                </StackPanel>
            </ScrollViewer>
        </Border>
    </Grid>
</UserControl>
```

### 3) CreatePullRequestDialog.xaml (New File)

**Location:** `src/Leaf/Views/CreatePullRequestDialog.xaml`

**Pattern:** Follow `MergeDialog.xaml` and `StartBranchDialog.xaml` for dialog structure.

```xml
<Window x:Class="Leaf.Views.CreatePullRequestDialog"
        Title="Create Pull Request"
        Width="550" Height="500"
        WindowStartupLocation="CenterOwner"
        ...>
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" /> <!-- Title -->
            <RowDefinition Height="Auto" /> <!-- Branches -->
            <RowDefinition Height="*" />    <!-- Description -->
            <RowDefinition Height="Auto" /> <!-- Options -->
            <RowDefinition Height="Auto" /> <!-- Buttons -->
        </Grid.RowDefinitions>

        <!-- Title -->
        <TextBox Grid.Row="0"
                 Text="{Binding Title, UpdateSourceTrigger=PropertyChanged}"
                 x:Name="TitleBox" />

        <!-- Branch selection -->
        <Grid Grid.Row="1" Margin="0,16">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>

            <ComboBox Grid.Column="0"
                      ItemsSource="{Binding SourceBranches}"
                      SelectedItem="{Binding SelectedSourceBranch}"
                      DisplayMemberPath="Name" />
            <TextBlock Grid.Column="1" Text="→" Margin="12,0" />
            <ComboBox Grid.Column="2"
                      ItemsSource="{Binding TargetBranches}"
                      SelectedItem="{Binding SelectedTargetBranch}"
                      DisplayMemberPath="Name" />
        </Grid>

        <!-- Description -->
        <TextBox Grid.Row="2"
                 Text="{Binding Description}"
                 AcceptsReturn="True"
                 TextWrapping="Wrap"
                 VerticalScrollBarVisibility="Auto" />

        <!-- Options -->
        <StackPanel Grid.Row="3" Margin="0,16">
            <CheckBox Content="Create as draft" IsChecked="{Binding IsDraft}" />
        </StackPanel>

        <!-- Validation message -->
        <TextBlock Grid.Row="3"
                   Text="{Binding ValidationError}"
                   Foreground="#E81123"
                   Visibility="{Binding HasValidationError, Converter={...}}" />

        <!-- Buttons -->
        <StackPanel Grid.Row="4" Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="Cancel" IsCancel="True" Width="80" />
            <Button Content="Create PR" IsDefault="True" Width="100" Margin="8,0,0,0"
                    Command="{Binding CreateCommand}"
                    IsEnabled="{Binding CanCreate}" />
        </StackPanel>
    </Grid>
</Window>
```

### 4) MergePullRequestDialog.xaml (New File)

```xml
<!-- Similar dialog for merge method selection -->
<!-- Options: Merge, Squash, Rebase (if supported by provider) -->
<!-- Show warnings for failing checks, missing approvals -->
```

### 5) Context Menu Integration

**File:** `src/Leaf/Views/BranchListView.xaml`

Add to branch context menu (after "Rename Branch" menu item, ~line 321):

```xml
<Separator />
<MenuItem Header="Create Pull Request..."
          Command="{Binding PlacementTarget.Tag.CreatePullRequestFromBranchCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
          CommandParameter="{Binding}"
          IsEnabled="{Binding PlacementTarget.Tag.IsPullRequestSupported, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
```

**File:** `src/Leaf/Views/GitGraphView.xaml.cs`

Add to commit context menu (in `CommitItem_ContextMenuOpening`, ~line 383):

```csharp
// Add after existing menu items
var findPrMenuItem = new MenuItem
{
    Header = "Find Pull Request...",
    Command = viewModel.FindPullRequestForCommitCommand,
    CommandParameter = commit
};
menu.Items.Add(new Separator());
menu.Items.Add(findPrMenuItem);
```

### 6) Markdown Rendering

**Location:** `src/Leaf/Controls/MarkdownViewer.xaml` or converter

PR body and comments contain GitHub/Azure DevOps markdown. Render properly for readability.

**Option A: Full Markdown Renderer (Recommended)**

Use MdXaml or Markdig.Wpf for rich rendering:

```xml
<!-- NuGet: MdXaml or Markdig.Wpf -->
<mdxaml:MarkdownScrollViewer
    Markdown="{Binding Body}"
    MarkdownStyle="{StaticResource DarkThemeMarkdownStyle}" />
```

**Option B: Fallback Plain-Text Converter**

If full markdown is deferred, strip formatting for basic readability:

```csharp
public class MarkdownToPlainTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string markdown || string.IsNullOrEmpty(markdown))
            return string.Empty;

        // Strip common markdown syntax
        var result = markdown;

        // Remove code blocks (``` ... ```)
        result = Regex.Replace(result, @"```[\s\S]*?```", "[code block]");

        // Remove inline code (`...`)
        result = Regex.Replace(result, @"`([^`]+)`", "$1");

        // Remove headers (# ## ###)
        result = Regex.Replace(result, @"^#{1,6}\s*", "", RegexOptions.Multiline);

        // Remove bold/italic (**text**, *text*, __text__, _text_)
        result = Regex.Replace(result, @"\*\*([^*]+)\*\*", "$1");
        result = Regex.Replace(result, @"\*([^*]+)\*", "$1");
        result = Regex.Replace(result, @"__([^_]+)__", "$1");
        result = Regex.Replace(result, @"_([^_]+)_", "$1");

        // Convert links [text](url) to text (url)
        result = Regex.Replace(result, @"\[([^\]]+)\]\(([^)]+)\)", "$1 ($2)");

        // Remove images ![alt](url)
        result = Regex.Replace(result, @"!\[([^\]]*)\]\([^)]+\)", "[image: $1]");

        // Remove horizontal rules
        result = Regex.Replace(result, @"^[-*_]{3,}$", "---", RegexOptions.Multiline);

        return result.Trim();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
```

### 7) PullRequestFileNode (TreeView Model)

**Location:** `src/Leaf/Models/PullRequestFileNode.cs`

For directory-grouped file tree view:

```csharp
namespace Leaf.Models;

/// <summary>
/// Hierarchical node for displaying PR files in a directory tree.
/// Used by TreeView in PullRequestPanel for large PRs.
/// </summary>
public class PullRequestFileNode
{
    /// <summary>
    /// Display name (directory name or file name).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// True if this node represents a directory, false if a file.
    /// </summary>
    public bool IsDirectory { get; set; }

    /// <summary>
    /// Full path for this node (used for sorting and deduplication).
    /// </summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>
    /// The file info (only set for leaf nodes where IsDirectory == false).
    /// </summary>
    public PullRequestFileInfo? File { get; set; }

    /// <summary>
    /// Child nodes (subdirectories and files).
    /// </summary>
    public ObservableCollection<PullRequestFileNode> Children { get; set; } = [];

    /// <summary>
    /// Display text for UI binding.
    /// Shows file status indicator for files, directory icon for folders.
    /// </summary>
    public string DisplayName => IsDirectory
        ? $"📁 {Name}"
        : $"{File?.StatusIndicator ?? " "} {Name}";

    /// <summary>
    /// Build a tree from a flat list of PR files.
    /// </summary>
    public static ObservableCollection<PullRequestFileNode> BuildTree(IEnumerable<PullRequestFileInfo> files)
    {
        var root = new Dictionary<string, PullRequestFileNode>();
        var result = new ObservableCollection<PullRequestFileNode>();

        foreach (var file in files.OrderBy(f => f.Path))
        {
            var parts = file.Path.Split('/', '\\');
            var currentPath = "";
            PullRequestFileNode? parent = null;
            var currentCollection = result;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var isLast = i == parts.Length - 1;
                currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";

                if (!root.TryGetValue(currentPath, out var node))
                {
                    node = new PullRequestFileNode
                    {
                        Name = part,
                        FullPath = currentPath,
                        IsDirectory = !isLast,
                        File = isLast ? file : null
                    };
                    root[currentPath] = node;
                    currentCollection.Add(node);
                }

                if (!isLast)
                {
                    currentCollection = node.Children;
                }
            }
        }

        return result;
    }
}
```

---

## Error Handling

**Follow existing patterns from GitHubService.cs and AzureDevOpsService.cs:**

```csharp
// HTTP errors
if (!response.IsSuccessStatusCode)
{
    var errorContent = await response.Content.ReadAsStringAsync();
    throw new HttpRequestException($"Failed to fetch PRs: {response.StatusCode}\n{errorContent}");
}

// Missing auth
if (string.IsNullOrEmpty(token))
{
    throw new InvalidOperationException("No PAT configured. Please add your PAT in Settings.");
}

// Rate limiting (GitHub)
if (response.StatusCode == HttpStatusCode.TooManyRequests ||
    response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) &&
    remaining.First() == "0")
{
    throw new HttpRequestException("API rate limit exceeded. Please try again later.");
}
```

**UI error states:**
- Show error message in status bar (StatusMessage property)
- Show "No PRs available" when list is empty
- Show auth prompt when token is missing
- Disable PR features when remote is not GitHub/AzureDevOps

---

## Caching & Performance

**Thread-Safety:** Use `ConcurrentDictionary` to avoid race conditions with async access.
**Path Normalization:** Normalize paths to avoid cache splits from case/slash differences on Windows.

```csharp
// Add to PullRequestService
private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(90);

// Thread-safe cache - ConcurrentDictionary handles concurrent reads/writes
private readonly ConcurrentDictionary<string, (DateTime timestamp, List<PullRequestInfo> data)> _prListCache = new();

// Lock for cache operations that need atomicity (check-then-fetch)
private readonly SemaphoreSlim _cacheLock = new(1, 1);

public async Task<List<PullRequestInfo>> ListPullRequestsAsync(
    string repoPath,
    PullRequestStatus state = PullRequestStatus.Open,
    string? author = null,
    string? searchQuery = null,
    CancellationToken cancellationToken = default)
{
    var cacheKey = BuildCacheKey(repoPath, state, author, searchQuery);

    // Fast path: check cache without lock
    if (_prListCache.TryGetValue(cacheKey, out var cached) &&
        DateTime.UtcNow - cached.timestamp < CacheTtl)
    {
        return cached.data;
    }

    // Slow path: fetch with lock to prevent duplicate requests
    await _cacheLock.WaitAsync(cancellationToken);
    try
    {
        // Double-check after acquiring lock (another thread may have fetched)
        if (_prListCache.TryGetValue(cacheKey, out cached) &&
            DateTime.UtcNow - cached.timestamp < CacheTtl)
        {
            return cached.data;
        }

        var result = await FetchPullRequestsFromApiAsync(repoPath, state, author, searchQuery, cancellationToken);
        _prListCache[cacheKey] = (DateTime.UtcNow, result);
        return result;
    }
    finally
    {
        _cacheLock.Release();
    }
}

private static string BuildCacheKey(string repoPath, PullRequestStatus state, string? author, string? searchQuery)
{
    // Normalize path for consistent caching on Windows:
    // - Convert to lowercase (Windows paths are case-insensitive)
    // - Normalize slashes (\ vs /)
    // - Trim trailing slashes
    var normalizedPath = NormalizePath(repoPath);

    var authorPart = string.IsNullOrWhiteSpace(author) ? "_" : author.ToLowerInvariant().Trim();
    var searchPart = string.IsNullOrWhiteSpace(searchQuery) ? "_" : searchQuery.ToLowerInvariant().Trim();
    return $"{normalizedPath}|{state}|{authorPart}|{searchPart}";
}

private static string NormalizePath(string path)
{
    if (string.IsNullOrEmpty(path))
        return string.Empty;

    // Normalize for Windows: lowercase, forward slashes, no trailing slash
    return path
        .Replace('\\', '/')
        .TrimEnd('/')
        .ToLowerInvariant();
}

public void InvalidateCache(string repoPath)
{
    var normalizedPath = NormalizePath(repoPath);
    var prefix = $"{normalizedPath}|";

    // Thread-safe removal
    var keysToRemove = _prListCache.Keys.Where(k => k.StartsWith(prefix)).ToList();
    foreach (var key in keysToRemove)
    {
        _prListCache.TryRemove(key, out _);
    }
}
```

**Lazy loading strategy:**
- Load PR list immediately (limited fields)
- Load files/commits/reviews only when PR is selected
- Load comments only when comments section is expanded
- Paginate large file lists (>100 files)

---

## Integration with Existing Flow

1. **DiffViewer integration:**
   - Reuse existing `DiffViewerViewModel.LoadDiff()` for PR file diffs
   - Set `RepositoryPath` for blame/history navigation
   - PR file diff uses same `FileDiffResult` model

2. **Graph update after operations:**
   ```csharp
   // After merge/checkout in PullRequestViewModel, raise event to MainViewModel
   public event EventHandler? GraphRefreshRequested;

   private async Task AfterPrOperationAsync()
   {
       // Invalidate cache
       InvalidateCache(RepositoryPath);

       // Request MainViewModel to refresh the graph
       // (MainViewModel handles this via GitGraphViewModel.LoadCommitsAsync + RefreshBranchesAsync)
       GraphRefreshRequested?.Invoke(this, EventArgs.Empty);
   }
   ```

   **Note:** Do NOT call a non-existent `RefreshRepositoryAsync`. Instead:
   - The `FileWatcherService` will automatically detect `.git` directory changes
   - For immediate feedback, raise `GraphRefreshRequested` event
   - MainViewModel handles refresh via existing `GitGraphViewModel.LoadCommitsAsync()`

3. **Auto-refresh PR list:**
   ```csharp
   // After create/merge/close
   InvalidateCache(RepositoryPath);
   await RefreshAsync();  // Refresh PR list
   await AfterPrOperationAsync();  // Trigger graph refresh
   ```

---

## Testing Plan

### Unit Tests (with mocked HTTP)

```csharp
[TestClass]
public class GitHubPullRequestServiceTests
{
    [TestMethod]
    public async Task ListPullRequests_ReturnsOpenPRs()
    {
        // Mock HTTP response with sample PR JSON
        // Verify correct parsing to PullRequestInfo
    }

    [TestMethod]
    public async Task CreatePullRequest_SetsCorrectHeaders()
    {
        // Verify Accept header, auth header, body format
    }

    [TestMethod]
    public async Task MergePullRequest_HandlesConflict()
    {
        // Mock 409 response, verify PullRequestMergeResult.Error
    }
}
```

### Manual Test Scenarios

| Scenario | GitHub | Azure DevOps |
|----------|--------|--------------|
| List open PRs | ✓ | ✓ |
| List closed/merged PRs | ✓ | ✓ |
| Search PRs by title | ✓ | ✓ |
| View PR details | ✓ | ✓ |
| View PR files with diff | ✓ | ✓ |
| View PR commits | ✓ | ✓ |
| View PR reviews/approvals | ✓ | ✓ |
| View PR comments | ✓ | ✓ |
| View status checks | ✓ | ✓ |
| Create PR (standard) | ✓ | ✓ |
| Create PR (draft) | ✓ | ✓ |
| Merge PR (merge) | ✓ | ✓ |
| Merge PR (squash) | ✓ | ✓ |
| Merge PR (rebase) | ✓ | N/A |
| Close PR | ✓ | ✓ |
| Checkout PR locally | ✓ | ✓ |
| PR with large file list (100+) | ✓ | ✓ |
| PR with many comments | ✓ | ✓ |
| No auth token error | ✓ | ✓ |
| Rate limit handling | ✓ | N/A |

---

## Incremental Delivery

### Phase 1: Core Infrastructure + GitHub List/View
- [ ] Add all data models (including HeadSha/BaseSha on PullRequestInfo)
- [ ] Add IPullRequestService interface
- [ ] Implement GitHubPullRequestService (list, get, files, commits)
  - [ ] Configure HttpClient injection (singleton or IHttpClientFactory)
  - [ ] Map HeadSha from response `head.sha`, BaseSha from `base.sha`
  - [ ] Implement FetchAllPagesAsync helper for pagination (Link header following)
- [ ] Add PullRequestViewModel
  - [ ] Handle checkout edge cases:
    - [ ] Local branch collision (pr-{number} already exists) - check tracking, reuse or rename
    - [ ] Dirty working directory - check before checkout, show user-friendly message
    - [ ] Parse Git errors to improve UX (avoid "exit code 1" messages)
- [ ] Add basic PullRequestPanel UI (list only)
- [ ] Wire into MainViewModel (with try/catch on async void handlers)

### Phase 2: PR Details + File Diff
- [ ] Add PR details panel UI
- [ ] Implement GetPullRequestFileDiffAsync
- [ ] Connect file list click to DiffViewer
- [ ] Add reviews/comments display
- [ ] Add MarkdownToPlainTextConverter (basic fallback for PR body/comments)

### Phase 3: Create PR
- [ ] Add CreatePullRequestDialog
- [ ] Implement CreatePullRequestAsync
- [ ] Add branch context menu integration
- [ ] Add draft PR support

### Phase 4: Merge/Close/Update Operations
- [ ] Add MergePullRequestDialog
- [ ] Implement MergePullRequestAsync (all methods)
- [ ] Implement ClosePullRequestAsync
- [ ] Implement UpdatePullRequestAsync (title, body, draft toggle)
- [ ] Add Edit PR dialog/inline editing
- [ ] Add status checks display (use HeadSha for check-runs API, avoid extra fetch)
- [ ] Add merge warnings

### Phase 5: Find PR & Polish
- [ ] Implement FindPullRequestsForCommitAsync
- [ ] Wire up "Find Pull Request..." commit context menu
- [ ] Handle multiple PR results (show selection UI)
- [ ] Add squash-merge detection fallback (search PRs by commit message #123 pattern)

### Phase 6: Azure DevOps Parity
- [ ] Implement AzureDevOpsPullRequestService
  - [ ] Configure HttpClient injection (singleton or IHttpClientFactory)
  - [ ] Map HeadSha from `lastMergeSourceCommit.commitId`, BaseSha from `lastMergeTargetCommit.commitId`
  - [ ] Map reviewer votes to PullRequestReviewState (10=Approved, 5=ApprovedWithSuggestions, 0=Pending, -5=WaitingForAuthor, -10=ChangesRequested)
- [ ] Handle Azure-specific differences (threads, iterations)
- [ ] Use iterations API for file diffs (sourceRefCommit/commonRefCommit instead of branch head)
- [ ] Implement Azure DevOps commit→PR lookup
- [ ] Test with Azure DevOps repos

### Phase 7: Polish & Advanced Features
- [ ] Add caching layer
- [ ] Add refresh button and auto-refresh
- [ ] Add loading states and error handling
- [ ] Add keyboard shortcuts
- [ ] Performance optimization
- [ ] Add PullRequestFileNode model for tree view
- [ ] Add TreeView file list toggle (flat list vs directory-grouped tree)
- [ ] Add full markdown renderer (MdXaml or Markdig.Wpf) for PR body/comments

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| API rate limits (GitHub: 5000/hour) | Users may hit limits with frequent use | Cache responses (90s TTL), show rate limit warning |
| Missing auth scopes | PR operations may fail | Detect 403 errors, show clear "missing permissions" message with required scopes |
| Large PRs (1000+ files) | Slow loading, memory issues | Paginate file list, lazy load file content, virtualize UI |
| Azure DevOps API differences | Inconsistent behavior | Abstract differences behind IPullRequestService, test both providers |
| OAuth token expiry | Silent failures | Check token validity on startup, refresh if needed |
| Network failures | Poor UX | Graceful error handling, retry with backoff, offline indicator |
| Merge conflicts | Confusing error | Clear error message, suggest "pull and resolve locally" |

---

## File Structure Summary

```
src/Leaf/
├── Models/
│   ├── PullRequestInfo.cs
│   ├── PullRequestFileInfo.cs
│   ├── PullRequestReviewInfo.cs
│   ├── PullRequestCommentInfo.cs
│   ├── PullRequestStatusCheckInfo.cs
│   └── PullRequestMergeResult.cs
├── Services/
│   ├── IPullRequestService.cs
│   ├── PullRequestService.cs
│   ├── GitHubPullRequestService.cs
│   └── AzureDevOpsPullRequestService.cs
├── ViewModels/
│   ├── PullRequestViewModel.cs
│   └── CreatePullRequestDialogViewModel.cs
├── Controls/
│   ├── PullRequestPanel.xaml
│   └── PullRequestPanel.xaml.cs
└── Views/
    ├── CreatePullRequestDialog.xaml
    ├── CreatePullRequestDialog.xaml.cs
    ├── MergePullRequestDialog.xaml
    └── MergePullRequestDialog.xaml.cs
```

---

## API Reference

### GitHub REST API
- Documentation: https://docs.github.com/en/rest/pulls
- Authentication: Bearer token or Basic auth with PAT
- Base URL: https://api.github.com
- Rate limit: 5000 requests/hour (authenticated)

### Azure DevOps REST API
- Documentation: https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-requests
- Authentication: Basic auth with PAT (empty username)
- Base URL: https://dev.azure.com/{organization}
- API Version: 7.0

---

## Future Improvements

The following items have been **integrated into the main plan**:

- ✅ **HeadSha/BaseSha** → Added to PullRequestInfo model (Data Models section) and phased (Phase 1, 4, 6)
- ✅ **Azure DevOps iterations** → Added to AzureDevOpsPullRequestService section and Phase 6
- ✅ **GitHub pagination** → Added FetchAllPagesAsync helper to Phase 1
- ✅ **Markdown rendering** → Added MarkdownViewer/converter to UI section (§6), Phase 2 (fallback), Phase 7 (full)
- ✅ **Find PR for squash merges** → Added to Phase 5
- ✅ **HttpClient lifecycle** → Added DI notes to GitHubPullRequestService and AzureDevOpsPullRequestService sections
- ✅ **async void try/catch** → Added to MainViewModel event handler snippets
- ✅ **TreeView file list** → Added PullRequestFileNode model (§7), UI toggle in PullRequestPanel, Phase 7

**Remaining future enhancements:**

- Unified diff parsing edge cases: support zero-length hunks (added/deleted files with
  no content), tolerate optional context text after @@ hunk headers, and surface the
  "No newline at end of file" marker in DiffViewer UI (e.g., special DiffLineType.NoNewline
  or inline badge after the last line).
