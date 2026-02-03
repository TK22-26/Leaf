using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit.Highlighting;
using Leaf.Models;
using Leaf.Services;
using System.Linq;

namespace Leaf.ViewModels;

/// <summary>
/// ViewModel for the diff viewer control.
/// </summary>
public partial class DiffViewerViewModel : ObservableObject
{
    public enum ViewerMode
    {
        Diff,
        Blame,
        History
    }

    private readonly IGitService _gitService;
    private readonly IHunkService _hunkService;
    private CancellationTokenSource? _loadCts;
    private int _loadSequence;

    public DiffViewerViewModel(IGitService gitService)
        : this(gitService, new HunkService())
    {
    }

    public DiffViewerViewModel(IGitService gitService, IHunkService hunkService)
    {
        _gitService = gitService;
        _hunkService = hunkService;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowFileInsights))]
    private FileDiffResult? _diffResult;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowFileInsights))]
    private string _filePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowFileInsights))]
    private string _repositoryPath = string.Empty;

    [ObservableProperty]
    private string _inlineContent = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<DiffLine> _lines = [];

    [ObservableProperty]
    private bool _isBinary;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ObservableCollection<FileBlameLine> _blameLines = [];

    [ObservableProperty]
    private ObservableCollection<FileBlameChunk> _blameChunks = [];

    [ObservableProperty]
    private string _blameContent = string.Empty;

    [ObservableProperty]
    private ObservableCollection<CommitInfo> _historyCommits = [];

    [ObservableProperty]
    private int _linesAdded;

    [ObservableProperty]
    private int _linesDeleted;

    [ObservableProperty]
    private IHighlightingDefinition? _syntaxHighlighting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiffMode))]
    [NotifyPropertyChangedFor(nameof(IsBlameMode))]
    [NotifyPropertyChangedFor(nameof(IsHistoryMode))]
    private ViewerMode _mode = ViewerMode.Diff;

    [ObservableProperty]
    private double _blameLineHeight = 18;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFullDiff))]
    [NotifyPropertyChangedFor(nameof(ShowHunkView))]
    private bool _isHunkMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowHunks))]
    private ObservableCollection<DiffHunk> _hunks = [];

    public bool IsDiffMode => Mode == ViewerMode.Diff;
    public bool IsBlameMode => Mode == ViewerMode.Blame;
    public bool IsHistoryMode => Mode == ViewerMode.History;

    /// <summary>
    /// True if hunk mode can be enabled (file has both old and new content - not a new or deleted file).
    /// </summary>
    public bool CanShowHunks => IsDiffMode && !IsBinary && DiffResult != null &&
                                !string.IsNullOrEmpty(DiffResult.OldContent) &&
                                !string.IsNullOrEmpty(DiffResult.NewContent);

    /// <summary>
    /// True when showing full inline diff (diff mode, not hunk mode).
    /// </summary>
    public bool ShowFullDiff => IsDiffMode && !IsHunkMode && !IsBinary;

    /// <summary>
    /// True when showing hunk-collapsed view.
    /// </summary>
    public bool ShowHunkView => IsDiffMode && IsHunkMode && !IsBinary;

    public bool CanShowFileInsights => !string.IsNullOrWhiteSpace(RepositoryPath) &&
                                       DiffResult?.IsFileBacked == true &&
                                       !string.IsNullOrWhiteSpace(FilePath);

    /// <summary>
    /// Event raised when the diff viewer should be closed.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Event raised when a hunk has been reverted.
    /// </summary>
    public event EventHandler<DiffHunk>? HunkReverted;

    /// <summary>
    /// Load a diff result into the viewer.
    /// </summary>
    public void LoadDiff(FileDiffResult result)
    {
        DiffResult = result;
        FileName = result.FileName;
        FilePath = result.FilePath;
        InlineContent = result.InlineContent;
        Lines = result.Lines;
        IsBinary = result.IsBinary;
        LinesAdded = result.LinesAddedCount;
        LinesDeleted = result.LinesDeletedCount;
        Mode = ViewerMode.Diff;
        IsHunkMode = false;
        BlameLines = [];
        BlameChunks = [];
        BlameContent = string.Empty;
        HistoryCommits = [];

        // Parse hunks for the diff
        if (!result.IsBinary && !string.IsNullOrEmpty(result.OldContent) && !string.IsNullOrEmpty(result.NewContent))
        {
            var parsedHunks = _hunkService.ParseHunks(result);
            Hunks = new ObservableCollection<DiffHunk>(parsedHunks);
        }
        else
        {
            Hunks = [];
        }

        // Set syntax highlighting based on file extension
        var extension = Path.GetExtension(result.FileName);
        SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(extension);

        // Notify property changes for computed properties
        OnPropertyChanged(nameof(CanShowHunks));
        OnPropertyChanged(nameof(ShowFullDiff));
        OnPropertyChanged(nameof(ShowHunkView));
    }

    /// <summary>
    /// Clear the current diff.
    /// </summary>
    public void Clear()
    {
        DiffResult = null;
        FileName = string.Empty;
        FilePath = string.Empty;
        RepositoryPath = string.Empty;
        InlineContent = string.Empty;
        Lines = [];
        IsBinary = false;
        LinesAdded = 0;
        LinesDeleted = 0;
        SyntaxHighlighting = null;
        BlameLines = [];
        BlameChunks = [];
        BlameContent = string.Empty;
        HistoryCommits = [];
        Hunks = [];
        IsHunkMode = false;
        Mode = ViewerMode.Diff;
    }

    [RelayCommand]
    private void Close()
    {
        CancelActiveLoad();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ShowDiff()
    {
        CancelActiveLoad();
        IsLoading = false;
        Mode = ViewerMode.Diff;
        Debug.WriteLine("[DiffViewer] Mode=Diff (cancel active load).");
    }

    [RelayCommand]
    private void ToggleHunkMode()
    {
        if (!CanShowHunks)
            return;

        IsHunkMode = !IsHunkMode;
        Debug.WriteLine($"[DiffViewer] HunkMode={IsHunkMode}");
    }

    /// <summary>
    /// Revert a specific hunk.
    /// </summary>
    [RelayCommand]
    public async Task RevertHunkAsync(DiffHunk hunk)
    {
        if (string.IsNullOrEmpty(RepositoryPath) || string.IsNullOrEmpty(FilePath) || DiffResult == null)
            return;

        try
        {
            IsLoading = true;
            var patch = _hunkService.GenerateHunkPatch(FilePath, hunk);
            await _gitService.RevertHunkAsync(RepositoryPath, patch);
            HunkReverted?.Invoke(this, hunk);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DiffViewer] RevertHunk failed: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ShowBlameAsync()
    {
        if (!CanShowFileInsights)
            return;

        var token = ResetActiveLoad();
        Mode = ViewerMode.Blame;

        try
        {
            IsLoading = true;
            var loadId = Interlocked.Increment(ref _loadSequence);
            var sw = Stopwatch.StartNew();
            Debug.WriteLine($"[DiffViewer] Blame start #{loadId} path={FilePath}");

            var lines = await _gitService.GetFileBlameAsync(RepositoryPath, FilePath);
            if (token.IsCancellationRequested)
            {
                Debug.WriteLine($"[DiffViewer] Blame canceled #{loadId}");
                return;
            }
            MarkBlameChunks(lines);
            BlameLines = new ObservableCollection<FileBlameLine>(lines);
            BlameChunks = new ObservableCollection<FileBlameChunk>(BuildBlameChunks(lines));
            BlameContent = string.Join('\n', lines.Select(l => l.Content));

            sw.Stop();
            Debug.WriteLine($"[DiffViewer] Blame done #{loadId} lines={lines.Count} ms={sw.ElapsedMilliseconds}");
        }
        finally
        {
            if (IsActiveToken(token))
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    private async Task ShowHistoryAsync()
    {
        if (!CanShowFileInsights)
            return;

        var token = ResetActiveLoad();
        Mode = ViewerMode.History;

        try
        {
            IsLoading = true;
            var loadId = Interlocked.Increment(ref _loadSequence);
            var sw = Stopwatch.StartNew();
            Debug.WriteLine($"[DiffViewer] History start #{loadId} path={FilePath}");

            var commits = await _gitService.GetFileHistoryAsync(RepositoryPath, FilePath);
            if (token.IsCancellationRequested)
            {
                Debug.WriteLine($"[DiffViewer] History canceled #{loadId}");
                return;
            }
            HistoryCommits = new ObservableCollection<CommitInfo>(commits);

            sw.Stop();
            Debug.WriteLine($"[DiffViewer] History done #{loadId} commits={commits.Count} ms={sw.ElapsedMilliseconds}");
        }
        finally
        {
            if (IsActiveToken(token))
            {
                IsLoading = false;
            }
        }
    }

    private static void MarkBlameChunks(IReadOnlyList<FileBlameLine> lines)
    {
        string? lastSha = null;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            line.IsChunkStart = !string.Equals(line.Sha, lastSha, StringComparison.OrdinalIgnoreCase);
            line.IsChunkEnd = i == lines.Count - 1 ||
                              !string.Equals(line.Sha, lines[i + 1].Sha, StringComparison.OrdinalIgnoreCase);
            lastSha = line.Sha;
        }
    }

    private static List<FileBlameChunk> BuildBlameChunks(IReadOnlyList<FileBlameLine> lines)
    {
        var chunks = new List<FileBlameChunk>();
        FileBlameChunk? current = null;

        foreach (var line in lines)
        {
            if (current == null || !string.Equals(current.Sha, line.Sha, StringComparison.OrdinalIgnoreCase))
            {
                if (current != null)
                {
                    chunks.Add(current);
                }

                current = new FileBlameChunk
                {
                    Sha = line.Sha,
                    Author = line.Author,
                    Date = line.Date,
                    LineCount = 1
                };
            }
            else
            {
                current.LineCount++;
            }
        }

        if (current != null)
        {
            chunks.Add(current);
        }

        return chunks;
    }

    private CancellationToken ResetActiveLoad()
    {
        CancelActiveLoad();
        _loadCts = new CancellationTokenSource();
        return _loadCts.Token;
    }

    private void CancelActiveLoad()
    {
        if (_loadCts != null)
        {
            _loadCts.Cancel();
            _loadCts.Dispose();
            _loadCts = null;
        }
    }

    private bool IsActiveToken(CancellationToken token)
    {
        return _loadCts != null && _loadCts.Token == token;
    }
}
