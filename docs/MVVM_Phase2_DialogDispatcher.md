# MVVM Phase 2: Dialog & Dispatcher Service Usage

**Status:** Planned
**Scope:** Replace direct `MessageBox` and `Application.Current.Dispatcher` with existing services
**Risk:** Medium - behavioral changes, requires testing all dialogs

---

## Objective

Route all `MessageBox.Show()` calls through `IDialogService` and all `Application.Current.Dispatcher` calls through `IDispatcherService`. Both services already exist.

---

## Existing Infrastructure

**Already implemented:**
- `IDialogService` / `DialogService` - fully functional
- `IDispatcherService` / `DispatcherService` - fully functional

**Current issue:** MainViewModel has these services injected but doesn't use them consistently. Other ViewModels don't have them injected at all.

---

## Violations to Fix

### MessageBox.Show (19+ locations)

#### MainViewModel.cs (15+ calls)
| Line | Purpose |
|------|---------|
| 838-860 | Update available dialog |
| 1503-1514 | Cannot delete current branch / Delete branch confirmation |
| 1522-1527 | Force delete branch confirmation |
| 2015-2019 | Various confirmations |
| 2271-2279 | Error dialogs |
| 2790-2791 | GitFlow error |
| 2818-2819 | GitFlow error |
| 2846-2847 | GitFlow error |
| 2874-2875 | GitFlow error |
| 2884-2885 | GitFlow error |
| 2912-2913 | GitFlow error |
| 2922-2923 | GitFlow error |
| 2953-2954 | GitFlow error |

#### WorkingChangesViewModel.cs (4 calls)
| Line | Purpose |
|------|---------|
| 281-285 | Discard file changes confirmation |
| 553-557 | Delete file confirmation |
| 594-598 | Admin delete confirmation |
| 702-706 | Discard all changes confirmation |

### Application.Current.Dispatcher (7+ locations)

#### MainViewModel.cs (6+ calls)
| Line | Purpose |
|------|---------|
| 306 | UI thread marshalling |
| 326 | UI thread marshalling |
| 368 | UI thread marshalling |
| 371 | UI thread marshalling |
| 1354 | UI thread marshalling |
| 1499 | Branch deletion dialog |
| 1520 | Force delete dialog |

#### ConflictResolutionViewModel.cs (1 call)
| Line | Purpose |
|------|---------|
| 229 | Update UI after background merge |

---

## IDialogService Methods

```csharp
public interface IDialogService
{
    Task<bool> ShowConfirmationAsync(string message, string title);
    Task<MessageBoxResult> ShowMessageAsync(string message, string title, MessageBoxButton buttons);
    Task ShowInformationAsync(string message, string title);
    Task ShowErrorAsync(string message, string title);
    Task<T?> ShowDialogAsync<T>(object viewModel);
    Task<string?> ShowInputAsync(string prompt, string title, string? defaultValue = null);
}
```

---

## Refactor Pattern

### Before (violation):
```csharp
var result = MessageBox.Show(
    "Delete branch?",
    "Confirm",
    MessageBoxButton.YesNo,
    MessageBoxImage.Warning);
if (result == MessageBoxResult.Yes) { ... }
```

### After (MVVM-compliant):
```csharp
var confirmed = await _dialogService.ShowConfirmationAsync(
    "Delete branch?",
    "Confirm");
if (confirmed) { ... }
```

### Dispatcher Before:
```csharp
Application.Current.Dispatcher.Invoke(() => { ... });
```

### Dispatcher After:
```csharp
_dispatcherService.Invoke(() => { ... });
// or
await _dispatcherService.InvokeAsync(() => { ... });
```

---

## ViewModel Updates

### MainViewModel.cs
- Already has `_dialogService` and `_dispatcherService` injected
- Replace all `MessageBox.Show()` with appropriate `_dialogService` methods
- Replace all `Application.Current.Dispatcher` with `_dispatcherService`

### WorkingChangesViewModel.cs
- Add `IDialogService` parameter to constructor
- Replace 4 `MessageBox.Show()` calls with `_dialogService` methods
- Update commands to be async where needed

### ConflictResolutionViewModel.cs
- Add `IDispatcherService` parameter to constructor
- Replace `Application.Current.Dispatcher.Invoke()` (line 229)

### ViewModelFactory.cs
- Update to pass `IDialogService` and `IDispatcherService` to ViewModels

---

## Files to Modify

- `src/Leaf/ViewModels/MainViewModel.cs`
- `src/Leaf/ViewModels/WorkingChangesViewModel.cs`
- `src/Leaf/ViewModels/ConflictResolutionViewModel.cs`
- `src/Leaf/ViewModels/ViewModelFactory.cs`
- `src/Leaf/MainWindow.xaml.cs` (if ViewModelFactory needs service references)

---

## Async Considerations

Converting `MessageBox.Show()` to `_dialogService.ShowConfirmationAsync()` makes the method async. This may require:

1. Changing synchronous command handlers to async
2. Updating `RelayCommand` to `AsyncRelayCommand` where needed
3. Ensuring proper exception handling for async void event handlers

---

## Verification

1. `dotnet build Leaf.sln` - must pass
2. Test all dialogs:
   - Branch deletion (current branch warning, normal delete, force delete)
   - File discard confirmation
   - File delete confirmation
   - Admin delete confirmation
   - Discard all changes confirmation
   - Update available dialog
   - All GitFlow error dialogs
3. Test conflict resolution UI updates properly after merge
4. Grep for violations: `MessageBox.Show`, `Application.Current.Dispatcher`

---

## Dependencies

- Requires Phase 1 completion (ViewModelFactory changes)
