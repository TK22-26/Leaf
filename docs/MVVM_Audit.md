# MVVM Practices Audit

## Scope
Review of MVVM adherence and areas where View, ViewModel, and Model responsibilities are blurred. Includes a full file‑by‑file scan of all ViewModels, Views, and Services.

## Findings (Summary)

### 1) Code-behind contains significant logic
**Where:** src/Leaf/Views/GitGraphView.xaml.cs, src/Leaf/Views/WorkingChangesView.xaml.cs, and other view code‑behind.
**Issue:** UI event handlers contain selection, command routing, tooltip creation, and context menu construction.
**Impact:** Tight coupling between UI and domain logic; harder unit testing; increases regression risk.
**Recommendation:** Move non‑visual logic into ViewModels/services; use commands + behaviors for input; build context menus via VM‑bound collections.

### 2) ViewModels directly manipulate UI state and UI APIs
**Where:** MainViewModel, WorkingChangesViewModel, CommitDetailViewModel, ConflictResolutionViewModel.
**Issue:** Direct MessageBox, Clipboard, Application.Current.Dispatcher usage.
**Recommendation:** Route all UI interactions through IDialogService / IWindowService / IDispatcherService to keep VM testable.

### 3) Services contain presentation shaping
**Where:** GitService builds branch labels/tags for UI display.
**Issue:** Presentation logic lives in data service rather than VM/presenter.
**Recommendation:** Keep services domain‑pure; format in VM or presenter layer.

### 4) Large, multi‑responsibility classes (MVVM strain)
**Where:** MainViewModel, GitGraphViewModel, WorkingChangesViewModel, GitGraphCanvas.
**Issue:** Feature logic, state, and UI concerns intermixed.
**Recommendation:** Split into feature services/VMs and thin coordinators.

## Positive MVVM usage
- Feature‑scoped ViewModels exist for Working Changes, Git Graph, Commit Details.
- Commands are widely used (RelayCommand).
- Models are mostly POCOs.

## Prioritized improvements
1) Reduce code‑behind logic (tooltips, context menus, hover behavior).
2) Split MainViewModel into feature VMs + dialog service.
3) Move presentation formatting out of GitService into VM/presenter.
4) Standardize all UI interactions through IDialogService / IWindowService / IDispatcherService.

---

## Full Surface Review (ViewModels / Views / Services)

### ViewModels (file‑by‑file scan)
| File | UI/Threading Flags |
| --- | --- |
| src\Leaf\ViewModels\MainViewModel.cs | MessageBox, Clipboard, Application.Current, Window, Dispatcher |
| src\Leaf\ViewModels\IViewModelFactory.cs |  |
| src\Leaf\ViewModels\GitGraphViewModel.cs |  |
| src\Leaf\ViewModels\DiffViewerViewModel.cs |  |
| src\Leaf\ViewModels\ConflictResolutionViewModel.cs | Clipboard, Application.Current, Dispatcher |
| src\Leaf\ViewModels\CommitDetailViewModel.cs | Clipboard, Process |
| src\Leaf\ViewModels\TerminalViewModel.cs | Application.Current, Dispatcher |
| src\Leaf\ViewModels\MergeDialogViewModel.cs |  |
| src\Leaf\ViewModels\MergeCommitTooltipViewModel.cs |  |
| src\Leaf\ViewModels\WorkingChangesViewModel.cs | MessageBox, Clipboard, Process |
| src\Leaf\ViewModels\ViewModelFactory.cs |  |

**Notes:**
- MainViewModel has extensive MessageBox usage and direct UI window ownership in some flows.
- WorkingChangesViewModel uses MessageBox and Clipboard directly; should go through dialog/clipboard service.
- CommitDetailViewModel uses Clipboard directly; should be abstracted.
- ConflictResolutionViewModel uses Application.Current.Dispatcher directly; use IDispatcherService consistently.

### Views (file‑by‑file scan)
| File | XAML Event/Command Flags |
| --- | --- |
| src\Leaf\Views\WorkingChangesView.xaml | Mouse handlers, Commands |
| src\Leaf\Views\TerminalView.xaml | Key handlers, Commands |
| src\Leaf\Views\StartBranchDialog.xaml | Click handlers |
| src\Leaf\Views\SettingsDialog.xaml | Click handlers, Mouse handlers |
| src\Leaf\Views\RepositoryListView.xaml | Commands |
| src\Leaf\Views\ReportIssueDialog.xaml | Click handlers |
| src\Leaf\Views\MergeStatusView.xaml | Mouse handlers, Commands |
| src\Leaf\Views\MergeDialog.xaml | Click handlers |
| src\Leaf\Views\MergeCommitTooltipView.xaml |  |
| src\Leaf\Views\GitGraphView.xaml | Click handlers, Mouse handlers |
| src\Leaf\Views\GitFlowInitDialog.xaml | Click handlers |
| src\Leaf\Views\FinishBranchDialog.xaml | Click handlers |
| src\Leaf\Views\CreateTagDialog.xaml | Click handlers |
| src\Leaf\Views\ConflictResolutionView.xaml | Click handlers, Mouse handlers, Commands |
| src\Leaf\Views\CommitDetailView.xaml | Click handlers, Mouse handlers, Commands |
| src\Leaf\Views\CloneDialog.xaml | Click handlers |
| src\Leaf\Views\BranchListView.xaml | Click handlers, Mouse handlers, Key handlers, Commands |

**Notes:**
- Several XAML files wire event handlers (Click, Mouse, KeyDown), indicating view logic in code‑behind.
- GitGraphView.xaml(.cs) is a heavy controller‑style view; tooltip and context menu logic should move to VM/services.

### Services (file‑by‑file scan)
| File | UI/Threading Flags |
| --- | --- |
| src\Leaf\Services\AutoCommitService.cs | Process |
| src\Leaf\Services\AzureDevOpsService.cs |  |
| src\Leaf\Services\AzureDevOpsOAuthService.cs |  |
| src\Leaf\Services\AutoFetchService.cs | System.Windows, Dispatcher |
| src\Leaf\Services\CommitHistoryService.cs |  |
| src\Leaf\Services\BranchService.cs |  |
| src\Leaf\Services\CommitOperationService.cs |  |
| src\Leaf\Services\ConflictResolutionService.cs |  |
| src\Leaf\Services\UpdateService.cs | Process |
| src\Leaf\Services\ThreeWayMergeService.cs |  |
| src\Leaf\Services\TerminalService.cs | Process |
| src\Leaf\Services\TagService.cs |  |
| src\Leaf\Services\StashService.cs |  |
| src\Leaf\Services\StagingService.cs |  |
| src\Leaf\Services\SettingsService.cs |  |
| src\Leaf\Services\RepositoryManagementService.cs |  |
| src\Leaf\Services\RemoteSyncService.cs |  |
| src\Leaf\Services\RebaseService.cs |  |
| src\Leaf\Services\OllamaService.cs |  |
| src\Leaf\Services\MergeService.cs |  |
| src\Leaf\Services\IWindowService.cs | System.Windows, Application.Current |
| src\Leaf\Services\IThreeWayMergeService.cs |  |
| src\Leaf\Services\ITagService.cs |  |
| src\Leaf\Services\IStashService.cs |  |
| src\Leaf\Services\IStagingService.cs |  |
| src\Leaf\Services\IRepositoryManagementService.cs |  |
| src\Leaf\Services\IRemoteSyncService.cs |  |
| src\Leaf\Services\IRebaseService.cs |  |
| src\Leaf\Services\IMergeService.cs |  |
| src\Leaf\Services\IGitService.cs |  |
| src\Leaf\Services\IGitFlowService.cs |  |
| src\Leaf\Services\IDispatcherService.cs | Dispatcher |
| src\Leaf\Services\IDiffService.cs |  |
| src\Leaf\Services\IDialogService.cs | System.Windows, MessageBox, Dispatcher |
| src\Leaf\Services\ICredentialService.cs |  |
| src\Leaf\Services\IConflictResolutionService.cs |  |
| src\Leaf\Services\ICommitOperationService.cs |  |
| src\Leaf\Services\ICommitHistoryService.cs |  |
| src\Leaf\Services\IBranchService.cs |  |
| src\Leaf\Services\IAutoFetchService.cs |  |
| src\Leaf\Services\GitService.cs | Process |
| src\Leaf\Services\GitHubService.cs |  |
| src\Leaf\Services\GitHubOAuthService.cs |  |
| src\Leaf\Services\GitFlowService.cs | Process |
| src\Leaf\Services\FileWatcherService.cs |  |
| src\Leaf\Services\DispatcherService.cs | System.Windows, Dispatcher, Application.Current |
| src\Leaf\Services\DiffService.cs |  |
| src\Leaf\Services\DialogService.cs | System.Windows, MessageBox, Dispatcher |
| src\Leaf\Services\CredentialService.cs |  |

**Notes:**
- DialogService is the right direction (centralized MessageBox), but many VMs still call MessageBox directly.
- AutoFetchService uses DispatcherTimer (UI‑adjacent); consider a timer abstraction for testability.
- GitService combines domain logic with presentation shaping (labels), which should move to VM/presenter.

---

## Suggested MVVM Alignment Checklist
- View code‑behind limited to visual wiring; no domain decisions or state transitions.
- All user actions bound to commands; avoid handling logic in events when possible.
- ViewModels do not access Application.Current, Window, or MessageBox directly.
- Services return domain data; ViewModels shape data for presentation.
- Large viewmodels are split by feature and responsibility.

## Notes
- This audit is structural; changes can be incremental without breaking UX.
\n---\n\n## Per-File Narrative Review\n\n### ViewModels\n#### src\Leaf\ViewModels\MainViewModel.cs\n- Approx size: 3113 lines.\n- UI flags: MessageBox, Clipboard, Application.Current, Window, Dispatcher.\n- Notes: Uses MessageBox directly; route through IDialogService. Uses Clipboard directly; abstract behind clipboard service. Accesses Application.Current; prefer IDispatcherService/IWindowService. Dispatcher usage detected; ensure IDispatcherService is used consistently. Direct window access detected; use IWindowService or messaging.\n\n#### src\Leaf\ViewModels\IViewModelFactory.cs\n- Approx size: 35 lines.\n- UI flags: .\n- Notes: No direct UI API usage detected; likely MVVM-friendly.\n\n#### src\Leaf\ViewModels\GitGraphViewModel.cs\n- Approx size: 778 lines.\n- UI flags: .\n- Notes: No direct UI API usage detected; likely MVVM-friendly.\n\n#### src\Leaf\ViewModels\DiffViewerViewModel.cs\n- Approx size: 312 lines.\n- UI flags: .\n- Notes: No direct UI API usage detected; likely MVVM-friendly.\n\n#### src\Leaf\ViewModels\ConflictResolutionViewModel.cs\n- Approx size: 902 lines.\n- UI flags: Clipboard, Application.Current, Dispatcher.\n- Notes: Uses Clipboard directly; abstract behind clipboard service. Accesses Application.Current; prefer IDispatcherService/IWindowService. Dispatcher usage detected; ensure IDispatcherService is used consistently.\n\n#### src\Leaf\ViewModels\CommitDetailViewModel.cs\n- Approx size: 428 lines.\n- UI flags: Clipboard, Process.\n- Notes: Uses Clipboard directly; abstract behind clipboard service.\n\n#### src\Leaf\ViewModels\TerminalViewModel.cs\n- Approx size: 395 lines.\n- UI flags: Application.Current, Dispatcher.\n- Notes: Accesses Application.Current; prefer IDispatcherService/IWindowService. Dispatcher usage detected; ensure IDispatcherService is used consistently.\n\n#### src\Leaf\ViewModels\MergeDialogViewModel.cs\n- Approx size: 40 lines.\n- UI flags: .\n- Notes: No direct UI API usage detected; likely MVVM-friendly.\n\n#### src\Leaf\ViewModels\MergeCommitTooltipViewModel.cs\n- Approx size: 47 lines.\n- UI flags: .\n- Notes: No direct UI API usage detected; likely MVVM-friendly.\n\n#### src\Leaf\ViewModels\WorkingChangesViewModel.cs\n- Approx size: 1471 lines.\n- UI flags: MessageBox, Clipboard, Process.\n- Notes: Uses MessageBox directly; route through IDialogService. Uses Clipboard directly; abstract behind clipboard service.\n\n#### src\Leaf\ViewModels\ViewModelFactory.cs\n- Approx size: 42 lines.\n- UI flags: .\n- Notes: No direct UI API usage detected; likely MVVM-friendly.\n\n### Views\n#### src\Leaf\Views\WorkingChangesView.xaml\n- Approx size: 1289 lines.\n- XAML flags: Mouse handlers, Commands.\n- Notes: Mouse handlers indicate view logic; consider behaviors/commands.\n\n#### src\Leaf\Views\TerminalView.xaml\n- Approx size: 165 lines.\n- XAML flags: Key handlers, Commands.\n- Notes: Key event handlers in view; consider input bindings/commands.\n\n#### src\Leaf\Views\StartBranchDialog.xaml\n- Approx size: 269 lines.\n- XAML flags: Click handlers.\n- Notes: Click event handlers in XAML suggest code-behind logic; prefer ICommand bindings.\n\n#### src\Leaf\Views\SettingsDialog.xaml\n- Approx size: 1360 lines.\n- XAML flags: Click handlers, Mouse handlers.\n- Notes: Click event handlers in XAML suggest code-behind logic; prefer ICommand bindings. Mouse handlers indicate view logic; consider behaviors/commands.\n\n#### src\Leaf\Views\RepositoryListView.xaml\n- Approx size: 190 lines.\n- XAML flags: Commands.\n- Notes: No event handlers detected; view appears binding-driven.\n\n#### src\Leaf\Views\ReportIssueDialog.xaml\n- Approx size: 121 lines.\n- XAML flags: Click handlers.\n- Notes: Click event handlers in XAML suggest code-behind logic; prefer ICommand bindings.\n\n#### src\Leaf\Views\MergeStatusView.xaml\n- Approx size: 294 lines.\n- XAML flags: Mouse handlers, Commands.\n- Notes: Mouse handlers indicate view logic; consider behaviors/commands.\n\n#### src\Leaf\Views\MergeDialog.xaml\n- Approx size: 145 lines.\n- XAML flags: Click handlers.\n- Notes: Click event handlers in XAML suggest code-behind logic; prefer ICommand bindings.\n\n#### src\Leaf\Views\MergeCommitTooltipView.xaml\n- Approx size: 90 lines.\n- XAML flags: .\n- Notes: No event handlers detected; view appears binding-driven.\n\n#### src\Leaf\Views\GitGraphView.xaml\n- Approx size: 314 lines.\n- XAML flags: Click handlers, Mouse handlers.\n- Notes: Click event handlers in XAML suggest code-behind logic; prefer ICommand bindings. Mouse handlers indicate view logic; consider behaviors/commands.\n\n#### src\Leaf\Views\GitFlowInitDialog.xaml\n- Approx size: 760 lines.\n- XAML flags: Click handlers.\n- Notes: Click event handlers in XAML suggest code-behind logic; prefer ICommand bindings.\n\n#### src\Leaf\Views\FinishBranchDialog.xaml\n- Approx size: 296 lines.\n- XAML flags: Click handlers.\n- Notes: Click event handlers in XAML suggest code-behind logic; prefer ICommand bindings.\n\n#### src\Leaf\Views\CreateTagDialog.xaml\n- Approx size: 44 lines.\n- XAML flags: Click handlers.\n- Notes: Click event handlers in XAML suggest code-behind logic; prefer ICommand bindings.\n\n#### src\Leaf\Views\ConflictResolutionView.xaml\n- Approx size: 660 lines.\n- XAML flags: Click handlers, Mouse handlers, Commands.\n- Notes: Click event handlers in XAML suggest code-behind logic; prefer ICommand bindings. Mouse handlers indicate view logic; consider behaviors/commands.\n\n#### src\Leaf\Views\CommitDetailView.xaml\n- Approx size: 609 lines.\n- XAML flags: Click handlers, Mouse handlers, Commands.\n- Notes: Click event handlers in XAML suggest code-behind logic; prefer ICommand bindings. Mouse handlers indicate view logic; consider behaviors/commands.\n\n#### src\Leaf\Views\CloneDialog.xaml\n- Approx size: 696 lines.\n- XAML flags: Click handlers.\n- Notes: Click event handlers in XAML suggest code-behind logic; prefer ICommand bindings.\n\n#### src\Leaf\Views\BranchListView.xaml\n- Approx size: 540 lines.\n- XAML flags: Click handlers, Mouse handlers, Key handlers, Commands.\n- Notes: Click event handlers in XAML suggest code-behind logic; prefer ICommand bindings. Mouse handlers indicate view logic; consider behaviors/commands. Key event handlers in view; consider input bindings/commands.\n\n### Services\n#### src\Leaf\Services\AutoCommitService.cs\n- Approx size: 499 lines.\n- UI flags: Process.\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\AzureDevOpsService.cs\n- Approx size: 100 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\AzureDevOpsOAuthService.cs\n- Approx size: 334 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\AutoFetchService.cs\n- Approx size: 132 lines.\n- UI flags: System.Windows, Dispatcher.\n- Notes: Service references System.Windows; check MVVM boundary. Dispatcher usage; consider IDispatcherService abstraction.\n\n#### src\Leaf\Services\CommitHistoryService.cs\n- Approx size: 103 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\BranchService.cs\n- Approx size: 168 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\CommitOperationService.cs\n- Approx size: 86 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\ConflictResolutionService.cs\n- Approx size: 103 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\UpdateService.cs\n- Approx size: 296 lines.\n- UI flags: Process.\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\ThreeWayMergeService.cs\n- Approx size: 372 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\TerminalService.cs\n- Approx size: 90 lines.\n- UI flags: Process.\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\TagService.cs\n- Approx size: 70 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\StashService.cs\n- Approx size: 80 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\StagingService.cs\n- Approx size: 99 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\SettingsService.cs\n- Approx size: 171 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\RepositoryManagementService.cs\n- Approx size: 319 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\RemoteSyncService.cs\n- Approx size: 84 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\RebaseService.cs\n- Approx size: 101 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\OllamaService.cs\n- Approx size: 108 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\MergeService.cs\n- Approx size: 92 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\IWindowService.cs\n- Approx size: 40 lines.\n- UI flags: System.Windows, Application.Current.\n- Notes: Service references System.Windows; check MVVM boundary. Application.Current usage; avoid in services if possible.\n\n#### src\Leaf\Services\IThreeWayMergeService.cs\n- Approx size: 33 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\ITagService.cs\n- Approx size: 70 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\IStashService.cs\n- Approx size: 62 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\IStagingService.cs\n- Approx size: 82 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\IRepositoryManagementService.cs\n- Approx size: 94 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\IRemoteSyncService.cs\n- Approx size: 84 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\IRebaseService.cs\n- Approx size: 52 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\IMergeService.cs\n- Approx size: 54 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\IGitService.cs\n- Approx size: 457 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\IGitFlowService.cs\n- Approx size: 206 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\IDispatcherService.cs\n- Approx size: 38 lines.\n- UI flags: Dispatcher.\n- Notes: Dispatcher usage; consider IDispatcherService abstraction.\n\n#### src\Leaf\Services\IDiffService.cs\n- Approx size: 19 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\IDialogService.cs\n- Approx size: 62 lines.\n- UI flags: System.Windows, MessageBox, Dispatcher.\n- Notes: UI dialog usage inside service; centralize via IDialogService only. Service references System.Windows; check MVVM boundary. Dispatcher usage; consider IDispatcherService abstraction.\n\n#### src\Leaf\Services\ICredentialService.cs\n- Approx size: 32 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\IConflictResolutionService.cs\n- Approx size: 91 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\ICommitOperationService.cs\n- Approx size: 58 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\ICommitHistoryService.cs\n- Approx size: 115 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\IBranchService.cs\n- Approx size: 135 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\IAutoFetchService.cs\n- Approx size: 45 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\GitService.cs\n- Approx size: 3181 lines.\n- UI flags: Process.\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\GitHubService.cs\n- Approx size: 132 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\GitHubOAuthService.cs\n- Approx size: 247 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\GitFlowService.cs\n- Approx size: 1004 lines.\n- UI flags: Process.\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\FileWatcherService.cs\n- Approx size: 280 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\DispatcherService.cs\n- Approx size: 90 lines.\n- UI flags: System.Windows, Dispatcher, Application.Current.\n- Notes: Service references System.Windows; check MVVM boundary. Dispatcher usage; consider IDispatcherService abstraction. Application.Current usage; avoid in services if possible.\n\n#### src\Leaf\Services\DiffService.cs\n- Approx size: 92 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n\n#### src\Leaf\Services\DialogService.cs\n- Approx size: 176 lines.\n- UI flags: System.Windows, MessageBox, Dispatcher.\n- Notes: UI dialog usage inside service; centralize via IDialogService only. Service references System.Windows; check MVVM boundary. Dispatcher usage; consider IDispatcherService abstraction.\n\n#### src\Leaf\Services\CredentialService.cs\n- Approx size: 228 lines.\n- UI flags: .\n- Notes: No UI/threading flags detected; likely domain-pure.\n

---

## Refactor Map (Actionable)

This section is intended to be sufficient for a human or AI to execute the refactor. It lists concrete moves, target owners, and suggested interfaces.

### Global Strategy (Order of Operations)
1) Introduce UI abstraction services (`IDialogService`, `IClipboardService`, `IWindowService`, `IDispatcherService`) and use them in VMs.
2) Migrate view code‑behind logic into ViewModels or small UI services.
3) Split god ViewModels and services into feature slices.
4) Move presentation shaping out of core services into presenter/VM layer.

### New/Extended Interfaces (recommended)
- `IClipboardService`:
  - `void SetText(string text)`
- `IGraphTooltipService`:
  - `Task<ToolTip?> BuildMergeTooltipAsync(CommitInfo commit)`
- `IContextMenuService`:
  - `ContextMenu BuildCommitContextMenu(CommitInfo commit)`
- `IGraphSelectionService`:
  - `void SelectCommitByIndex(int index)` / `void SelectWorkingChanges()` / `void SelectStash(StashInfo stash)`

### ViewModels (move logic out of views)

#### `src/Leaf/Views/GitGraphView.xaml.cs`
**Move to:** `GitGraphViewModel` + services
- `CommitItem_ContextMenuOpening` → `IContextMenuService` (VM exposes `ContextMenu` or command).
- Tooltip logic (`ShowMergeTooltipAsync`, `BuildMergeTooltip`) → `IGraphTooltipService`.
- Hover/selection event handling → command bindings + `IGraphSelectionService`.
**Result:** Code‑behind becomes only input wiring (or eliminated via behaviors).

#### `src/Leaf/Views/WorkingChangesView.xaml.cs`
**Move to:** `WorkingChangesViewModel`
- File selection and context menu commands → bind `ICommand` directly in XAML.
- Remove `ApplyTreeVisibility` from code‑behind; use binding with `Visibility` converters.

#### `src/Leaf/ViewModels/MainViewModel.cs`
**Split into:**
- `RepositoryShellViewModel` (overall navigation + child VM composition)
- `RepositoryActionsViewModel` (fetch/pull/push/stash/branch actions)
- `GitFlowViewModel` (GitFlow workflows)
- `MergeOperationsViewModel` (merge/conflict dialogs)
**Move dialog usage to:** `IDialogService`

#### `src/Leaf/ViewModels/WorkingChangesViewModel.cs`
**Extract services:**
- `WorkingChangesTreeBuilder` (tree build logic)
- `WorkingChangesFileOpsService` (open, delete, copy path)
- `WorkingChangesStageService` (stage/unstage/discard)
**Keep in VM:** state, selection, commands binding

#### `src/Leaf/ViewModels/CommitDetailViewModel.cs`
**Replace:** direct `Clipboard` calls → `IClipboardService`

#### `src/Leaf/ViewModels/ConflictResolutionViewModel.cs`
**Replace:** `Application.Current.Dispatcher` → `IDispatcherService`

### Services (purify domain layer)

#### `src/Leaf/Services/GitService.cs`
**Split into:**
- `GitHistoryService` (commit history, blame)
- `GitDiffService` (diffs)
- `GitBranchService` (branch ops)
- `GitTagService`
- `GitStashService`
**Move presentation shaping** (branch labels/tags display) into a `CommitPresentationBuilder` in the VM layer.

#### `src/Leaf/Services/DialogService.cs`
- Keep as the only MessageBox owner; remove MessageBox calls from VMs.

### Concrete refactor checklist
- [ ] Add `IClipboardService` and inject into VMs that use clipboard.
- [ ] Replace all `MessageBox.Show` in VMs with `IDialogService`.
- [ ] Extract tooltip building from `GitGraphView.xaml.cs`.
- [ ] Replace context menu creation in code‑behind with VM‑bound items.
- [ ] Split `GitService` into domain‑focused services.

### Verification Steps
- Rebuild with `dotnet build Leaf.sln`.
- Validate UI actions: commit selection, tooltips, context menus.
- Ensure no view code‑behind triggers domain actions directly.


---

## Refactor‑Ready Appendix (Per‑Method Map + DI Plan)

> This appendix provides method‑level move guidance and concrete interface signatures so refactoring can be executed without additional discovery.

### A) Method‑Level Move Map (high priority offenders)

#### `src/Leaf/Views/GitGraphView.xaml.cs`
Move these methods into VM/services:
- `CommitItem_ContextMenuOpening` → `IContextMenuService.BuildCommitContextMenu(commit)`; VM exposes `ContextMenu` or `ICommand` to open.
- `CommitItem_ToolTipOpening`, `GraphCanvas_ToolTipOpening`, `ShowMergeTooltipAsync`, `BuildMergeTooltip`, `BuildTooltipLoading` → `IGraphTooltipService`.
- `CommitItem_MouseLeftButtonDown` → VM command `SelectCommitCommand` (command parameter = CommitInfo).
- `WorkingChangesRow_MouseLeftButtonDown` → VM command `SelectWorkingChangesCommand`.
- `StashItem_MouseLeftButtonDown` → VM command `SelectStashCommand`.
- `MainScrollViewer_PreviewMouseWheel` → consider attached behavior or custom ScrollViewer behavior class.

#### `src/Leaf/Views/WorkingChangesView.xaml.cs`
- `ApplyTreeVisibility` + toggle handlers → pure bindings using `Visibility` converters or `DataTrigger`s.
- File open/stage/unstage/discard right‑click behavior should use `Command` bindings only.

#### `src/Leaf/ViewModels/MainViewModel.cs`
- Replace all `MessageBox.Show` with `IDialogService` calls.
- Remove direct `Window` ownership in VM; use `IWindowService` for dialog ownership.
- Split into child VMs (`RepositoryActionsViewModel`, `GitFlowViewModel`, `MergeOperationsViewModel`).

#### `src/Leaf/ViewModels/CommitDetailViewModel.cs`
- Replace `Clipboard.SetText` with `IClipboardService.SetText`.

#### `src/Leaf/ViewModels/WorkingChangesViewModel.cs`
- Extract tree building into `WorkingChangesTreeBuilder`.
- Extract OS/file operations into `IFileSystemService` (open, delete, copy path).
- Replace MessageBox use with `IDialogService`.

#### `src/Leaf/ViewModels/ConflictResolutionViewModel.cs`
- Replace `Application.Current.Dispatcher` with `IDispatcherService` injected.

### B) Proposed Interface Signatures

```csharp
public interface IClipboardService { void SetText(string text); }
public interface IGraphTooltipService
{
    Task<ToolTip?> BuildMergeTooltipAsync(CommitInfo commit);
    ToolTip BuildLoadingTooltip();
}
public interface IContextMenuService
{
    ContextMenu BuildCommitContextMenu(CommitInfo commit);
}
public interface IFileSystemService
{
    void OpenInExplorer(string path);
    void CopyPath(string path);
    void DeleteFile(string path);
}
```

### C) DI Wiring Plan (example)
- Register services in App startup (or current DI configuration):
  - `IClipboardService` → `ClipboardService`
  - `IGraphTooltipService` → `GraphTooltipService`
  - `IContextMenuService` → `ContextMenuService`
  - `IFileSystemService` → `FileSystemService`
- Update VM constructors to accept these interfaces and use them instead of direct static calls.

### D) Staged Refactor Order (safe checkpoints)
1) **Introduce services + replace direct MessageBox/Clipboard usage.**
2) **Move tooltip and context menu building into services; wire via VM.**
3) **Replace view code‑behind mouse handlers with commands/behaviors.**
4) **Split `GitService` and `MainViewModel` by responsibility.**
5) **Verify with smoke tests (selection, tooltips, context menus).**

### E) Verification Checklist
- All UI dialogs are routed through `IDialogService`.
- No direct `Clipboard` calls in VMs.
- View code‑behind only wires events; no domain logic.
- Services contain no UI‑layer formatting.
- Build passes: `dotnet build Leaf.sln`.

