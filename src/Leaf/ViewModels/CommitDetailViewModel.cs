using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.ViewModels;

/// <summary>
/// ViewModel for commit detail view with diff.
/// </summary>
public partial class CommitDetailViewModel : ObservableObject
{
    private readonly IGitService _gitService;
    private readonly IClipboardService _clipboardService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IExternalToolConfigService _externalToolConfig;
    private readonly IExternalToolLauncherService _externalToolLauncher;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasParent))]
    [NotifyPropertyChangedFor(nameof(ParentShortSha))]
    [NotifyPropertyChangedFor(nameof(CoAuthors))]
    [NotifyPropertyChangedFor(nameof(HasCoAuthors))]
    private CommitInfo? _commit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModifiedCount))]
    [NotifyPropertyChangedFor(nameof(AddedCount))]
    [NotifyPropertyChangedFor(nameof(DeletedCount))]
    [NotifyPropertyChangedFor(nameof(RenamedCount))]
    [NotifyPropertyChangedFor(nameof(TotalFileCount))]
    private ObservableCollection<FileChangeInfo> _fileChanges = [];

    [ObservableProperty]
    private FileChangeInfo? _selectedFile;

    [ObservableProperty]
    private string _oldContent = string.Empty;

    [ObservableProperty]
    private string _newContent = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isDiffLoading;

    [ObservableProperty]
    private string? _repositoryPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkingChanges))]
    private int _workingChangesCount;

    [ObservableProperty]
    private bool _showTreeView;

    [ObservableProperty]
    private ObservableCollection<FileChangeTreeNode> _fileChangesTreeItems = [];

    [ObservableProperty]
    private bool _isCompactFileList;

    [ObservableProperty]
    private bool _showAllFiles;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenInExternalDiffToolCommand))]
    private bool _hasExternalDiffTool;

    private List<FileChangeInfo>? _changedFiles;

    /// <summary>
    /// True if there are working changes to display in banner.
    /// </summary>
    public bool HasWorkingChanges => WorkingChangesCount > 0;

    /// <summary>
    /// True if the commit has a parent.
    /// </summary>
    public bool HasParent => Commit?.ParentShas.Count > 0;

    public List<CommitInfo.CoAuthorInfo> CoAuthors => Commit?.CoAuthors ?? [];

    public bool HasCoAuthors => CoAuthors.Count > 0;

    /// <summary>
    /// Short SHA of the first parent commit.
    /// </summary>
    public string ParentShortSha => Commit?.ParentShas.Count > 0
        ? Commit.ParentShas[0][..Math.Min(7, Commit.ParentShas[0].Length)]
        : string.Empty;

    /// <summary>
    /// Count of modified files.
    /// </summary>
    public int? ModifiedCount
    {
        get
        {
            var count = FileChanges.Count(f => f.Status == FileChangeStatus.Modified);
            return count > 0 ? count : null;
        }
    }

    /// <summary>
    /// Count of added files.
    /// </summary>
    public int? AddedCount
    {
        get
        {
            var count = FileChanges.Count(f => f.Status == FileChangeStatus.Added);
            return count > 0 ? count : null;
        }
    }

    /// <summary>
    /// Count of deleted files.
    /// </summary>
    public int? DeletedCount
    {
        get
        {
            var count = FileChanges.Count(f => f.Status == FileChangeStatus.Deleted);
            return count > 0 ? count : null;
        }
    }

    /// <summary>
    /// Count of renamed files.
    /// </summary>
    public int? RenamedCount
    {
        get
        {
            var count = FileChanges.Count(f => f.Status == FileChangeStatus.Renamed);
            return count > 0 ? count : null;
        }
    }

    /// <summary>
    /// Total count of changed files.
    /// </summary>
    public int TotalFileCount => FileChanges.Count;

    /// <summary>
    /// Event raised when user wants to navigate to parent commit.
    /// </summary>
    public event EventHandler<string>? NavigateToCommitRequested;

    /// <summary>
    /// Event raised when user wants to select working changes.
    /// </summary>
    public event EventHandler? SelectWorkingChangesRequested;

    /// <summary>
    /// Returns the current repository's cancellation token. Set by
    /// MainViewModel so this VM's background git calls abort when the
    /// session is disposed on repo switch.
    /// </summary>
    public Func<CancellationToken>? GetSessionToken { get; set; }

    private CancellationToken SessionToken => GetSessionToken?.Invoke() ?? CancellationToken.None;

    public CommitDetailViewModel(
        IGitService gitService,
        IClipboardService clipboardService,
        IFileSystemService fileSystemService,
        IExternalToolConfigService externalToolConfig,
        IExternalToolLauncherService externalToolLauncher,
        SettingsService settingsService)
    {
        _gitService = gitService;
        _clipboardService = clipboardService;
        _fileSystemService = fileSystemService;
        _externalToolConfig = externalToolConfig;
        _externalToolLauncher = externalToolLauncher;
        IsCompactFileList = settingsService.LoadSettings().CompactFileList;
    }

    /// <summary>
    /// Clear all commit detail state (used when no repository is selected).
    /// </summary>
    public void ClearSelection()
    {
        Commit = null;
        FileChanges.Clear();
        FileChangesTreeItems.Clear();
        SelectedFile = null;
        OldContent = string.Empty;
        NewContent = string.Empty;
        RepositoryPath = null;
        WorkingChangesCount = 0;
        IsLoading = false;
        IsDiffLoading = false;
    }

    /// <summary>
    /// Load commit details.
    /// </summary>
    public async Task LoadCommitAsync(string repoPath, string sha)
    {
        try
        {
            IsLoading = true;
            RepositoryPath = repoPath;
            await RefreshExternalDiffToolAvailabilityAsync();

            // Clear existing data
            FileChanges.Clear();
            OldContent = string.Empty;
            NewContent = string.Empty;
            SelectedFile = null;

            // Load commit info
            Commit = await _gitService.GetCommitAsync(repoPath, sha, cancellationToken: SessionToken);

            // Load file changes
            ShowAllFiles = false;
            var changes = await _gitService.GetCommitChangesAsync(repoPath, sha, cancellationToken: SessionToken);
            _changedFiles = changes;
            foreach (var change in changes)
            {
                FileChanges.Add(change);
            }

            // Notify counts changed and rebuild tree
            OnPropertyChanged(nameof(ModifiedCount));
            OnPropertyChanged(nameof(AddedCount));
            OnPropertyChanged(nameof(DeletedCount));
            OnPropertyChanged(nameof(RenamedCount));
            OnPropertyChanged(nameof(TotalFileCount));
            FileChangesTreeItems = BuildTree(FileChanges);

            // Auto-select first file
            if (FileChanges.Count > 0)
            {
                SelectedFile = FileChanges[0];
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Load stash details (treats stash as a commit for display).
    /// </summary>
    public async Task LoadStashAsync(string repoPath, StashInfo stash)
    {
        try
        {
            IsLoading = true;
            RepositoryPath = repoPath;

            // Clear existing data
            FileChanges.Clear();
            OldContent = string.Empty;
            NewContent = string.Empty;
            SelectedFile = null;

            // Create a synthetic commit info for the stash
            Commit = new CommitInfo
            {
                Sha = stash.Sha,
                Message = stash.Message,
                MessageShort = stash.MessageShort,
                Author = stash.Author,
                AuthorEmail = string.Empty,
                Date = stash.Date,
                ParentShas = []
            };

            // Load file changes from the stash commit
            var changes = await _gitService.GetCommitChangesAsync(repoPath, stash.Sha, cancellationToken: SessionToken);
            foreach (var change in changes)
            {
                FileChanges.Add(change);
            }

            // Notify counts changed and rebuild tree
            OnPropertyChanged(nameof(ModifiedCount));
            OnPropertyChanged(nameof(AddedCount));
            OnPropertyChanged(nameof(DeletedCount));
            OnPropertyChanged(nameof(RenamedCount));
            OnPropertyChanged(nameof(TotalFileCount));
            FileChangesTreeItems = BuildTree(FileChanges);

            // Auto-select first file
            if (FileChanges.Count > 0)
            {
                SelectedFile = FileChanges[0];
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Update working changes count for banner display.
    /// </summary>
    public void UpdateWorkingChangesCount(int count)
    {
        WorkingChangesCount = count;
    }

    partial void OnShowAllFilesChanged(bool value)
    {
        if (value)
        {
            ShowTreeView = true;
            LoadAllFilesAsync().FireAndForget(nameof(LoadAllFilesAsync), isUserAction: true);
        }
        else
        {
            // Restore changed-files-only view
            FileChanges.Clear();
            if (_changedFiles != null)
            {
                foreach (var change in _changedFiles)
                    FileChanges.Add(change);
            }
            OnPropertyChanged(nameof(TotalFileCount));
            FileChangesTreeItems = BuildTree(FileChanges);
        }
    }

    private async Task LoadAllFilesAsync()
    {
        if (string.IsNullOrEmpty(RepositoryPath) || Commit == null)
            return;

        try
        {
            IsLoading = true;
            var allFiles = await _gitService.GetCommitAllFilesAsync(RepositoryPath, Commit.Sha, cancellationToken: SessionToken);
            FileChanges.Clear();
            foreach (var file in allFiles)
                FileChanges.Add(file);
            OnPropertyChanged(nameof(TotalFileCount));
            FileChangesTreeItems = BuildTree(FileChanges);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Load diff for selected file.
    /// </summary>
    partial void OnSelectedFileChanged(FileChangeInfo? value)
    {
        if (value != null && !string.IsNullOrEmpty(RepositoryPath) && Commit != null)
        {
            LoadDiffAsync(value).FireAndForget(nameof(LoadDiffAsync), isUserAction: true);
        }
    }

    private async Task LoadDiffAsync(FileChangeInfo file)
    {
        try
        {
            IsDiffLoading = true;

            if (string.IsNullOrEmpty(RepositoryPath) || Commit == null)
                return;

            var (oldContent, newContent) = await _gitService.GetFileDiffAsync(
                RepositoryPath, Commit.Sha, file.Path, cancellationToken: SessionToken);

            OldContent = oldContent;
            NewContent = newContent;
        }
        finally
        {
            IsDiffLoading = false;
        }
    }

    /// <summary>
    /// Copy SHA to clipboard.
    /// </summary>
    [RelayCommand]
    public void CopySha()
    {
        if (Commit != null)
        {
            _clipboardService.SetText(Commit.Sha);
        }
    }

    /// <summary>
    /// Open file in Windows Explorer.
    /// </summary>
    [RelayCommand]
    public void OpenInExplorer(FileChangeInfo? file)
    {
        if (string.IsNullOrEmpty(RepositoryPath) || file == null)
            return;

        // Normalize path separators (Git uses forward slashes, worktree paths may too)
        var normalizedFilePath = file.Path.Replace('/', '\\');
        var fullPath = Path.GetFullPath(Path.Combine(RepositoryPath, normalizedFilePath));

        if (File.Exists(fullPath))
        {
            _fileSystemService.OpenInExplorerAndSelect(fullPath);
        }
        else if (Directory.Exists(fullPath))
        {
            _fileSystemService.OpenInExplorer(fullPath);
        }
        else
        {
            // File doesn't exist (maybe deleted), open the containing directory
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                _fileSystemService.RevealInExplorer(directory);
            }
        }
    }

    /// <summary>
    /// Copy file path to clipboard.
    /// </summary>
    [RelayCommand]
    public void CopyFilePath(FileChangeInfo? file)
    {
        if (string.IsNullOrEmpty(RepositoryPath) || file == null)
            return;

        var normalizedFilePath = file.Path.Replace('/', '\\');
        var fullPath = Path.GetFullPath(Path.Combine(RepositoryPath, normalizedFilePath));
        _clipboardService.SetText(fullPath);
    }

    /// <summary>
    /// Diff the file's parent-revision content against this commit's
    /// revision in the user's configured external diff tool. We write
    /// both sides to temp files rather than point the tool at the
    /// working tree, because the commit being viewed may not be HEAD.
    /// Disabled when no external diff tool is configured.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasExternalDiffTool))]
    public async Task OpenInExternalDiffToolAsync(FileChangeInfo? file)
    {
        if (string.IsNullOrEmpty(RepositoryPath) || Commit == null || file == null)
            return;

        try
        {
            var diffTool = await _externalToolConfig.GetCurrentToolAsync(
                RepositoryPath, ExternalToolKind.Diff, SessionToken);
            if (diffTool == null)
            {
                HasExternalDiffTool = false;
                return;
            }

            var (oldContent, newContent) = await _gitService.GetFileDiffAsync(
                RepositoryPath, Commit.Sha, file.Path, cancellationToken: SessionToken);

            var tempDir = Path.Combine(Path.GetTempPath(), "LeafDiff", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            var fileName = Path.GetFileName(file.Path);
            var extension = Path.GetExtension(file.Path);
            var leftPath = Path.Combine(tempDir, $"{fileName}.before{extension}");
            var rightPath = Path.Combine(tempDir, $"{fileName}.after{extension}");

            try
            {
                await File.WriteAllTextAsync(leftPath, oldContent, SessionToken);
                await File.WriteAllTextAsync(rightPath, newContent, SessionToken);
                await _externalToolLauncher.LaunchDiffAsync(diffTool, leftPath, rightPath, SessionToken);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Warn("ExternalDiff", $"Failed to clean up temp directory: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                or IOException
                                or UnauthorizedAccessException
                                or System.ComponentModel.Win32Exception
                                or OperationCanceledException)
        {
            Log.Warn("ExternalDiff", $"Open diff in external tool failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-check whether an external diff tool is configured for this
    /// repository. Called on repo switch and after the Settings dialog
    /// closes so the menu item's enabled state matches git config.
    /// </summary>
    public async Task RefreshExternalDiffToolAvailabilityAsync()
    {
        if (string.IsNullOrEmpty(RepositoryPath))
        {
            HasExternalDiffTool = false;
            return;
        }

        try
        {
            var tool = await _externalToolConfig.GetCurrentToolAsync(
                RepositoryPath, ExternalToolKind.Diff, SessionToken);
            HasExternalDiffTool = tool != null;
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                or OperationCanceledException)
        {
            Log.Info("ExternalDiff", $"Availability probe failed: {ex.Message}");
            HasExternalDiffTool = false;
        }
    }

    /// <summary>
    /// Navigate to parent commit.
    /// </summary>
    public void NavigateToParent()
    {
        if (Commit?.ParentShas.Count > 0)
        {
            NavigateToCommitRequested?.Invoke(this, Commit.ParentShas[0]);
        }
    }

    /// <summary>
    /// Select working changes view.
    /// </summary>
    public void SelectWorkingChanges()
    {
        SelectWorkingChangesRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnFileChangesChanged(ObservableCollection<FileChangeInfo> value)
    {
        FileChangesTreeItems = BuildTree(value ?? []);
    }

    private static ObservableCollection<FileChangeTreeNode> BuildTree(IEnumerable<FileChangeInfo> files)
    {
        var roots = new ObservableCollection<FileChangeTreeNode>();
        var dirLookup = new Dictionary<string, FileChangeTreeNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
        {
            var normalized = file.Path.Replace('\\', '/');
            var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            FileChangeTreeNode? parent = null;
            var currentPath = string.Empty;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";
                bool isFile = i == parts.Length - 1;

                if (isFile)
                {
                    var fileNode = new FileChangeTreeNode(part, currentPath, isFile: true, file, isRoot: parent == null);
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
                        dirNode = new FileChangeTreeNode(part, currentPath, isFile: false);
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

    private static void SortNodes(ObservableCollection<FileChangeTreeNode> nodes)
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
}
