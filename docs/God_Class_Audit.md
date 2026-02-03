# God Class / Code Fracture Audit

## Scope
Focused on large, multi‑responsibility classes and areas where splitting would improve readability and maintainability.

## Top candidates (by size + mixed responsibilities)

### 1) `src/Leaf/Services/GitService.cs` (~3181 lines)
**Why it’s a god class**
- Single class implements a wide range of git operations: history, diffs, stash, GitFlow, blame, file ops, config, etc.
- Mixes LibGit2Sharp operations with direct git CLI calls and parsing logic.
- Contains many unrelated helper methods (branch label building, remote parsing, file history, blame parsing, stash workflows).

**Suggested fracture**
- Split by domain into dedicated services:
  - `GitHistoryService` (commit history, blame, file history)
  - `GitDiffService` (commit diff, file diff, working tree diff)
  - `GitBranchService` (branch CRUD, checkout, tips)
  - `GitStashService` (stash list, pop, drop)
  - `GitTagService` (tags)
  - `GitConfigService` (config read/write)
- Keep a thin `GitService` facade that composes these services and implements `IGitService`.

### 2) `src/Leaf/ViewModels/MainViewModel.cs` (~3113 lines)
**Why it’s a god class**
- Centralizes app orchestration, commands, UI state, and domain logic.
- Manages repository switching, git commands, UI panels, dialogs, and view models in one class.

**Suggested fracture**
- Split by feature slice:
  - `RepositoryManagementViewModel`
  - `CommitHistoryViewModel`
  - `WorkingChangesViewModel` (already exists but MainVM still orchestrates too much)
  - `BranchActionsViewModel` (create/finish/start GitFlow)
  - `MergeOperationsViewModel`
- Move dialog orchestration into a `DialogService` to keep MainVM focused on state.

### 3) `src/Leaf/Controls/GitGraphCanvas.cs` (~2622 lines)
**Why it’s a god class**
- Custom rendering + hit‑testing + tooltip logic + branch label layout + stash/working changes row drawing all in one file.
- Large number of layout constants and drawing routines mixed with interaction state.

**Suggested fracture**
- Extract render passes into separate classes:
  - `GraphRailRenderer`
  - `GraphNodeRenderer`
  - `GraphLabelRenderer`
  - `GraphOverlayRenderer` (stash/working changes)
- Extract hit‑testing into a `GraphHitTester`.
- Keep `GitGraphCanvas` as coordinator with smaller render helper classes.

### 4) `src/Leaf/ViewModels/WorkingChangesViewModel.cs` (~1471 lines)
**Why it’s a god class**
- Contains UI state, command logic, file operations, staging/unstaging, tree building, path parsing, and OS shell calls.

**Suggested fracture**
- Extract:
  - `WorkingChangesTreeBuilder`
  - `WorkingChangesCommandService`
  - `WorkingChangesFileOperations` (delete/open/copy path)
- Keep VM focused on state + command wiring.

### 5) `src/Leaf/Services/GitFlowService.cs` (~1004 lines)
**Why it’s a god class**
- Handles GitFlow lifecycle, config parsing, branch naming, and multiple sub‑features in one class.

**Suggested fracture**
- Separate GitFlow config persistence from GitFlow commands:
  - `GitFlowConfigService`
  - `GitFlowBranchService`

### 6) `src/Leaf/ViewModels/GitGraphViewModel.cs` (~778 lines)
**Why it’s growing toward a god class**
- History loading + filtering + search + selection + lazy‑load + stash/working changes all in one VM.

**Suggested fracture**
- Extract:
  - `CommitSearchService`
  - `CommitFilterService`
  - `CommitSelectionService`

### 7) `src/Leaf/Views/GitGraphView.xaml.cs` (~782 lines)
**Why it’s at risk**
- Handles scrolling, hit testing, tooltip orchestration, menu building, and event wiring in code-behind.

**Suggested fracture**
- Move tooltip building to a `GitGraphTooltipService`.
- Move context menu generation to a `CommitContextMenuBuilder`.
- Minimize code‑behind to view-specific wiring only.

## Other fracture points
- Multiple XAML views embed operational logic in code‑behind (context menus, tooltip loading). Consider a thin view layer + small service helpers to keep UI logic centralized and testable.

## Prioritized refactor order (if only a few changes are possible)
1) `GitService` → split by domain (largest surface area, hardest to maintain)
2) `MainViewModel` → break into feature view models + dialog service
3) `GitGraphCanvas` → split into render + hit‑testing helpers

## Notes
- The sizes above are indicative (line counts) and intended to highlight maintenance hotspots.
- Splitting does not require changing public API surface immediately; a façade can preserve compatibility.
