using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.ViewModels;

/// <summary>
/// Event arguments for file selection events.
/// </summary>
public class FileSelectedEventArgs : EventArgs
{
    public FileStatusInfo File { get; }
    public bool IsStaged { get; }

    public FileSelectedEventArgs(FileStatusInfo file, bool isStaged)
    {
        File = file;
        IsStaged = isStaged;
    }
}

/// <summary>
/// ViewModel for the working changes staging area view.
/// Handles staging, unstaging, discarding, and committing files.
/// </summary>
public partial class WorkingChangesViewModel : ObservableObject
{
    private readonly IGitService _gitService;
    private readonly IClipboardService _clipboardService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IDialogService _dialogService;
    private readonly IAiCommitMessageService _aiCommitService;
    private readonly IGitignoreService _gitignoreService;
    private readonly SettingsService _settingsService;
    private string? _repositoryPath;
    private CancellationTokenSource? _aiCancellationTokenSource;

    [ObservableProperty]
    private bool _showUnstagedTreeView;

    [ObservableProperty]
    private bool _showStagedTreeView;

    [ObservableProperty]
    private bool _isUnstagedExpanded = true;

    [ObservableProperty]
    private bool _isStagedExpanded = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    [NotifyPropertyChangedFor(nameof(FileChangesSummary))]
    private WorkingChangesInfo? _workingChanges;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemainingChars))]
    [NotifyPropertyChangedFor(nameof(CanCommit))]
    private string _commitMessage = string.Empty;

    [ObservableProperty]
    private string _commitDescription = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isAiAvailable;

    [ObservableProperty]
    private ObservableCollection<PathTreeNode> _unstagedTreeItems = [];

    [ObservableProperty]
    private ObservableCollection<PathTreeNode> _stagedTreeItems = [];

    [ObservableProperty]
    private FileChangesSectionContext? _unstagedSectionContext;

    [ObservableProperty]
    private FileChangesSectionContext? _stagedSectionContext;

    /// <summary>
    /// Event raised when a file is selected for diff viewing.
    /// </summary>
    public event EventHandler<FileSelectedEventArgs>? FileSelected;

    /// <summary>
    /// Maximum characters for commit message.
    /// </summary>
    public const int MaxMessageLength = 72;
    private const int MaxSummaryChars = 400000;

    /// <summary>
    /// Remaining characters for commit message.
    /// </summary>
    public int RemainingChars => MaxMessageLength - CommitMessage.Length;

    /// <summary>
    /// True if there are any working changes.
    /// </summary>
    public bool HasChanges => WorkingChanges?.HasChanges ?? false;

    /// <summary>
    /// True if can commit (has staged files and non-empty message).
    /// </summary>
    public bool CanCommit =>
        WorkingChanges?.HasStagedChanges == true &&
        !string.IsNullOrWhiteSpace(CommitMessage) &&
        CommitMessage.Length <= MaxMessageLength;

    /// <summary>
    /// Summary of file changes for display.
    /// </summary>
    public string FileChangesSummary
    {
        get
        {
            if (WorkingChanges == null)
                return "No changes";

            var total = WorkingChanges.TotalChanges;
            var branch = WorkingChanges.BranchName;

            return total switch
            {
                0 => "No changes",
                1 => $"1 file change on {branch}",
                _ => $"{total} file changes on {branch}"
            };
        }
    }

    public WorkingChangesViewModel(
        IGitService gitService,
        IClipboardService clipboardService,
        IFileSystemService fileSystemService,
        IDialogService dialogService,
        IAiCommitMessageService aiCommitService,
        IGitignoreService gitignoreService,
        SettingsService settingsService)
    {
        _gitService = gitService;
        _clipboardService = clipboardService;
        _fileSystemService = fileSystemService;
        _dialogService = dialogService;
        _aiCommitService = aiCommitService;
        _gitignoreService = gitignoreService;
        _settingsService = settingsService;
        RefreshAiAvailability();
    }

    /// <summary>
    /// Refresh whether any AI provider is connected.
    /// </summary>
    public void RefreshAiAvailability()
    {
        var settings = _settingsService.LoadSettings();
        IsAiAvailable = settings.IsClaudeConnected
                        || settings.IsGeminiConnected
                        || settings.IsCodexConnected
                        || !string.IsNullOrEmpty(settings.OllamaSelectedModel);
    }

    /// <summary>
    /// Set the repository path and refresh working changes.
    /// </summary>
    public async Task SetRepositoryAsync(string? repoPath)
    {
        _repositoryPath = repoPath;
        await RefreshAsync();
    }

    /// <summary>
    /// Set the working changes directly (synced from GitGraphViewModel).
    /// </summary>
    public void SetWorkingChanges(string repoPath, WorkingChangesInfo? workingChanges)
    {
        _repositoryPath = repoPath;
        WorkingChanges = workingChanges;

        // Debug: show what data we received
        if (workingChanges == null)
        {
            ErrorMessage = "SetWorkingChanges: null data received";
        }
        else
        {
            ErrorMessage = $"Synced: {workingChanges.UnstagedFiles.Count} unstaged, {workingChanges.StagedFiles.Count} staged";
        }

        // Force notification for dependent properties
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(FileChangesSummary));
    }

    /// <summary>
    /// Refresh working changes from the repository.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
        {
            WorkingChanges = null;
            ErrorMessage = "No repository path set";
            return;
        }

        try
        {
            IsLoading = true;
            ErrorMessage = null;
            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            ErrorMessage = $"Loaded: {WorkingChanges?.TotalChanges ?? 0} changes";
        }
        catch (Exception ex)
        {
            WorkingChanges = null;
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnWorkingChangesChanged(WorkingChangesInfo? value)
    {
        UnstagedTreeItems = BuildTree(value?.UnstagedFiles ?? []);
        StagedTreeItems = BuildTree(value?.StagedFiles ?? []);
        BuildSectionContexts();
    }

    /// <summary>
    /// Creates or updates the section context objects with current data and commands.
    /// </summary>
    private void BuildSectionContexts()
    {
        var isCompact = _settingsService.LoadSettings().CompactFileList;

        UnstagedSectionContext = new FileChangesSectionContext
        {
            SectionTitle = "Unstaged",
            IsStagedSection = false,
            FilesSource = WorkingChanges?.UnstagedFiles ?? [],
            TreeItemsSource = UnstagedTreeItems,
            PrimaryActionCommand = StageFileCommand,
            PrimaryActionText = "Stage",
            BulkActionCommand = StageAllCommand,
            BulkActionText = "Stage All",
            DiscardFileCommand = DiscardFileCommand,
            IgnoreFileCommand = IgnoreFileCommand,
            IgnoreExtensionCommand = IgnoreExtensionCommand,
            IgnoreDirectoryCommand = IgnoreDirectoryCommand,
            StashFileCommand = StashFileCommand,
            OpenFileCommand = OpenFileCommand,
            OpenInExplorerCommand = OpenInExplorerCommand,
            CopyFilePathCommand = CopyFilePathCommand,
            DeleteFileCommand = DeleteFileCommand,
            AdminDeleteCommand = AdminDeleteReservedFileCommand,
            FileSelectedCommand = SelectUnstagedFileCommand,
            FolderPrimaryActionCommand = StageFolderCommand,
            FolderDiscardCommand = DiscardFolderCommand,
            FolderIgnoreCommand = IgnoreFolderCommand,
            FolderOpenInExplorerCommand = OpenFolderInExplorerCommand,
            IsCompactFileList = isCompact
        };

        StagedSectionContext = new FileChangesSectionContext
        {
            SectionTitle = "Staged",
            IsStagedSection = true,
            FilesSource = WorkingChanges?.StagedFiles ?? [],
            TreeItemsSource = StagedTreeItems,
            PrimaryActionCommand = UnstageFileCommand,
            PrimaryActionText = "Unstage",
            BulkActionCommand = UnstageAllCommand,
            BulkActionText = "Unstage All",
            DiscardFileCommand = DiscardFileCommand,
            IgnoreFileCommand = IgnoreFileCommand,
            IgnoreExtensionCommand = IgnoreExtensionCommand,
            IgnoreDirectoryCommand = IgnoreDirectoryCommand,
            StashFileCommand = StashFileCommand,
            OpenFileCommand = OpenFileCommand,
            OpenInExplorerCommand = OpenInExplorerCommand,
            CopyFilePathCommand = CopyFilePathCommand,
            DeleteFileCommand = DeleteFileCommand,
            AdminDeleteCommand = null, // Not applicable for staged files
            FileSelectedCommand = SelectStagedFileCommand,
            FolderPrimaryActionCommand = UnstageFolderCommand,
            FolderDiscardCommand = DiscardFolderCommand,
            FolderIgnoreCommand = IgnoreFolderCommand,
            FolderOpenInExplorerCommand = OpenFolderInExplorerCommand,
            IsCompactFileList = isCompact
        };
    }

    /// <summary>
    /// Rebuild section contexts to pick up changed settings (e.g., compact file list).
    /// </summary>
    public void RefreshSectionContexts()
    {
        if (WorkingChanges != null)
            BuildSectionContexts();
    }

    /// <summary>
    /// Command to select an unstaged file for diff viewing.
    /// </summary>
    [RelayCommand]
    private void SelectUnstagedFile(FileStatusInfo? file)
    {
        if (file != null)
        {
            FileSelected?.Invoke(this, new FileSelectedEventArgs(file, isStaged: false));
        }
    }

    /// <summary>
    /// Command to select a staged file for diff viewing.
    /// </summary>
    [RelayCommand]
    private void SelectStagedFile(FileStatusInfo? file)
    {
        if (file != null)
        {
            FileSelected?.Invoke(this, new FileSelectedEventArgs(file, isStaged: true));
        }
    }

    /// <summary>
    /// Refreshes working changes and notifies dependent properties.
    /// </summary>
    private async Task RefreshAndNotifyAsync()
    {
        WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath!);
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(FileChangesSummary));
    }

    /// <summary>
    /// Stage a single file.
    /// </summary>
    [RelayCommand]
    public async Task StageFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        try
        {
            await _gitService.StageFileAsync(_repositoryPath, file.Path);
            await RefreshAndNotifyAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Stage failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Unstage a single file.
    /// </summary>
    [RelayCommand]
    public async Task UnstageFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        try
        {
            await _gitService.UnstageFileAsync(_repositoryPath, file.Path);
            await RefreshAndNotifyAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unstage failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Stage all modified files.
    /// </summary>
    [RelayCommand]
    public async Task StageAllAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
            return;

        try
        {
            await _gitService.StageAllAsync(_repositoryPath);
            await RefreshAndNotifyAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Stage all failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Unstage all staged files.
    /// </summary>
    [RelayCommand]
    public async Task UnstageAllAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
            return;

        try
        {
            await _gitService.UnstageAllAsync(_repositoryPath);
            await RefreshAndNotifyAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unstage all failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Discard changes for a single file.
    /// </summary>
    [RelayCommand]
    public async Task DiscardFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            $"Are you sure you want to discard changes to '{file.FileName}'?\n\nThis cannot be undone.",
            "Discard Changes");

        if (!confirmed)
            return;

        try
        {
            await _gitService.DiscardFileChangesAsync(_repositoryPath, file.Path);
            await RefreshAndNotifyAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Discard failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Add a specific file to .gitignore.
    /// </summary>
    [RelayCommand]
    public async Task IgnoreFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        try
        {
            await _gitignoreService.IgnoreFileAsync(_repositoryPath, file);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ignore failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Add all files with a specific extension to .gitignore.
    /// </summary>
    [RelayCommand]
    public async Task IgnoreExtensionAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null || string.IsNullOrEmpty(file.Extension))
            return;

        try
        {
            await _gitignoreService.IgnoreExtensionAsync(_repositoryPath, file);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ignore extension failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Add all files in a specific directory to .gitignore.
    /// </summary>
    [RelayCommand]
    public async Task IgnoreDirectoryAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null || string.IsNullOrEmpty(file.Directory))
            return;

        try
        {
            await _gitignoreService.IgnoreDirectoryAsync(_repositoryPath, file);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ignore directory failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Stash a single file.
    /// </summary>
    [RelayCommand]
    public async Task StashFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        try
        {
            // Stage the file first, then stash only staged changes
            await _gitService.StageFileAsync(_repositoryPath, file.Path);
            await _gitService.StashStagedAsync(_repositoryPath, $"Stash: {file.FileName}");
            await RefreshAndNotifyAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Stash failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Open the file's containing folder in Windows Explorer and select the file.
    /// </summary>
    [RelayCommand]
    public void OpenInExplorer(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        // Normalize path separators (Git uses forward slashes, worktree paths may too)
        var normalizedFilePath = file.Path.Replace('/', '\\');
        var fullPath = Path.GetFullPath(Path.Combine(_repositoryPath, normalizedFilePath));

        if (File.Exists(fullPath))
        {
            // Open Explorer and select the file
            _fileSystemService.OpenInExplorerAndSelect(fullPath);
        }
        else if (Directory.Exists(fullPath))
        {
            // Open the directory
            _fileSystemService.OpenInExplorer(fullPath);
        }
        else
        {
            // File doesn't exist (deleted), open the containing folder
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                _fileSystemService.RevealInExplorer(directory);
            }
        }
    }

    /// <summary>
    /// Open the file using the default associated application.
    /// </summary>
    [RelayCommand]
    public void OpenFile(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        var normalizedFilePath = file.Path.Replace('/', '\\');
        var fullPath = Path.GetFullPath(Path.Combine(_repositoryPath, normalizedFilePath));
        if (!File.Exists(fullPath))
            return;

        _fileSystemService.OpenWithDefaultApp(fullPath);
    }

    private static ObservableCollection<PathTreeNode> BuildTree(IEnumerable<FileStatusInfo> files)
    {
        var roots = new ObservableCollection<PathTreeNode>();
        var dirLookup = new Dictionary<string, PathTreeNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
        {
            var normalized = file.Path.Replace('\\', '/');
            var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            PathTreeNode? parent = null;
            var currentPath = string.Empty;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";
                bool isFile = i == parts.Length - 1;

                if (isFile)
                {
                    var fileNode = new PathTreeNode(part, currentPath, isFile: true, file, isRoot: parent == null);
                    if (parent == null)
                    {
                        roots.Add(fileNode);
                    }
                    else
                    {
                        parent.Children.Add(fileNode);
                    }
                }
                else
                {
                    if (!dirLookup.TryGetValue(currentPath, out var dirNode))
                    {
                        dirNode = new PathTreeNode(part, currentPath, isFile: false);
                        dirLookup[currentPath] = dirNode;

                        if (parent == null)
                        {
                            roots.Add(dirNode);
                        }
                        else
                        {
                            parent.Children.Add(dirNode);
                        }
                    }

                    parent = dirNode;
                }
            }
        }

        SortNodes(roots);
        return roots;
    }

    private static void SortNodes(ObservableCollection<PathTreeNode> nodes)
    {
        var sorted = nodes
            .OrderBy(n => n.IsFile)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        nodes.Clear();
        foreach (var node in sorted)
        {
            nodes.Add(node);
            if (!node.IsFile && node.Children.Count > 0)
            {
                SortNodes(node.Children);
            }
        }
    }

    /// <summary>
    /// Copy the file's full path to the clipboard.
    /// </summary>
    [RelayCommand]
    public void CopyFilePath(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        var normalizedFilePath = file.Path.Replace('/', '\\');
        var fullPath = Path.GetFullPath(Path.Combine(_repositoryPath, normalizedFilePath));
        _clipboardService.SetText(fullPath);
    }

    /// <summary>
    /// Delete a file from the filesystem.
    /// </summary>
    [RelayCommand]
    public async Task DeleteFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            $"Are you sure you want to delete '{file.FileName}'?\n\nThis will permanently delete the file from disk and cannot be undone.",
            "Delete File");

        if (!confirmed)
            return;

        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(_repositoryPath, file.Path));
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            else if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }

            await RefreshAndNotifyAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Delete failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Delete a Windows reserved filename (nul, con, prn, etc.) using admin privileges.
    /// These files cannot be deleted normally due to Windows restrictions.
    /// </summary>
    [RelayCommand]
    public async Task AdminDeleteReservedFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            $"Delete reserved file '{file.FileName}'?\n\nThis requires administrator privileges and will run a command to rename and delete the file.",
            "Admin Delete");

        if (!confirmed)
            return;

        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(_repositoryPath, file.Path));
            var directory = Path.GetDirectoryName(fullPath) ?? _repositoryPath;
            var tempName = $"_leaf_temp_{Guid.NewGuid():N}.tmp";

            // Build the batch script to rename and delete the reserved file
            // Uses \\?\ prefix to bypass Windows reserved name restrictions
            var script = $@"
@echo off
cd /d ""{directory}""
ren ""\\?\{fullPath}"" ""{tempName}""
del ""{Path.Combine(directory, tempName)}""
exit /b %errorlevel%
";

            var batchFile = Path.Combine(Path.GetTempPath(), $"leaf_admin_delete_{Guid.NewGuid():N}.bat");
            await File.WriteAllTextAsync(batchFile, script);

            // Run with admin privileges
            var startInfo = new ProcessStartInfo
            {
                FileName = batchFile,
                Verb = "runas", // Request admin elevation
                UseShellExecute = true,
                CreateNoWindow = false
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();

                // Clean up batch file
                try { File.Delete(batchFile); } catch { }

                if (process.ExitCode == 0)
                {
                    await RefreshAndNotifyAsync();
                }
                else
                {
                    ErrorMessage = $"Admin delete failed with exit code {process.ExitCode}";
                }
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User cancelled UAC prompt
            ErrorMessage = "Admin delete cancelled by user.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Admin delete failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Discard all working directory changes.
    /// </summary>
    [RelayCommand]
    public async Task DiscardAllAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
            return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Are you sure you want to discard all changes? This cannot be undone.",
            "Discard All Changes");

        if (!confirmed)
            return;

        try
        {
            await _gitService.DiscardAllChangesAsync(_repositoryPath);
            await RefreshAndNotifyAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Discard failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Commit staged changes.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCommit))]
    public async Task CommitAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath) || !CanCommit)
            return;

        try
        {
            IsLoading = true;

            var description = string.IsNullOrWhiteSpace(CommitDescription)
                ? null
                : CommitDescription.Trim();

            await _gitService.CommitAsync(_repositoryPath, CommitMessage.Trim(), description);

            // Clear form after successful commit
            CommitMessage = string.Empty;
            CommitDescription = string.Empty;

            await RefreshAndNotifyAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Commit failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task AutoFillCommitMessageAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
        {
            ErrorMessage = "No repository selected.";
            return;
        }

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            // Cancel any existing AI generation
            _aiCancellationTokenSource?.Cancel();
            _aiCancellationTokenSource?.Dispose();
            _aiCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _aiCancellationTokenSource.Token;

            Debug.WriteLine($"[WorkingChanges] AutoFill start: repo={_repositoryPath}");

            // Get staged diff summary
            var summary = await _gitService.GetStagedSummaryAsync(_repositoryPath);
            if (summary.Length > MaxSummaryChars)
            {
                ErrorMessage = $"Staged summary is too large to send ({summary.Length} chars).";
                Debug.WriteLine($"[WorkingChanges] AutoFill blocked: summary length {summary.Length} exceeds limit {MaxSummaryChars}.");
                return;
            }

            Debug.WriteLine($"[WorkingChanges] AutoFill summary length: {summary.Length}");

            var (message, description, error) = await _aiCommitService.GenerateCommitMessageAsync(
                summary, _repositoryPath, cancellationToken);

            if (error != null)
            {
                ErrorMessage = error;
                return;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                ErrorMessage = "AI returned an empty commit message.";
                return;
            }

            message = message.Trim();
            if (message.Length > MaxMessageLength)
            {
                message = message[..MaxMessageLength].TrimEnd();
                ErrorMessage = $"AI message trimmed to {MaxMessageLength} characters.";
            }

            CommitMessage = message;
            CommitDescription = description?.Trim() ?? string.Empty;
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "AI generation cancelled.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"AI commit failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            _aiCancellationTokenSource?.Dispose();
            _aiCancellationTokenSource = null;
        }
    }

    /// <summary>
    /// Cancels any in-progress AI commit message generation.
    /// </summary>
    [RelayCommand]
    public void CancelAutoFill()
    {
        _aiCancellationTokenSource?.Cancel();
    }

    partial void OnCommitMessageChanged(string value)
    {
        // Notify CanCommit changed when message changes
        CommitCommand.NotifyCanExecuteChanged();
    }

    // --- Folder context menu commands ---

    /// <summary>
    /// Stage all files within a folder tree node.
    /// </summary>
    [RelayCommand]
    public async Task StageFolderAsync(PathTreeNode folder)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || folder == null)
            return;

        try
        {
            foreach (var file in folder.GetAllFiles())
                await _gitService.StageFileAsync(_repositoryPath, file.Path);
            await RefreshAndNotifyAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Stage folder failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Unstage all files within a folder tree node.
    /// </summary>
    [RelayCommand]
    public async Task UnstageFolderAsync(PathTreeNode folder)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || folder == null)
            return;

        try
        {
            foreach (var file in folder.GetAllFiles())
                await _gitService.UnstageFileAsync(_repositoryPath, file.Path);
            await RefreshAndNotifyAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unstage folder failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Discard all changes in a folder tree node.
    /// </summary>
    [RelayCommand]
    public async Task DiscardFolderAsync(PathTreeNode folder)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || folder == null)
            return;

        var files = folder.GetAllFiles().ToList();
        var confirmed = await _dialogService.ShowConfirmationAsync(
            $"Discard all changes in '{folder.RelativePath}/'?\n\n{files.Count} file(s) will be reverted. This cannot be undone.",
            "Discard Folder Changes");

        if (!confirmed)
            return;

        try
        {
            foreach (var file in files)
                await _gitService.DiscardFileChangesAsync(_repositoryPath, file.Path);
            await RefreshAndNotifyAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Discard folder failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Add a folder to .gitignore.
    /// </summary>
    [RelayCommand]
    public async Task IgnoreFolderAsync(PathTreeNode folder)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || folder == null)
            return;

        try
        {
            var trackedFiles = folder.GetAllFiles()
                .Where(f => f.Status != FileChangeStatus.Untracked)
                .ToList();
            await _gitignoreService.IgnoreDirectoryPathAsync(_repositoryPath, folder.RelativePath, trackedFiles);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ignore folder failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Open a folder in Windows Explorer.
    /// </summary>
    [RelayCommand]
    public void OpenFolderInExplorer(PathTreeNode folder)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || folder == null)
            return;

        var normalizedPath = folder.RelativePath.Replace('/', '\\');
        var fullPath = Path.GetFullPath(Path.Combine(_repositoryPath, normalizedPath));

        if (Directory.Exists(fullPath))
            _fileSystemService.OpenInExplorer(fullPath);
        else
            _fileSystemService.RevealInExplorer(_repositoryPath);
    }
}
