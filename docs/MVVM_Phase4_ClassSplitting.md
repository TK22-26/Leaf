# MVVM Phase 4: Large Class Splitting

**Status:** Planned
**Scope:** Split god classes into focused, single-responsibility components
**Risk:** High - major architectural changes

---

## Objective

Break down large, multi-responsibility classes into focused components following Single Responsibility Principle.

---

## Target 1: MainViewModel.cs (3113 lines)

### Current Responsibilities (Too Many)

1. Repository state management
2. Navigation/shell coordination
3. Branch operations (checkout, create, delete, rename)
4. Fetch/pull/push operations
5. Stash operations
6. GitFlow workflow management
7. Merge/conflict operations
8. Tag operations
9. Dialog coordination
10. Status message management
11. Update checking
12. File diff display

### Proposed Split

#### RepositoryShellViewModel (Core coordinator)
- Overall navigation
- Child VM composition
- Status message
- Repository loading/switching

**Responsibilities:**
- `CurrentRepository` property
- `StatusMessage` property
- Child ViewModel references
- Repository changed events

#### BranchOperationsViewModel
- Branch checkout
- Branch creation
- Branch deletion
- Branch renaming
- Branch listing refresh

**Move these commands:**
- `CheckoutBranchCommand`
- `CreateBranchCommand`
- `DeleteBranchCommand`
- `RenameBranchCommand`

#### RemoteSyncViewModel
- Fetch operations
- Pull operations
- Push operations
- Remote management

**Move these commands:**
- `FetchCommand`
- `PullCommand`
- `PushCommand`

#### StashViewModel
- Stash creation
- Stash application
- Stash deletion
- Stash listing

**Move these commands:**
- `StashCommand`
- `PopStashCommand`
- `ApplyStashCommand`
- `DropStashCommand`

#### GitFlowViewModel
- Feature start/finish
- Release start/finish
- Hotfix start/finish
- GitFlow initialization

**Move these commands:**
- All `StartFeature`, `FinishFeature`, etc.
- GitFlow configuration

#### MergeOperationsViewModel
- Merge initiation
- Conflict resolution coordination
- Rebase operations

**Move these commands:**
- `MergeCommand`
- `AbortMergeCommand`
- `ContinueMergeCommand`

---

## Target 2: GitService.cs (3181 lines)

### Current Responsibilities (Too Many)

1. Repository initialization/cloning
2. Commit history retrieval
3. Branch operations
4. Tag operations
5. Stash operations
6. Diff generation
7. Blame operations
8. Status retrieval
9. Remote operations
10. Configuration management

### Proposed Split

Already partially done with existing services:
- `IBranchService` / `BranchService`
- `ITagService` / `TagService`
- `IStashService` / `StashService`
- `ICommitHistoryService` / `CommitHistoryService`
- `IDiffService` / `DiffService`

#### Remaining in GitService
- Repository initialization
- Clone operations
- Basic git status
- Git configuration

#### GitHistoryService (extract)
- Commit log retrieval
- Blame operations
- Ancestry queries

#### GitRemoteService (extract)
- Remote listing
- Remote add/remove
- Fetch/pull/push internals

#### CommitPresentationBuilder (new - VM layer)
- Branch label formatting
- Tag display formatting
- Commit message truncation
- Author display formatting

**Key:** Keep GitService as a thin coordinator; move domain logic to focused services.

---

## Implementation Strategy

### Phase 4a: Extract Feature ViewModels from MainViewModel

1. Create `BranchOperationsViewModel`
2. Move branch commands and related state
3. MainViewModel holds reference to child VM
4. Update bindings in Views

### Phase 4b: Extract Workflow ViewModels

1. Create `GitFlowViewModel`
2. Create `MergeOperationsViewModel`
3. Move related commands and state
4. Update Views

### Phase 4c: Split GitService

1. Create `GitHistoryService`
2. Create `GitRemoteService`
3. Update DI registration
4. Update consumers

### Phase 4d: Presentation Layer

1. Create `CommitPresentationBuilder`
2. Move display formatting from GitService
3. Call from ViewModels instead of service

---

## Coordination Pattern

```csharp
public class RepositoryShellViewModel
{
    // Child ViewModels
    public BranchOperationsViewModel Branches { get; }
    public RemoteSyncViewModel RemoteSync { get; }
    public StashViewModel Stash { get; }
    public GitFlowViewModel GitFlow { get; }
    public MergeOperationsViewModel Merge { get; }

    // Shared state
    public IRepositorySession CurrentSession { get; }
    public string StatusMessage { get; set; }
}
```

Views bind to child VMs:
```xaml
<Button Command="{Binding Branches.CheckoutCommand}" />
<Button Command="{Binding RemoteSync.FetchCommand}" />
```

---

## Files to Create

**New ViewModels:**
- `src/Leaf/ViewModels/RepositoryShellViewModel.cs`
- `src/Leaf/ViewModels/BranchOperationsViewModel.cs`
- `src/Leaf/ViewModels/RemoteSyncViewModel.cs`
- `src/Leaf/ViewModels/StashViewModel.cs`
- `src/Leaf/ViewModels/GitFlowViewModel.cs`
- `src/Leaf/ViewModels/MergeOperationsViewModel.cs`

**New Services:**
- `src/Leaf/Services/IGitHistoryService.cs`
- `src/Leaf/Services/GitHistoryService.cs`
- `src/Leaf/Services/IGitRemoteService.cs`
- `src/Leaf/Services/GitRemoteService.cs`
- `src/Leaf/Presenters/CommitPresentationBuilder.cs`

---

## Files to Modify

- `src/Leaf/ViewModels/MainViewModel.cs` - Reduce to shell coordinator
- `src/Leaf/Services/GitService.cs` - Extract to focused services
- `src/Leaf/MainWindow.xaml.cs` - Update DI for new services/VMs
- `src/Leaf/Views/MainWindow.xaml` - Update bindings to child VMs
- Multiple Views that bind to MainViewModel commands

---

## Verification

1. `dotnet build Leaf.sln`
2. Test all branch operations
3. Test all remote sync operations
4. Test all stash operations
5. Test all GitFlow workflows
6. Test merge/conflict resolution
7. Test commit history display
8. Performance testing - ensure no regression

---

## Dependencies

- Requires Phase 1, 2, and 3 completion
- Largest scope - consider as separate milestone

---

## Risk Mitigation

- Extract one ViewModel at a time
- Maintain backwards compatibility during transition
- Extensive testing after each extraction
- Consider feature flags for gradual rollout
- Keep MainViewModel functional during refactor

---

## Success Metrics

- MainViewModel < 1000 lines
- GitService < 1000 lines
- Each new VM/service < 500 lines
- Single clear responsibility per class
- Improved testability (more focused unit tests)
