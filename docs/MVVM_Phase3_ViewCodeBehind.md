# MVVM Phase 3: View Code-Behind Cleanup

**Status:** Planned
**Scope:** Move business logic from View code-behind to ViewModels/Services
**Risk:** Medium-High - significant refactor of GitGraphView

---

## Objective

Reduce code-behind to pure UI wiring. Move domain decisions, state transitions, and complex UI construction to ViewModels and services.

---

## Primary Target: GitGraphView.xaml.cs (782 lines)

This view has the most MVVM violations with business logic embedded in code-behind.

### A. Context Menu Construction (Lines 387-506)

**Current:** Entire context menu dynamically built in code-behind with conditional logic.

**Issues:**
- Creates MenuItem objects programmatically
- Icons hardcoded as TextBlocks with Fluent icon codes
- Conditional logic for branch reset based on current branch
- Merge option filtering based on BranchLabels
- Direct command binding to MainViewModel

**Solution:**
1. Create `CommitContextMenuViewModel` with menu item collection
2. Add `ObservableCollection<MenuItemViewModel>` to `GitGraphViewModel`
3. Bind context menu via XAML `ItemsSource` with `DataTemplate`
4. Move conditional visibility logic to ViewModel properties

**New classes:**
```csharp
public class MenuItemViewModel
{
    public string Header { get; set; }
    public string? IconGlyph { get; set; }
    public ICommand Command { get; set; }
    public object? CommandParameter { get; set; }
    public bool IsVisible { get; set; }
    public bool IsSeparator { get; set; }
    public ObservableCollection<MenuItemViewModel>? SubItems { get; set; }
}
```

---

### B. Tooltip Lifecycle (Lines 508-697)

**Current:** ~190 lines managing tooltip state, timers, and content building.

**Issues:**
- Tooltip state tracked with `_openTooltips` HashSet
- Timer-based tooltip close logic
- Manual tooltip view creation and content binding
- Loading state UI building
- Mouse interaction coordination

**Solution:**
1. Create `IGraphTooltipService` interface
2. Move tooltip building to service
3. ViewModel exposes `TooltipViewModel` property
4. View binds to tooltip visibility and content

**New interface:**
```csharp
public interface IGraphTooltipService
{
    Task<object?> BuildTooltipContentAsync(CommitInfo commit);
    object BuildLoadingContent();
}
```

---

### C. Selection Handlers (Lines 172-256)

**Current:** Mouse event handlers call ViewModel methods directly.

**Methods:**
- `CommitItem_MouseLeftButtonDown` → `viewModel.SelectCommit()`
- `WorkingChangesRow_MouseLeftButtonDown` → `viewModel.SelectWorkingChanges()`
- `StashItem_MouseLeftButtonDown` → `viewModel.SelectStash()`

**Solution:**
- Convert to `Command` bindings in XAML where possible
- Use `InputBindings` for mouse actions
- Keep minimal routing in code-behind if XAML binding is impractical

---

### D. Hover State Management (Lines 183-287)

**Current:** Manual brush assignments and canvas state synchronization.

**Solution:**
- ViewModel properties: `HoveredCommitSha`, `IsWorkingChangesHovered`, `IsStashHovered`
- XAML triggers handle brush changes based on state
- Remove direct resource lookups from code-behind

---

### E. Scroll-to-Selection Logic (Lines 125-170)

**Current:** Complex scroll calculation with linear search and viewport math.

**Solution:**
- ViewModel calculates target scroll position
- View just calls `ScrollToVerticalOffset` based on ViewModel property
- Or use attached behavior

---

### F. Load-More Infinite Scroll (Lines 739-780)

**Current:** Scroll threshold logic and debounce timer in code-behind.

**Solution:**
- Create `InfiniteScrollBehavior` attached behavior
- Behavior exposes `ThresholdReachedCommand`
- ViewModel handles load-more logic

---

## Secondary Target: WorkingChangesView.xaml.cs (90 lines)

Mostly clean, minor fixes needed.

### A. Tree Visibility Toggle (Lines 57-88)

**Current:** Toggle Changed event handlers update Visibility.

**Solution:**
- Remove event handlers
- Bind Visibility directly in XAML: `Visibility="{Binding ShowUnstagedTreeView, Converter=...}"`

### B. File Selection Routing (Lines 26-48)

**Current:** Click events route to MainViewModel via code-behind.

**Solution:**
- Bind to commands on WorkingChangesViewModel
- Use `MouseBinding` or `EventToCommand` behavior

### C. Empty Event Handler (Lines 50-55)

**Current:** `FileItem_MouseRightButtonDown` does nothing.

**Solution:** Remove the handler and XAML event binding.

---

## New Files

- `src/Leaf/Services/IGraphTooltipService.cs`
- `src/Leaf/Services/GraphTooltipService.cs`
- `src/Leaf/ViewModels/MenuItemViewModel.cs`
- `src/Leaf/Behaviors/InfiniteScrollBehavior.cs` (optional)

---

## Files to Modify

- `src/Leaf/Views/GitGraphView.xaml` - Add command bindings, ItemsSource for context menu
- `src/Leaf/Views/GitGraphView.xaml.cs` - Remove business logic (~500+ lines reduction target)
- `src/Leaf/ViewModels/GitGraphViewModel.cs` - Add tooltip/menu properties
- `src/Leaf/Views/WorkingChangesView.xaml` - Direct visibility binding
- `src/Leaf/Views/WorkingChangesView.xaml.cs` - Remove toggle handlers

---

## Implementation Order

1. Create `MenuItemViewModel` class
2. Move context menu building to ViewModel
3. Create `IGraphTooltipService`
4. Move tooltip building to service
5. Convert selection handlers to commands
6. Add XAML triggers for hover states
7. Clean up WorkingChangesView
8. Optional: Create scroll behavior

---

## Verification

1. `dotnet build Leaf.sln`
2. Test commit context menu (all items, conditional items)
3. Test tooltips (loading state, merge commit tooltip, timing)
4. Test selection (commit, working changes, stash)
5. Test hover highlighting
6. Test infinite scroll loading
7. Test tree view toggle in Working Changes
8. Code-behind should only have UI wiring, no domain logic

---

## Dependencies

- Requires Phase 1 and Phase 2 completion
- Larger scope - consider splitting into sub-phases

---

## Risk Mitigation

- Test each sub-change independently
- Keep old code commented initially until verified
- GitGraphView is complex - allow extra testing time
