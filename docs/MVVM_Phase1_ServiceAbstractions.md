# MVVM Phase 1: Service Abstractions

**Status:** Planned
**Scope:** Create `IClipboardService` and `IFileSystemService`
**Risk:** Low - additive changes only

---

## Objective

Abstract direct `System.Windows.Clipboard` and `Process.Start` calls from ViewModels into testable service interfaces.

---

## New Services

### IClipboardService

**Files to create:**
- `src/Leaf/Services/IClipboardService.cs`
- `src/Leaf/Services/ClipboardService.cs`

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

public class TestClipboardService : IClipboardService
{
    public string? LastSetText { get; private set; }
    public string? TextToReturn { get; set; }

    public void SetText(string text) => LastSetText = text;
    public string? GetText() => TextToReturn;
}
```

### IFileSystemService

**Files to create:**
- `src/Leaf/Services/IFileSystemService.cs`
- `src/Leaf/Services/FileSystemService.cs`

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
        => Process.Start("explorer.exe", $"\"{path}\"");

    public void OpenInExplorerAndSelect(string filePath)
        => Process.Start("explorer.exe", $"/select,\"{filePath}\"");

    public void OpenWithDefaultApp(string filePath)
        => Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });

    public void RevealInExplorer(string directoryPath)
        => Process.Start("explorer.exe", $"\"{directoryPath}\"");
}

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

---

## Violations to Fix

### Clipboard (5 locations)

| File | Line | Current Code |
|------|------|--------------|
| MainViewModel.cs | 2384 | `Clipboard.SetText(commit.Sha)` |
| WorkingChangesViewModel.cs | 541 | `Clipboard.SetText(fullPath)` |
| CommitDetailViewModel.cs | 278 | `System.Windows.Clipboard.SetText(Commit.Sha)` |
| CommitDetailViewModel.cs | 325 | `System.Windows.Clipboard.SetText(fullPath)` |
| ConflictResolutionViewModel.cs | 331 | `Clipboard.SetText(content)` |

### Process.Start for Explorer (7 locations)

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

Add after existing service creation:
```csharp
var clipboardService = new ClipboardService();
var fileSystemService = new FileSystemService();
```

---

## ViewModel Updates

### MainViewModel.cs
- Add `IClipboardService` parameter to constructor
- Replace line 2384 with `_clipboardService.SetText(commit.Sha)`

### WorkingChangesViewModel.cs
- Add `IClipboardService`, `IFileSystemService` parameters to constructor
- Replace clipboard call (line 541)
- Replace 4 Process.Start calls (lines 418, 423, 431, 450)

### CommitDetailViewModel.cs
- Add `IClipboardService`, `IFileSystemService` parameters to constructor
- Replace 2 clipboard calls (lines 278, 325)
- Replace 3 Process.Start calls (lines 297, 301, 309)

### ConflictResolutionViewModel.cs
- Add `IClipboardService` parameter to constructor
- Replace clipboard call (line 331)

### ViewModelFactory.cs
- Update to accept and pass new services to created ViewModels

---

## Files Summary

**New files (4):**
- `src/Leaf/Services/IClipboardService.cs`
- `src/Leaf/Services/ClipboardService.cs`
- `src/Leaf/Services/IFileSystemService.cs`
- `src/Leaf/Services/FileSystemService.cs`

**Modified files (6):**
- `src/Leaf/MainWindow.xaml.cs`
- `src/Leaf/ViewModels/MainViewModel.cs`
- `src/Leaf/ViewModels/WorkingChangesViewModel.cs`
- `src/Leaf/ViewModels/CommitDetailViewModel.cs`
- `src/Leaf/ViewModels/ConflictResolutionViewModel.cs`
- `src/Leaf/ViewModels/ViewModelFactory.cs`

---

## Verification

1. `dotnet build Leaf.sln` - must pass
2. Test manually:
   - Right-click commit > Copy SHA
   - Right-click file > Copy Path
   - Right-click file > Open in Explorer
   - Right-click file > Open File
   - Conflict Resolution > Copy merged result
3. No direct `Clipboard` or `Process.Start("explorer` in modified VMs
