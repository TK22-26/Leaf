---
title: "Leaf v2.0"
status: draft
owner: TBD
start_date: asap
duration: 25d
story_points: 101
depends_on: []
follows: []
---

# M1: Leaf v2.0

Major release focused on pull request support for GitHub and Azure DevOps, implemented as a new `PULL REQUESTS` section in the existing repository tree with an inline main-area experience for PR creation and detail viewing.

## Scope
- List and filter pull requests from supported remotes.
- View pull request details in the main content area (replacing the git graph).
- Create pull requests via an inline form in the main content area.
- Reuse the current diff viewer for pull request file diffs.
- Support create, update, merge, close, checkout, and commit-to-PR lookup.
- Reviewer search and assignment during PR creation.
- Never lock users to in-app or browser — always provide both options.

## Architectural Direction
- Integrate PRs into `BranchListView` alongside branches, tags, and worktrees.
- Main content area is a multi-mode surface: git graph (default), PR create form, or PR detail view.
- Right-side repository panel hides when any PR mode is active.
- PR mode is unavailable while merge/conflict or orphaned-conflict state is active; use an actionable toast to keep users in the resolution workflow.
- Reuse PAT-based auth and existing remote detection.
- Build around `IRepositorySession`, `IRepositoryEventHub`, and the current composition root in `MainWindow.xaml.cs`.
- Extend the existing notification stack into actionable toast notifications for success/info flows.
- Avoid a standalone `PullRequestPanel` or a second parallel navigation model.

## Testing Requirements
- Provider adapter tests with mocked GitHub and Azure DevOps payloads.
- Shared tree regression testing for branches, tags, worktrees, and PRs.
- Manual coverage for list/detail/create/update/merge/close/checkout/find flows.
- Reviewer autocomplete testing for both providers.

## Risks
| Risk | Impact | Mitigation |
|------|--------|------------|
| Shared tree changes ripple into existing branch UX | Medium | Treat tree behavior as a first-class regression target |
| GitHub and Azure DevOps parity diverges | Medium | Keep provider-neutral contracts and capability-aware adapters |
| Large PRs slow the UI | Medium | Lazy loading, short-lived caching, and virtualized lists |
| Legacy docs assume panel-based UI | High | All PR planning docs rewritten around main-area integration |
| Main content area mode switching adds state complexity | Medium | Clear state management with explicit mode transitions in MainViewModel |

## Goals
1. Deliver a coherent PR workflow without leaving Leaf, while always offering browser access.
2. Keep navigation consistent with existing branch/tag/worktree behavior.
3. Use the main content area for PR creation and detail viewing instead of dialogs or side panels.
4. Align new services with current session and refresh patterns.
5. Ship GitHub first, then Azure DevOps parity.

## Phases
| Phase | Name | Story Points | Status |
|-------|------|--------------|--------|
| [P1.1](P1.1-pull-request-support/) | Pull Request Support | 101 | Draft |
| **Total** | | **101 SP** | |

## Exit Criteria
- [ ] Pull requests appear as a `PULL REQUESTS` tree section for supported repositories, with count badge, add button, and filter button.
- [ ] Selecting a PR opens pull request details in the main content area (replacing the git graph).
- [ ] Creating a PR uses an inline form in the main content area with reviewer autocomplete.
- [ ] Post-create feedback uses an actionable toast with links/actions to open the PR in Leaf or the browser.
- [ ] "Open in browser" is always available alongside in-app PR management.
- [ ] GitHub flow is complete for list/detail/create/update/merge/close/checkout/find.
- [ ] Azure DevOps parity is complete for the supported capability set.
- [ ] No regressions in branch, tag, or worktree tree behavior.

## Dependencies
**Depends on:** None
**Blocks:** None

## Proposed Improvements
| ID | Description | Resolution |
|:---|:------------|:-----------|
| M1-PR-1 | `BranchCategory` now models more than branches once PRs are added | Defer rename/refactor until after feature delivery |
| M1-PR-2 | Notification stack needs actionable success/info toast support | Extend `NotificationHost` / `NotificationService` / `NotificationCard` as part of E1.1.3 |
