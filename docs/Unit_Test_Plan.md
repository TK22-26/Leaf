# Unit Testing Suite Plan (Leaf)

> Goal: a comprehensive, incremental unit‑testing suite that maximizes coverage of core logic while isolating UI and Git dependencies. This plan lists test files, target methods, test doubles, and key scenarios.

## Testing Strategy

### 1) Prioritize pure logic and service boundaries
- Start with ViewModel logic and service helper methods that are deterministic.
- Avoid UI/Framework classes in unit tests; isolate with interfaces.
- Use fake implementations for Git and OS services.

### 2) Suggested frameworks
- Test runner: `xUnit` (or `NUnit` if repo already prefers it)
- Mocking: `Moq` or `NSubstitute`
- Assertions: `FluentAssertions`

### 3) Test Project Layout
- `tests/Leaf.Tests`
  - `ViewModels/`
  - `Services/`
  - `Graph/`
  - `Utils/`

---

## ViewModels

### `GitGraphViewModelTests`
File: `tests/Leaf.Tests/ViewModels/GitGraphViewModelTests.cs`

**Methods / scenarios**
- `LoadRepositoryAsync`
  - loads commits, stashes, working changes
  - sets `IsLoading` correctly
  - preserves working changes selection
- `ApplySearchFilter`
  - highlights matches by message, sha, author
  - clears highlighting when search is empty
- `SelectCommit` / `SelectCommitByIndex`
  - sets `SelectedCommit`, `SelectedSha`, `IsWorkingChangesSelected=false`
  - unselects previous commit
- `SelectWorkingChanges`
  - clears selected commit and stash
- `SelectStash`
  - selects stash, clears commit selection

**Dependencies to mock**
- `IGitService`
- `GraphBuilder` (wrap via interface if needed)

---

### `WorkingChangesViewModelTests`
File: `tests/Leaf.Tests/ViewModels/WorkingChangesViewModelTests.cs`

**Methods / scenarios**
- tree building behavior
  - root node handling
  - directories vs files
  - sort order (case‑insensitive)
- stage/unstage commands
  - stage single file
  - unstage single file
  - stage all / unstage all
- discard/ignore operations
  - ignore file
  - ignore extension
  - ignore directory

**Dependencies to mock**
- `IGitService`
- `IFileSystemService` (open/delete/copy path)
- `IDialogService`

---

### `CommitDetailViewModelTests`
File: `tests/Leaf.Tests/ViewModels/CommitDetailViewModelTests.cs`

**Methods / scenarios**
- `LoadCommitDetailsAsync`
  - loads commit + file changes
  - handles missing commit
- `CopyShaCommand`
  - calls clipboard service with correct SHA

---

### `MainViewModelTests`
File: `tests/Leaf.Tests/ViewModels/MainViewModelTests.cs`

**Methods / scenarios**
- `LoadRepositoryAsync`
  - sets selected repo
  - creates GitGraphViewModel
  - updates working changes view model
- command execution wiring
  - `FetchAllCommand`, `RefreshCommand`
  - GitFlow init guard conditions

**Dependencies to mock**
- `IGitService`, `IRepositoryManagementService`, `IDialogService`, `IDispatcherService`

---

## Services

### `GitServiceTests`
File: `tests/Leaf.Tests/Services/GitServiceTests.cs`

Focus on pure helpers:
- `FindNearestVisibleAncestor`
- `BuildBranchLabels`
- `GetBranchNameWithoutRemote`

Use fake repository data (avoid LibGit2Sharp in unit tests; integration tests can cover it).

---

### `CommitHistoryServiceTests`
File: `tests/Leaf.Tests/Services/CommitHistoryServiceTests.cs`

- Ensure pass‑through calls to `IGitService` are correct
- Verify cancellation token is checked

---

### `DialogServiceTests`
File: `tests/Leaf.Tests/Services/DialogServiceTests.cs`

- Use fake dispatcher service to verify UI thread marshalling
- Verify correct message box results

---

## Graph

### `GraphBuilderTests`
File: `tests/Leaf.Tests/Graph/GraphBuilderTests.cs`

- Builds graph for simple linear history
- Handles merges correctly
- Branch label placement

---

## Utils

### `IdenticonGeneratorTests`
File: `tests/Leaf.Tests/Utils/IdenticonGeneratorTests.cs`

- deterministic output for same input
- different output for different input

---

## Integration Tests (optional / future)

- `GitServiceIntegrationTests` with a real test repo fixture
- verify history retrieval, stash, diff, branch ops

---

## Suggested Test Doubles

- `FakeGitService` with in‑memory commits, stashes, working changes
- `FakeDialogService` for MessageBox flows
- `TestDispatcherService` already exists (use it)

---

## Implementation Order

1) `GitGraphViewModelTests`
2) `WorkingChangesViewModelTests`
3) `CommitDetailViewModelTests`
4) `GraphBuilderTests`
5) Service tests

---

## Notes
- Avoid UI thread dependencies in unit tests.
- Use `ObservableCollection` assertions carefully (compare by contents).


---

# xUnit Setup (Concrete)

## 1) Create test project
```
mkdir tests
cd tests
dotnet new xunit -n Leaf.Tests
```

## 2) Reference Leaf project
```
dotnet add Leaf.Tests/Leaf.Tests.csproj reference ..\src\Leaf\Leaf.csproj
```

## 3) Add packages
```
dotnet add Leaf.Tests/Leaf.Tests.csproj package Moq
# or: dotnet add Leaf.Tests/Leaf.Tests.csproj package NSubstitute

dotnet add Leaf.Tests/Leaf.Tests.csproj package FluentAssertions
```

## 4) Optional: treat warnings as errors in tests
Add to `tests/Leaf.Tests/Leaf.Tests.csproj`:
```xml
<PropertyGroup>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
</PropertyGroup>
```

## 5) Suggested test project layout
```
Leaf.Tests/
  ViewModels/
  Services/
  Graph/
  Utils/
  Fakes/
```

---

# File‑by‑File Test Opportunities + Skeletons

Below are the most valuable unit test targets with concrete skeletons.

## ViewModels

### `GitGraphViewModel` (`src/Leaf/ViewModels/GitGraphViewModel.cs`)
**Key tests**
- `LoadRepositoryAsync` sets `IsLoading`, updates `Commits`, `Nodes`, `Stashes`, `WorkingChanges`.
- `ApplySearchFilter` toggles `IsSearchActive`, `IsDimmed`, `IsSearchHighlighted`.
- `SelectCommit`, `SelectWorkingChanges`, `SelectStash` update selection flags correctly.

**Skeleton**
```csharp
public class GitGraphViewModelTests
{
    [Fact]
    public async Task LoadRepositoryAsync_LoadsCommitsAndStashes()
    {
        var git = new Mock<IGitService>();
        git.Setup(g => g.GetWorkingChangesAsync(It.IsAny<string>()))
           .ReturnsAsync(new WorkingChangesInfo());
        git.Setup(g => g.GetCommitHistoryAsync(It.IsAny<string>(), It.IsAny<int>(), null))
           .ReturnsAsync(new List<CommitInfo> { new CommitInfo { Sha = "a" } });
        git.Setup(g => g.GetStashesAsync(It.IsAny<string>()))
           .ReturnsAsync(new List<StashInfo>());

        var vm = new GitGraphViewModel(git.Object);
        await vm.LoadRepositoryAsync("repo");

        vm.Commits.Should().NotBeEmpty();
        vm.IsLoading.Should().BeFalse();
    }
}
```

### `WorkingChangesViewModel`
**Key tests**
- Tree builder creates folder hierarchy + root alignment
- Stage/Unstage commands call `IGitService`
- Ignore commands call correct service methods

**Skeleton**
```csharp
public class WorkingChangesViewModelTests
{
    [Fact]
    public void BuildTree_CreatesRootAndFolders()
    {
        // Arrange fake files
        // Act build tree
        // Assert nodes
    }
}
```

### `CommitDetailViewModel`
**Key tests**
- `LoadCommitDetailsAsync` returns data when commit exists
- `CopyShaCommand` uses clipboard service

### `MainViewModel`
**Key tests**
- `LoadRepositoryAsync` wires child VMs
- `FetchAllCommand` triggers GitService

---

## Services

### `GitService` (pure helpers)
Focus on **non‑LibGit2Sharp** helpers:
- `GetBranchNameWithoutRemote`
- `BuildBranchLabels` (given mock branch tip maps)
- `FindNearestVisibleAncestor` (can be tested with fake commit graph if decoupled)

### `CommitHistoryService`
- Pass‑through to `IGitService` with cancellation token checks

---

## Graph

### `GraphBuilder`
- Simple linear history
- Merge commit handling
- Max lane calculation

---

## Utils

### `IdenticonGenerator`
- Deterministic output for same input
- Different output for different input

---

# Additional Suggested Refactors for Testability

- Add `IClipboardService` for clipboard usage.
- Add `IFileSystemService` for open/delete/copy path.
- Add `IGraphTooltipService` for tooltip logic now in `GitGraphView.xaml.cs`.

---

# Example Fake Services

```csharp
public sealed class FakeClipboardService : IClipboardService
{
    public string? LastText { get; private set; }
    public void SetText(string text) => LastText = text;
}
```

---

# Suggested Test Order (Incremental)
1) GitGraphViewModel basic load + selection tests
2) WorkingChangesViewModel tree + commands
3) CommitDetailViewModel clipboard
4) GraphBuilder small graphs
5) CommitHistoryService pass‑through

