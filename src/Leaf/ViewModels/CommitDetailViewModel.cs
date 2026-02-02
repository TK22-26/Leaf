using System.Collections.ObjectModel;
using System.Diagnostics;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasParent))]
    [NotifyPropertyChangedFor(nameof(ParentShortSha))]
    private CommitInfo? _commit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModifiedCount))]
    [NotifyPropertyChangedFor(nameof(AddedCount))]
    [NotifyPropertyChangedFor(nameof(DeletedCount))]
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

    /// <summary>
    /// True if there are working changes to display in banner.
    /// </summary>
    public bool HasWorkingChanges => WorkingChangesCount > 0;

    /// <summary>
    /// True if the commit has a parent.
    /// </summary>
    public bool HasParent => Commit?.ParentShas.Count > 0;

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

    public CommitDetailViewModel(IGitService gitService)
    {
        _gitService = gitService;
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

            // Clear existing data
            FileChanges.Clear();
            OldContent = string.Empty;
            NewContent = string.Empty;
            SelectedFile = null;

            // Load commit info
            Commit = await _gitService.GetCommitAsync(repoPath, sha);

            // Load file changes
            var changes = await _gitService.GetCommitChangesAsync(repoPath, sha);
            foreach (var change in changes)
            {
                FileChanges.Add(change);
            }

            // Notify counts changed and rebuild tree
            OnPropertyChanged(nameof(ModifiedCount));
            OnPropertyChanged(nameof(AddedCount));
            OnPropertyChanged(nameof(DeletedCount));
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
            var changes = await _gitService.GetCommitChangesAsync(repoPath, stash.Sha);
            foreach (var change in changes)
            {
                FileChanges.Add(change);
            }

            // Notify counts changed and rebuild tree
            OnPropertyChanged(nameof(ModifiedCount));
            OnPropertyChanged(nameof(AddedCount));
            OnPropertyChanged(nameof(DeletedCount));
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

    /// <summary>
    /// Load diff for selected file.
    /// </summary>
    partial void OnSelectedFileChanged(FileChangeInfo? value)
    {
        if (value != null && !string.IsNullOrEmpty(RepositoryPath) && Commit != null)
        {
            _ = LoadDiffAsync(value);
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
                RepositoryPath, Commit.Sha, file.Path);

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
            System.Windows.Clipboard.SetText(Commit.Sha);
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

        // Normalize path separators (Git uses forward slashes)
        var normalizedFilePath = file.Path.Replace('/', '\\');
        var fullPath = Path.Combine(RepositoryPath, normalizedFilePath);

        if (File.Exists(fullPath))
        {
            Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
        }
        else if (Directory.Exists(fullPath))
        {
            Process.Start("explorer.exe", $"\"{fullPath}\"");
        }
        else
        {
            // File doesn't exist (maybe deleted), open the containing directory
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                Process.Start("explorer.exe", $"\"{directory}\"");
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
        var fullPath = Path.Combine(RepositoryPath, normalizedFilePath);
        System.Windows.Clipboard.SetText(fullPath);
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
