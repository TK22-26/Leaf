# Pull Request Implementation Plan (Leaf)

## Goals
- Add first-class pull request support for GitHub and Azure DevOps.
- Integrate pull requests into the existing repository tree alongside branches, tags, and worktrees.
- Use the main content area as a multi-mode surface that shows either the git graph OR a PR view (create form / detail), driven by tree selection.
- Reuse current auth, diff, refresh, and composition patterns already present in Leaf.
- Never lock users to Leaf or browser — always provide both in-app actions and "Open in browser" links.

## Product Direction
Pull requests appear as a dedicated `PULL REQUESTS` section in the repository tree, following the same collapsible pattern used by branches, tags, and worktrees. The section header includes a count badge, an add ("+") button to create PRs, and a filter button with a popup for filtering options. This requires extending the current category-header template, which today only supports count text plus a single category action slot.

Selecting a pull request replaces the main git graph area with a PR detail view. Creating a PR replaces the main area with an inline creation form. Navigating back to the graph happens via explicit close/back buttons, by selecting a branch/tag/worktree in the tree, or by selecting a commit in the graph. Entering PR mode is blocked while merge/conflict or orphaned-conflict state is active; an actionable toast explains why and keeps the user in the repository-state workflow. Once PR mode is active, the right-side repository panel hides.

After creating a PR, an actionable toast built on the existing `NotificationHost` / `NotificationService` / `NotificationCard` stack shows a success message with actions to open the PR detail in Leaf or open it in the browser.

All PR management actions (merge, close, update) are available both in-app from the detail view and via a persistent "Open in browser" link.

## Scope
- Providers: GitHub and Azure DevOps.
- Core flows: list, refresh, filter, inspect, create, update, merge, close, checkout locally, and find by commit.
- Detail data: files, commits, comments, reviews, status checks, and reviewer search.
- Create form fields: source branch, target branch, title, description, reviewers (with autocomplete), draft toggle.
- Explicitly out of scope: labels, milestones, work items, provider-specific admin actions, and a full web-style review editor.

## Architectural Constraints
- Do not add a standalone `PullRequestPanel` or PR browser panel.
- The main content area is a multi-mode surface: git graph, PR create form, or PR detail view — only one visible at a time.
- The right-side repository panel hides when any PR mode is active.
- PR detail and create modes are unavailable while merge/conflict or orphaned-conflict state is active. Show an actionable toast instead of hiding the resolution UI.
- Dirty working changes do not automatically block PR browsing; create, checkout, merge, and similar actions decide their own preflight warnings or disablement.
- Do not introduce a new dependency injection framework. Follow the current composition root in `MainWindow.xaml.cs`.
- Use PAT-based auth through `CredentialService`. Do not revive GitHub device-flow work.
- Use `IRepositorySession` for repository-scoped pull request operations where practical.
- Use `IRepositoryEventHub` for post-mutation refresh instead of custom graph refresh events.
- Reuse existing `BranchListView`, `BranchCategory`, `BranchCategoryItemsConverter`, and `DiffViewerViewModel` patterns where possible.
- Extend the existing notification stack (`NotificationHost`, `NotificationService`, `NotificationCard`) so toast notifications can carry success/info states plus inline actions or links (used for post-create feedback and other action confirmations).

## Proposed Architecture

### 1. Main Content Area Multi-Mode Surface
- The main content area (where the git graph currently renders) becomes a switchable surface supporting three modes: git graph (default), PR create form, and PR detail view.
- Mode is driven by tree selection and explicit user actions (e.g., clicking "+" to create, clicking a PR to view details).
- Navigation back to the graph: close/back button on PR views, selecting any branch/tag/worktree in the tree, or selecting a commit in the graph.
- `MainWindow.xaml` manages visibility of the three content modes via `MainViewModel` state.

### 2. Tree Integration
- Extend `BranchCategory` with a `PullRequests` collection and `IsPullRequestsCategory` flag.
- Keep the existing `BranchCategory` type for this milestone even though the name becomes broader than just branches. A rename can happen later if it proves worthwhile.
- Update `BranchCategoryItemsConverter` and `BranchListView` templates so PRs render as another first-level section in the current tree.
- PR section header includes: collapsible expander with count badge, "+" button (opens create form), filter button (opens filter popup with open/closed/merged filters).
- The header is not a zero-churn reuse of the existing category template; it needs a template extension so badge styling and two header actions can coexist without regressing other categories.
- Default filter: show open PRs only.
- Add click and context-menu handlers for PR nodes in `BranchListView.xaml.cs`, following the existing tag/worktree interaction model.

### 3. Selection and Detail Surface
- Add `SelectedPullRequest`, `PullRequestDetailViewModel`, and content mode state to `MainViewModel`.
- Selecting a PR switches the main area from git graph to PR detail view; the right-side repository panel hides.
- If merge/conflict or orphaned-conflict state is active, selecting a PR does not enter PR mode; show an actionable toast and keep the current repository-state surface visible.
- Deselecting (clicking close/back, selecting a branch/tag/worktree in the tree, or selecting a commit in the graph) restores the git graph and normal right-pane behavior.
- `DiffViewerControl` continues to overlay the content area the same way it does today.

### 4. Service Layer
- Introduce an application-facing `IPullRequestService` facade for list/detail/create/update/merge/close/find operations.
- Include reviewer-related methods: search reviewer candidates and request reviewers.
- Back that facade with provider-specific adapters, one for GitHub and one for Azure DevOps.
- Resolve provider capabilities from existing remote detection rather than hard-coded repo metadata.
- Keep provider mapping logic inside adapters; keep models provider-neutral.

### 5. Auth and Remote Resolution
- Reuse PAT lookup via `CredentialService` for GitHub owner keys and Azure DevOps organization keys.
- Parse remotes using the same conventions Leaf already uses for provider detection.
- GitHub user reviewer search: `GET /repos/{owner}/{repo}/collaborators` with client-side filtering. Requires `repo` + `read:org` scopes.
- GitHub team reviewer search: `GET /repos/{owner}/{repo}/teams` with client-side filtering by name/slug and request payload mapping through `team_reviewers`. Team discovery applies only where repository team access exists.
- Azure DevOps reviewer search: `GET /_apis/identities?searchFilter=General&filterValue={term}` with server-side filtering for users and groups. Requires `vso.identity` scope.
- Treat old OAuth refresh-token cleanup as legacy only; it is not part of the PR feature path.

### 6. Refresh Model
- After PR mutations, invalidate pull request caches and request refresh via `IRepositoryEventHub`.
- Use refresh scopes rather than custom event chains such as `GraphRefreshRequested`.
- Continue to rely on existing file watcher behavior for local git state changes where it already applies.

### 7. Actionable Toast Notifications
- Extend the existing notification stack instead of creating a parallel popup system.
- Add success/info support plus action metadata so notifications can include inline actions such as "Open PR in Leaf" or "Open in browser".
- Used for post-create PR feedback and other action confirmations throughout the PR workflow.

## Recommended Data Shapes
- `PullRequestInfo`: tree item and summary model; immutable provider data plus observable selection state.
- `PullRequestDetails`: expanded detail payload for a selected PR.
- `PullRequestFileInfo`, `PullRequestReviewInfo`, `PullRequestCommentInfo`, `PullRequestStatusCheckInfo`, `PullRequestMergeResult`.
- `ReviewerInfo`: provider-neutral reviewer directory model for autocomplete search, including kind (`user`, `team`, or `group`) plus the provider key/slug/id needed for assignment.
- Query types such as `PullRequestQuery` and `PullRequestIdentity` to keep provider routing explicit.

## Checkout Strategy
Checkout must be built from the git primitives that actually exist today.

Use:
- `GetWorkingChangesAsync` to detect a dirty worktree.
- `GetBranchesAsync` to detect local branch collisions and tracking state.
- `FetchAsync`, `CheckoutAsync`, `SetUpstreamAsync`, and `PullBranchFastForwardAsync` for branch materialization and tracking.
- Existing remote add/remove helpers only when a fork remote must be introduced temporarily or permanently.

Do not plan around non-existent helpers such as `HasUncommittedChangesAsync`, `GetBranchAsync`, `GetTrackingBranchAsync`, or `FetchRefAsync`.

## UI Flow

### Creating a PR
1. User clicks "+" in the `PULL REQUESTS` section header, OR right-clicks a branch and selects "Create Pull Request...".
2. If merge/conflict or orphaned-conflict state is active, show an actionable toast and keep the current repository-state UI visible.
3. Otherwise, the main content area switches from git graph to an inline create form. The right-side repository panel hides.
4. Form fields: source branch (dropdown), target branch (dropdown), title, description, reviewers (autocomplete search), draft toggle.
5. Source branch is pre-populated from context when launched from branch menu.
6. User fills out the form and clicks Accept. Cancel returns to the git graph.
7. On success: an actionable toast appears with actions to open the PR detail in Leaf or open it in the browser. The PR tree section refreshes.

### Viewing a PR
1. User clicks a PR in the tree section.
2. If merge/conflict or orphaned-conflict state is active, show an actionable toast and stay in the current repository-state workflow.
3. Otherwise, the main content area switches from git graph to PR detail view. The right-side repository panel hides.
4. PR detail shows: summary, source/target branches, body (with markdown), files, commits, reviews/comments, status checks, and action buttons.
5. A persistent "Open in browser" link is always visible in the detail header.
6. Clicking a file opens the existing diff viewer.
7. Close/back button, selecting a branch/tag/worktree in the tree, or selecting a commit in the graph returns to the git graph.

### Managing a PR
1. From the PR detail view: Merge (with method selection dialog), Close (with confirmation), Update (edit title/body/draft).
2. All actions also available via "Open in browser" — users are never locked to in-app management.
3. Mutations refresh tree state, detail state, and git history through the event hub.

### Finding a PR by Commit
1. Right-click a commit → "Find Pull Request..."
2. If one match → main area switches to PR detail.
3. If multiple matches → small chooser, then activates chosen PR.
4. If no match → clear message.

## Delivery Phases

### Phase 1: Core Infrastructure and GitHub Tree Integration
- Models, provider-neutral service contracts, and reviewer data shapes.
- GitHub provider for list/basic detail/reviewer discovery, including separate user and team search paths.
- `PULL REQUESTS` tree section with add button, filter button, count badge.
- MainViewModel multi-mode content area switching.

### Phase 2: Detail Surface and Diff Integration
- Pull request detail view in main content area (not right-side panel).
- Files, commits, reviews, comments, and markdown fallback rendering.
- Diff viewer integration from PR file selection.
- Close/back button and persistent "Open in browser" link.

### Phase 3: Create Pull Request
- Inline create form in main content area (not a dialog).
- Branch-context entry points (context menu and "+" button).
- Reviewer autocomplete search field covering users, teams, and groups through provider-aware search.
- Draft support and branch preflight validation.
- Actionable toast notifications for post-create feedback.

### Phase 4: Merge, Close, Update, and Checks
- Merge dialog and capability-aware merge methods.
- Close and update flows from main-area detail view.
- Status checks and merge readiness warnings.
- "Open in browser" available alongside all in-app actions.

### Phase 5: Find Pull Request by Commit
- Commit context-menu integration.
- Multi-result picker.
- Squash-merge heuristic fallback.

### Phase 6: Azure DevOps Parity
- Azure DevOps provider with identity search for reviewers.
- Iterations-based file diff support.
- Vote/thread mapping and policy differences.

### Phase 7: Polish
- Caching and request coalescing.
- File list tree mode inside details.
- Rich markdown rendering.
- Loading, empty, and error states.

## Testing Strategy
- Unit-test provider adapters with mocked HTTP payloads.
- Unit-test tree integration, content-area mode switching, and selection plumbing where feasible.
- Manual matrix for GitHub and Azure DevOps covering list/detail/create/update/merge/close/checkout/find.
- Regression-test branch, tag, and worktree tree behavior because PR support changes shared tree infrastructure.
- Test reviewer autocomplete for GitHub users and teams plus Azure DevOps users and groups.
- Test PR-mode blocking while merge/conflict or orphaned-conflict state is active.

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| Reusing `BranchCategory` for PRs increases type sprawl | Tree code becomes harder to reason about | Keep scope limited for this milestone and document a later rename to a generic repository tree category |
| Provider APIs differ sharply | Feature parity drifts | Keep provider-specific capability checks inside adapters and expose provider-neutral fallbacks |
| Large PRs are expensive to load | Slow tree/detail experience | Lazy-load details, cache lists briefly, and virtualize file/comment lists |
| Checkout from forks is error-prone | User-facing failures | Build checkout from current git primitives and validate remote/branch state before mutation |
| Shared tree code regresses existing items | Branch/tag/worktree UX degrades | Treat tree integration as a cross-cutting regression area in testing |
| Main content area mode switching introduces state complexity | Stale views or broken navigation | Clear state management in MainViewModel with explicit mode transitions and cleanup |
| Reviewer search adds API scope requirements | Users need broader PAT permissions | Guide users to update PAT scopes; handle 403 errors with clear messaging |

## File Structure Summary
```
src/Leaf/
|-- Models/
|   |-- PullRequestInfo.cs
|   |-- PullRequestDetails.cs
|   |-- PullRequestFileInfo.cs
|   |-- PullRequestReviewInfo.cs
|   |-- PullRequestCommentInfo.cs
|   |-- PullRequestStatusCheckInfo.cs
|   |-- PullRequestMergeResult.cs
|   |-- ReviewerInfo.cs
|-- Services/
|   |-- PullRequests/
|   |   |-- IPullRequestService.cs
|   |   |-- PullRequestService.cs
|   |   |-- GitHubPullRequestProvider.cs
|   |   |-- AzureDevOpsPullRequestProvider.cs
|-- ViewModels/
|   |-- PullRequestDetailViewModel.cs
|   |-- CreatePullRequestViewModel.cs
|-- Views/
|   |-- PullRequestDetailView.xaml
|   |-- PullRequestDetailView.xaml.cs
|   |-- CreatePullRequestView.xaml
|   |-- CreatePullRequestView.xaml.cs
|   |-- MergePullRequestDialog.xaml
|   |-- MergePullRequestDialog.xaml.cs
```

## Implementation Notes
- Follow current constructor injection patterns without introducing a DI container.
- Keep all new models and APIs ASCII-only unless an existing provider payload requires otherwise.
- Prefer incremental integration with the existing tree and content surfaces over new navigation abstractions.
- The create form is a view (not a dialog) that occupies the main content area; it has its own ViewModel with Accept/Cancel commands.
- The merge flow remains a dialog (modal confirmation is appropriate for destructive merge actions).
