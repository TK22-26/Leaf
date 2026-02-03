# MVVM Refactor Plan - Overview

This document provides an overview of the MVVM refactoring effort for the Leaf project. The refactor is divided into four phases, each with detailed implementation plans.

## Phase Documents

| Phase | Document | Scope | Risk |
|-------|----------|-------|------|
| 1 | [MVVM_Phase1_ServiceAbstractions.md](MVVM_Phase1_ServiceAbstractions.md) | Create IClipboardService, IFileSystemService | Low |
| 2 | [MVVM_Phase2_DialogDispatcher.md](MVVM_Phase2_DialogDispatcher.md) | Use existing IDialogService, IDispatcherService | Medium |
| 3 | [MVVM_Phase3_ViewCodeBehind.md](MVVM_Phase3_ViewCodeBehind.md) | Move logic from View code-behind | Medium-High |
| 4 | [MVVM_Phase4_ClassSplitting.md](MVVM_Phase4_ClassSplitting.md) | Split MainViewModel, GitService | High |

## Current State

**Infrastructure already exists:**
- `IDialogService` / `DialogService` - fully implemented
- `IDispatcherService` / `DispatcherService` - fully implemented
- `IWindowService` - fully implemented
- DI composition root in `MainWindow.xaml.cs`

**Violations identified:**
| ViewModel | MessageBox | Dispatcher | Clipboard | Process |
|-----------|-----------|------------|-----------|---------|
| MainViewModel (3113 lines) | 15+ | 6+ | 1 | - |
| WorkingChangesViewModel (1471 lines) | 4 | - | 1 | 4+ |
| CommitDetailViewModel (428 lines) | - | - | 2 | 3 |
| ConflictResolutionViewModel (902 lines) | - | 1 | 1 | - |

**View code-behind issues:**
- GitGraphView.xaml.cs (782 lines) - Heavy business logic
- WorkingChangesView.xaml.cs (90 lines) - Minor issues

---

## Phase 1 Summary: Service Abstractions

**Goal:** Create missing service interfaces for Clipboard and File System operations.

## Deliverables

### 1. IClipboardService

**New files:**
- `src/Leaf/Services/IClipboardService.cs`
- `src/Leaf/Services/ClipboardService.cs`

**Interface:**
```csharp
public interface IClipboardService
{
    void SetText(string text);
    string? GetText();
}

public class ClipboardService : IClipboardService
{
    public void SetText(string text) => System.Windows.Clipboard.SetText(text);
    public string? GetText() => System.Windows.Clipboard.GetText();
}

// For unit testing
public class TestClipboardService : IClipboardService
{
    public string? LastSetText { get; private set; }
    public string? TextToReturn { get; set; }

    public void SetText(string text) => LastSetText = text;
    public string? GetText() => TextToReturn;
}
```

**Violations to fix:**
| File | Line | Current Code |
|------|------|--------------|
| MainViewModel.cs | 2384 | `Clipboard.SetText(commit.Sha)` |
| WorkingChangesViewModel.cs | 541 | `Clipboard.SetText(fullPath)` |
| CommitDetailViewModel.cs | 278 | `System.Windows.Clipboard.SetText(Commit.Sha)` |
| CommitDetailViewModel.cs | 325 | `System.Windows.Clipboard.SetText(fullPath)` |
| ConflictResolutionViewModel.cs | 331 | `Clipboard.SetText(content)` |

---

### 2. IFileSystemService

**New files:**
- `src/Leaf/Services/IFileSystemService.cs`
- `src/Leaf/Services/FileSystemService.cs`

**Interface:**
```csharp
public interface IFileSystemService
{
    void OpenInExplorer(string path);
    void OpenInExplorerAndSelect(string filePath);
    void OpenWithDefaultApp(string filePath);
    void RevealInExplorer(string directoryPath);
}

public class FileSystemService : IFileSystemService
{
    public void OpenInExplorer(string path)
    {
        Process.Start("explorer.exe", $"\"{path}\"");
    }

    public void OpenInExplorerAndSelect(string filePath)
    {
        Process.Start("explorer.exe", $"/select,\"{filePath}\"");
    }

    public void OpenWithDefaultApp(string filePath)
    {
        Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
    }

    public void RevealInExplorer(string directoryPath)
    {
        Process.Start("explorer.exe", $"\"{directoryPath}\"");
    }
}

// For unit testing
public class TestFileSystemService : IFileSystemService
{
    public List<string> OpenedPaths { get; } = new();
    public List<string> SelectedFiles { get; } = new();
    public List<string> OpenedWithDefaultApp { get; } = new();

    public void OpenInExplorer(string path) => OpenedPaths.Add(path);
    public void OpenInExplorerAndSelect(string filePath) => SelectedFiles.Add(filePath);
    public void OpenWithDefaultApp(string filePath) => OpenedWithDefaultApp.Add(filePath);
    public void RevealInExplorer(string directoryPath) => OpenedPaths.Add(directoryPath);
}
```

**Violations to fix:**
| File | Line | Current Code |
|------|------|--------------|
| WorkingChangesViewModel.cs | 418 | `Process.Start("explorer.exe", $"/select,\"{fullPath}\"")` |
| WorkingChangesViewModel.cs | 423 | `Process.Start("explorer.exe", $"\"{fullPath}\"")` |
| WorkingChangesViewModel.cs | 431 | `Process.Start("explorer.exe", $"\"{directory}\"")` |
| WorkingChangesViewModel.cs | 450 | `Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true })` |
| CommitDetailViewModel.cs | 297 | `Process.Start("explorer.exe", $"/select,\"{fullPath}\"")` |
| CommitDetailViewModel.cs | 301 | `Process.Start("explorer.exe", $"\"{fullPath}\"")` |
| CommitDetailViewModel.cs | 309 | `Process.Start("explorer.exe", $"\"{directory}\"")` |

---

## DI Registration

**File:** `src/Leaf/MainWindow.xaml.cs`

Add to composition root (after existing service creation):
```csharp
// Phase 0 additions
var clipboardService = new ClipboardService();
var fileSystemService = new FileSystemService();
```

Update ViewModel constructors to accept new services.

---

## Files to Modify

**New files (4):**
- `src/Leaf/Services/IClipboardService.cs`
- `src/Leaf/Services/ClipboardService.cs`
- `src/Leaf/Services/IFileSystemService.cs`
- `src/Leaf/Services/FileSystemService.cs`

**Modified files (6):**
- `src/Leaf/MainWindow.xaml.cs` - DI registration
- `src/Leaf/ViewModels/MainViewModel.cs` - Add IClipboardService
- `src/Leaf/ViewModels/WorkingChangesViewModel.cs` - Add IClipboardService, IFileSystemService
- `src/Leaf/ViewModels/CommitDetailViewModel.cs` - Add IClipboardService, IFileSystemService
- `src/Leaf/ViewModels/ConflictResolutionViewModel.cs` - Add IClipboardService
- `src/Leaf/ViewModels/ViewModelFactory.cs` - Pass services to created VMs

---

## Implementation Order

1. Create `IClipboardService` and `ClipboardService`
2. Create `IFileSystemService` and `FileSystemService`
3. Register both in `MainWindow.xaml.cs`
4. Update `CommitDetailViewModel` (smallest, 2 clipboard + 3 process)
5. Update `ConflictResolutionViewModel` (1 clipboard only)
6. Update `WorkingChangesViewModel` (1 clipboard + 4 process)
7. Update `MainViewModel` (1 clipboard only for this phase)
8. Update `ViewModelFactory` if needed

---

## Verification

After implementation:
1. `dotnet build Leaf.sln` - must pass
2. Manual testing:
   - Copy commit SHA (right-click commit > Copy SHA)
   - Copy file path in Working Changes
   - Open file location in Explorer
   - Open file with default app
   - Copy merged content in Conflict Resolution
3. Verify no direct `Clipboard` or `Process.Start("explorer.exe"` in modified VMs

---

## Out of Scope (Future Phases)

- MessageBox.Show replacements with IDialogService (Phase 2)
- Application.Current.Dispatcher replacements (Phase 2)
- View code-behind cleanup (Phase 3)
- MainViewModel/GitService splitting (Phase 4)
