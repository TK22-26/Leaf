---
title: "Leaf v2.0"
status: draft
owner: TBD
start_date: asap
duration: 22.25d
story_points: 89
depends_on: []
follows: []
---

# M1: Leaf v2.0

Major release adding comprehensive pull request support for GitHub and Azure DevOps repositories, enabling users to view, create, update, merge, and manage pull requests directly within Leaf.

## Scope

**Providers:** GitHub and Azure DevOps (including Enterprise/Server variants)

**PR Features:**
- List/filter PRs (open, closed, merged, by author, search)
- View PR details (commits, files/diffs, comments, reviews, status checks)
- Create new PRs (with draft support)
- Update PRs (title, body, draft toggle)
- Merge PRs (merge, squash, rebase methods)
- Close PRs without merging
- Checkout PR branches locally (fork-aware)
- Find PRs associated with commits

**Update Scope:** Title, body, draft toggle (labels/reviewers/milestones deferred to future phase)

**Local Repo Actions:** Checkout PR branch, fetch PR refs, open related file diff, create PR from local branch

## Architecture

- Provider-agnostic PR domain layer with provider-specific adapters
- Dedicated `PullRequestPanel` control (separate from Diff/Blame/History viewer)
- Single interface (`IPullRequestService`) for future provider extensibility
- Dependency injection for all services to enable testing

## Testing Requirements

### Unit Tests (with mocked HTTP)
- `GitHubPullRequestServiceTests`: List PRs, create PR headers, merge conflict handling
- `AzureDevOpsPullRequestServiceTests`: Vote mapping, iterations API

### Manual Test Matrix

| Scenario | GitHub | Azure DevOps |
|----------|--------|--------------|
| List open PRs | [x] | [x] |
| List closed/merged PRs | [x] | [x] |
| Search PRs by title | [x] | [x] |
| View PR details | [x] | [x] |
| View PR files with diff | [x] | [x] |
| View PR commits | [x] | [x] |
| View PR reviews/approvals | [x] | [x] |
| View PR comments | [x] | [x] |
| View status checks | [x] | [x] |
| Create PR (standard) | [x] | [x] |
| Create PR (draft) | [x] | [x] |
| Merge PR (merge) | [x] | [x] |
| Merge PR (squash) | [x] | [x] |
| Merge PR (rebase) | [x] | N/A |
| Close PR | [x] | [x] |
| Checkout PR locally | [x] | [x] |
| PR with large file list (100+) | [x] | [x] |
| PR with many comments | [x] | [x] |
| No auth token error | [x] | [x] |
| Rate limit handling | [x] | N/A |

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| API rate limits (GitHub: 5000/hour) | Users may hit limits | Cache responses (90s TTL), show rate limit warning |
| Missing auth scopes | PR operations may fail | Detect 403 errors, show clear "missing permissions" message |
| Large PRs (1000+ files) | Slow loading, memory issues | Paginate file list, lazy load content, virtualize UI |
| Azure DevOps API differences | Inconsistent behavior | Abstract behind IPullRequestService, test both providers |
| OAuth token expiry | Silent failures | Check token validity on startup, refresh if needed |
| Network failures | Poor UX | Graceful error handling, retry with backoff |
| Merge conflicts | Confusing error | Clear message, suggest "pull and resolve locally" |
## Goals

1. Enable full PR workflow without leaving Leaf (list, view, create, merge, close, update)
2. Support both GitHub and Azure DevOps with unified interface
3. Integrate PR file diffs with existing DiffViewer
4. Maintain responsive UI with proper caching (90s TTL) and lazy loading
5. Provide clear feedback for PR operations (merge, checkout, status checks)
6. Handle edge cases gracefully (forks, dirty working directory, rate limits, large PRs)

## Phases

This milestone is organized into the following phases:

| Phase | Name | Story Points | Status |
|-------|------|--------------|--------|
| [P1.1](P1.1-pull-request-support/) | Pull Request Support | 89 | Draft |
| **Total** | | **89 SP** | |

## Exit Criteria

Milestone 1 is complete when:

- [ ] All phases complete
- [ ] Integration tests passing
- [ ] Documentation complete

## Dependencies

**Depends on:** None
**Blocks:** None

## Proposed Improvements

<!-- Document opportunities for refactoring, scope adjustments, or better engineering practices here. Do not implement yet; just log them. When a proposed improvement affects work above this item's level, document it in the appropriate ancestor. -->

| ID | Description | Resolution |
|:---|:------------|:-----------|
