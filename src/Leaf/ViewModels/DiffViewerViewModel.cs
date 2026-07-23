using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.TextEdit.Highlighting;
using Leaf.Models;
using Leaf.Services;
using Leaf.Utils;
using System.Linq;

namespace Leaf.ViewModels;

/// <summary>
/// ViewModel for the diff viewer control.
/// </summary>
public partial class DiffViewerViewModel : ObservableObject, IDisposable
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

    /// <summary>
    /// Returns the current repository's cancellation token. Set by
    /// MainViewModel so this VM's background git calls abort when the
    /// session is disposed on repo switch.
    /// </summary>
    public Func<CancellationToken>? GetSessionToken { get; set; }

    private CancellationToken SessionToken => GetSessionToken?.Invoke() ?? CancellationToken.None;

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

    /// <summary>
    /// When the diff is a historical commit's file, the SHA of that
    /// commit. Blame / History scope to it (the path may not exist in
    /// HEAD). Null for working-copy / staged diffs, where blame targets
    /// the working tree / HEAD as before.
    /// </summary>
    public string? SourceCommitSha { get; set; }

    [ObservableProperty]
    private string _inlineContent = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<DiffLine> _lines = [];

    [ObservableProperty]
    private bool _isBinary;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Whether the X close button on the diff viewer's header is shown.
    /// Default true (matches the historical behaviour the standalone
    /// IsDiffViewerVisible takeover relies on). Embedded callers — like
    /// the bisect detail pane — set this to false because the diff
    /// viewer is part of a larger view, not a closeable overlay.
    /// </summary>
    [ObservableProperty]
    private bool _isCloseable = true;

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
    [NotifyPropertyChangedFor(nameof(ShowFullDiff))]
    [NotifyPropertyChangedFor(nameof(ShowHunkView))]
    [NotifyPropertyChangedFor(nameof(CanShowHunks))]
    [NotifyPropertyChangedFor(nameof(HasDiffNavigation))]
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

    // ─── Next/previous difference navigation (#35) ──────────────────────
    //
    // Mirrors the merge editor's conflict navigation: an index with
    // modulo wrap-around ("seen all" = the counter cycling back to
    // "1 of N"), driven by header buttons and F8 / Shift+F8. Parsed for
    // every non-binary diff — unlike the revert-capable Hunks view
    // collection, which stays gated on CanShowHunks — so add-only and
    // delete-only files navigate too.

    /// <summary>
    /// All change hunks of the current diff, in inline-document order.
    /// Same parse (and order) as <see cref="Hunks"/>, so Diff-mode and
    /// Hunks-mode navigation indices align 1:1.
    /// </summary>
    private IReadOnlyList<DiffHunk> _navigationHunks = [];

    /// <summary>
    /// Index of the current difference; -1 = not navigated yet. The
    /// first Next lands on 0, the first Previous wraps to the last.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiffPositionText))]
    [NotifyPropertyChangedFor(nameof(CurrentNavigationHunk))]
    private int _currentDiffIndex = -1;

    /// <summary>Total number of differences (hunks) in the current diff.</summary>
    public int DiffChangeCount => _navigationHunks.Count;

    /// <summary>Navigation UI is shown only in diff mode with at least one change.</summary>
    public bool HasDiffNavigation => IsDiffMode && !IsBinary && _navigationHunks.Count > 0;

    /// <summary>"N of M" once navigating; "M differences" before the first jump.</summary>
    public string DiffPositionText => CurrentDiffIndex < 0
        ? $"{DiffChangeCount} difference{(DiffChangeCount == 1 ? "" : "s")}"
        : $"{CurrentDiffIndex + 1} of {DiffChangeCount}";

    /// <summary>The hunk the view should scroll to; null before the first jump.</summary>
    public DiffHunk? CurrentNavigationHunk =>
        CurrentDiffIndex >= 0 && CurrentDiffIndex < _navigationHunks.Count
            ? _navigationHunks[CurrentDiffIndex]
            : null;

    [RelayCommand(CanExecute = nameof(CanNavigateDiff))]
    private void NextDifference()
        => CurrentDiffIndex = (CurrentDiffIndex + 1) % _navigationHunks.Count;

    [RelayCommand(CanExecute = nameof(CanNavigateDiff))]
    private void PreviousDifference()
        => CurrentDiffIndex = (Math.Max(CurrentDiffIndex, 0) - 1 + _navigationHunks.Count) % _navigationHunks.Count;

    private bool CanNavigateDiff() => _navigationHunks.Count > 0;

    private void ResetDiffNavigation(IReadOnlyList<DiffHunk> navigationHunks)
    {
        _navigationHunks = navigationHunks;
        CurrentDiffIndex = -1;
        OnPropertyChanged(nameof(DiffChangeCount));
        OnPropertyChanged(nameof(HasDiffNavigation));
        OnPropertyChanged(nameof(DiffPositionText));
        OnPropertyChanged(nameof(CurrentNavigationHunk));
        NextDifferenceCommand.NotifyCanExecuteChanged();
        PreviousDifferenceCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// True if hunk mode can be enabled (file has both old and new content - not a new or deleted file).
    /// </summary>
    public bool CanShowHunks => !IsBinary && DiffResult != null &&
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
        // Default to working-tree/HEAD blame; ShowFileDiffAsync sets a
        // commit SHA afterward when the diff is a historical commit's file.
        SourceCommitSha = null;
        BlameLines = [];
        BlameChunks = [];
        BlameContent = string.Empty;
        HistoryCommits = [];

        // Parse hunks once for every non-binary diff — navigation works
        // for add-only/delete-only files too. The revert-capable Hunks
        // view collection keeps its stricter both-sides-present gate
        // (reverting needs old AND new content); both come from the same
        // parse so navigation indices align with hunk cards 1:1.
        var parsedHunks = !result.IsBinary && result.Lines.Count > 0
            ? _hunkService.ParseHunks(result)
            : [];
        ResetDiffNavigation(parsedHunks);

        if (!result.IsBinary && !string.IsNullOrEmpty(result.OldContent) && !string.IsNullOrEmpty(result.NewContent))
        {
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
        SourceCommitSha = null;
        ResetDiffNavigation([]);
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
        IsHunkMode = false;
        Mode = ViewerMode.Diff;
        Log.Info("DiffViewer", "Mode=Diff (cancel active load)");
    }

    [RelayCommand]
    private void ShowHunks()
    {
        if (!CanShowHunks)
            return;

        CancelActiveLoad();
        IsLoading = false;
        IsHunkMode = true;
        Mode = ViewerMode.Diff;
        Log.Info("DiffViewer", "Mode=Hunks");
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
            await _gitService.RevertHunkAsync(RepositoryPath, patch, cancellationToken: SessionToken);
            HunkReverted?.Invoke(this, hunk);
        }
        catch (Exception ex)
        {
            Log.Error("DiffViewer", "RevertHunk failed", ex);
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
            Log.Info("DiffViewer", $"Blame start #{loadId} path={FilePath}");

            var lines = await _gitService.GetFileBlameAsync(RepositoryPath, FilePath, rev: SourceCommitSha, cancellationToken: SessionToken);
            if (token.IsCancellationRequested)
            {
                Log.Info("DiffViewer", $"Blame canceled #{loadId}");
                return;
            }
            MarkBlameChunks(lines);
            BlameLines = new ObservableCollection<FileBlameLine>(lines);
            BlameChunks = new ObservableCollection<FileBlameChunk>(BuildBlameChunks(lines));
            BlameContent = string.Join('\n', lines.Select(l => l.Content));

            sw.Stop();
            Log.Perf("DiffViewer", $"Blame done #{loadId} lines={lines.Count}", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load / repo switch — expected.
        }
        catch (Exception ex)
        {
            // git blame fails loudly for legitimate reasons — the file
            // isn't in HEAD (untracked / newly added), lives only in a
            // historical commit, or the diff's path doesn't resolve at
            // HEAD. Surface it in the pane instead of letting the fault
            // escape this async command; there is no dispatcher backstop
            // upstream and an unhandled fault here terminated the app.
            Log.Warn("DiffViewer", $"Blame failed for {FilePath}: {ex.Message}");
            if (IsActiveToken(token))
            {
                BlameLines = [];
                BlameChunks = [];
                BlameContent = $"Blame unavailable for this file.\n\n{ex.Message.Trim()}";
            }
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
            Log.Info("DiffViewer", $"History start #{loadId} path={FilePath}");

            var commits = await _gitService.GetFileHistoryAsync(RepositoryPath, FilePath, rev: SourceCommitSha, cancellationToken: SessionToken);
            if (token.IsCancellationRequested)
            {
                Log.Info("DiffViewer", $"History canceled #{loadId}");
                return;
            }
            HistoryCommits = new ObservableCollection<CommitInfo>(commits);

            sw.Stop();
            Log.Perf("DiffViewer", $"History done #{loadId} commits={commits.Count}", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load / repo switch — expected.
        }
        catch (Exception ex)
        {
            // Same rationale as ShowBlameAsync: a git failure must not
            // escape this async command and crash the app.
            Log.Warn("DiffViewer", $"History failed for {FilePath}: {ex.Message}");
            if (IsActiveToken(token))
            {
                HistoryCommits = [];
            }
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
        return CancellationTokenSourceExtensions.ReplaceAndCancel(ref _loadCts).Token;
    }

    private void CancelActiveLoad()
    {
        CancellationTokenSourceExtensions.DisposeAndClear(ref _loadCts);
    }

    private bool IsActiveToken(CancellationToken token)
    {
        return _loadCts != null && _loadCts.Token == token;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No finalizer on this type, so no GC.SuppressFinalize — the
        // standard pattern only requires it when a finalizer is present.
        CancellationTokenSourceExtensions.DisposeAndClear(ref _loadCts);
    }
}
