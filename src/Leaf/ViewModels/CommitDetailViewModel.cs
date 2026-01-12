using System.Collections.ObjectModel;
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

            // Notify counts changed
            OnPropertyChanged(nameof(ModifiedCount));
            OnPropertyChanged(nameof(AddedCount));
            OnPropertyChanged(nameof(DeletedCount));

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

            // Notify counts changed
            OnPropertyChanged(nameof(ModifiedCount));
            OnPropertyChanged(nameof(AddedCount));
            OnPropertyChanged(nameof(DeletedCount));

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
}
