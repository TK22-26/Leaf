#nullable enable
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Models.Merge;
using Leaf.Services;
using Leaf.Services.Merge;
using Leaf.Utils;

namespace Leaf.ViewModels.Merge;

/// <summary>
/// ViewModel for the Phase 2c+ merge editor. Consumes <see cref="IMergeEngine"/>
/// directly — producing an immutable <see cref="MergeDocument"/> per conflict —
/// and tracks per-<see cref="ModifiedBaseRange"/> <see cref="ResolutionState"/>
/// in a dictionary. Replaces the pre-Phase-2c <c>ConflictResolutionViewModel</c>
/// whose per-line selection model and 1125-line surface no longer match the
/// new data model.
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle:
/// <list type="number">
/// <item><description><see cref="LoadConflictsAsync"/> populates <see cref="Conflicts"/>.</description></item>
/// <item><description>Selecting a conflict fires <see cref="OnSelectedConflictChanged"/>,
/// which calls <see cref="BuildDocumentForSelectedAsync"/> to run the engine
/// and populate <see cref="Document"/> + <see cref="RangeStates"/>.</description></item>
/// <item><description>User interactions (accept-ours / accept-theirs / accept-both /
/// manual-edit) mutate <see cref="RangeStates"/> through the resolution commands
/// which push onto <see cref="_undoStack"/>.</description></item>
/// <item><description>The composed text (<see cref="ComposedText"/>) is produced by
/// <see cref="MergeDocument.ComposeResolvedText"/> each time a state changes.</description></item>
/// <item><description><see cref="MarkResolvedAsync"/> writes the composed text to
/// disk and stages the file via <see cref="IGitService.MarkConflictResolvedAsync"/>,
/// then auto-advances.</description></item>
/// <item><description><see cref="CompleteMergeAsync"/> commits the merge when
/// every file is resolved.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed partial class MergeEditorViewModel : ObservableObject, IDisposable
{
    private readonly IGitService _gitService;
    private readonly IClipboardService _clipboardService;
    private readonly IMergeEngine _engine;
    private readonly string _repoPath;

    private readonly Stack<ResolutionUndoEntry> _undoStack = new();
    private readonly Stack<ResolutionUndoEntry> _redoStack = new();
    private CancellationTokenSource? _buildCts;
    private string? _lastBuiltFilePath;

    /// <summary>
    /// Per-range resolution state keyed by <see cref="ModifiedBaseRange.Index"/>.
    /// A range with no entry (or with <see cref="ResolutionState.Unresolved"/>) is treated
    /// as unresolved for display and commit purposes.
    /// </summary>
    public Dictionary<int, ResolutionState> RangeStates { get; } = new();

    public MergeEditorViewModel(
        IGitService gitService,
        IClipboardService clipboardService,
        IMergeEngine engine,
        string repoPath)
    {
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _repoPath = repoPath ?? throw new ArgumentNullException(nameof(repoPath));
    }

    public event EventHandler<bool>? MergeCompleted;

    /// <summary>Set by the host VM; returns a token cancelled when the repo session ends.</summary>
    public Func<CancellationToken>? GetSessionToken { get; set; }
    private CancellationToken SessionToken => GetSessionToken?.Invoke() ?? CancellationToken.None;

    [ObservableProperty]
    private string _sourceBranch = string.Empty;

    [ObservableProperty]
    private string _targetBranch = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ConflictInfo> _conflicts = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedConflict))]
    [NotifyPropertyChangedFor(nameof(CanMarkResolved))]
    private ConflictInfo? _selectedConflict;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocument))]
    [NotifyPropertyChangedFor(nameof(ConflictCount))]
    [NotifyPropertyChangedFor(nameof(UnresolvedConflictCount))]
    [NotifyPropertyChangedFor(nameof(IsFullyResolved))]
    [NotifyPropertyChangedFor(nameof(ComposedText))]
    private MergeDocument? _document;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isResolving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocument))]
    private bool _isEngineError;

    [ObservableProperty]
    private string _engineErrorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBinaryConflict;

    public bool HasSelectedConflict => SelectedConflict != null;
    public bool HasDocument => Document != null && !IsEngineError;

    public int ConflictCount => Document?.ConflictCount ?? 0;

    public int UnresolvedConflictCount =>
        Document?.Ranges.Count(r => r.IsConflicting && !IsResolved(r)) ?? 0;

    public int ResolvedConflictCount => ConflictCount - UnresolvedConflictCount;

    public bool IsFullyResolved => Document != null && UnresolvedConflictCount == 0;

    public int TotalFiles => Conflicts.Count;
    public int ResolvedFiles => Conflicts.Count(c => c.IsResolved);
    public int RemainingFiles => TotalFiles - ResolvedFiles;
    public bool CanCompleteMerge => TotalFiles > 0 && ResolvedFiles == TotalFiles;
    public bool CanMarkResolved => SelectedConflict != null && (IsFullyResolved || IsEngineError);

    /// <summary>
    /// The text the user is about to commit — produced by applying
    /// <see cref="RangeStates"/> to <see cref="MergeDocument"/>.
    /// Uses the file's original line-ending style.
    /// </summary>
    public string ComposedText => Document?.ComposeResolvedText(RangeStates) ?? string.Empty;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    // ─── Loading ──────────────────────────────────────────────────────────

    public async Task LoadConflictsAsync(bool showLoading = true)
    {
        try
        {
            if (showLoading) IsLoading = true;
            Log.Info("Merge", $"LoadConflicts: repo={System.IO.Path.GetFileName(_repoPath)}");

            var latest = await _gitService.GetConflictsAsync(_repoPath, cancellationToken: SessionToken)
                .ConfigureAwait(true);

            // Preserve the previously-selected file across reloads when possible.
            var previousSelection = SelectedConflict?.FilePath;
            Conflicts.Clear();
            foreach (var c in latest) Conflicts.Add(c);

            var resolved = await _gitService.GetResolvedMergeFilesAsync(_repoPath, SessionToken)
                .ConfigureAwait(true);
            foreach (var r in resolved)
            {
                if (!Conflicts.Any(c => string.Equals(c.FilePath, r.FilePath, StringComparison.Ordinal)))
                {
                    Conflicts.Add(r);
                }
            }

            OnPropertyChanged(nameof(TotalFiles));
            OnPropertyChanged(nameof(ResolvedFiles));
            OnPropertyChanged(nameof(RemainingFiles));
            OnPropertyChanged(nameof(CanCompleteMerge));

            SelectedConflict = previousSelection != null
                ? Conflicts.FirstOrDefault(c => c.FilePath == previousSelection)
                  ?? Conflicts.FirstOrDefault(c => !c.IsResolved)
                : Conflicts.FirstOrDefault(c => !c.IsResolved);
        }
        finally
        {
            if (showLoading) IsLoading = false;
        }
    }

    partial void OnSelectedConflictChanged(ConflictInfo? value)
    {
        _ = BuildDocumentForSelectedAsync();
    }

    private async Task BuildDocumentForSelectedAsync()
    {
        if (SelectedConflict is null)
        {
            Document = null;
            RangeStates.Clear();
            IsEngineError = false;
            return;
        }

        var filePath = SelectedConflict.FilePath;
        var baseContent = SelectedConflict.BaseContent ?? string.Empty;
        var oursContent = SelectedConflict.OursContent ?? string.Empty;
        var theirsContent = SelectedConflict.TheirsContent ?? string.Empty;

        // Clear stale state.
        IsEngineError = false;
        EngineErrorMessage = string.Empty;

        // Binary detection upstream of the engine call.
        if (ContentUtils.IsBinaryContent(oursContent) || ContentUtils.IsBinaryContent(theirsContent))
        {
            IsBinaryConflict = true;
            Document = null;
            RangeStates.Clear();
            _lastBuiltFilePath = filePath;
            return;
        }
        IsBinaryConflict = false;

        // Skip redundant builds for already-loaded files.
        if (_lastBuiltFilePath == filePath && Document != null) return;

        var ct = CancellationTokenSourceExtensions.ReplaceAndCancel(ref _buildCts).Token;

        MergeDocument doc;
        try
        {
            doc = await _engine.MergeAsync(filePath, baseContent, oursContent, theirsContent,
                cancellationToken: ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (MergeEngineException ex)
        {
            Log.Error("Merge", $"Engine error for {filePath}: {ex.Message}", ex);
            Document = null;
            RangeStates.Clear();
            IsEngineError = true;
            EngineErrorMessage = ex.Message;
            _lastBuiltFilePath = filePath;
            return;
        }

        if (ct.IsCancellationRequested || SelectedConflict?.FilePath != filePath) return;

        _lastBuiltFilePath = filePath;
        RangeStates.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
        Document = doc;
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        NotifyResolutionCountsChanged();
    }

    // ─── Resolution commands ──────────────────────────────────────────────

    [RelayCommand]
    private void AcceptOurs(int rangeIndex) => SetState(rangeIndex, ResolutionState.AcceptOurs.Instance);

    [RelayCommand]
    private void AcceptTheirs(int rangeIndex) => SetState(rangeIndex, ResolutionState.AcceptTheirs.Instance);

    [RelayCommand]
    private void AcceptBoth(int rangeIndex) =>
        SetState(rangeIndex, new ResolutionState.AcceptBoth(FirstOurs: true, SmartCombine: true));

    [RelayCommand]
    private void AcceptBothTheirsFirst(int rangeIndex) =>
        SetState(rangeIndex, new ResolutionState.AcceptBoth(FirstOurs: false, SmartCombine: true));

    [RelayCommand]
    private void Unresolve(int rangeIndex) => SetState(rangeIndex, ResolutionState.Unresolved.Instance);

    [RelayCommand]
    private void AcceptAllOurs()
    {
        if (Document is null) return;
        var before = CaptureState();
        foreach (var range in Document.Ranges.Where(r => r.IsConflicting))
            RangeStates[range.Index] = ResolutionState.AcceptOurs.Instance;
        PushUndo(before);
        NotifyResolutionCountsChanged();
    }

    [RelayCommand]
    private void AcceptAllTheirs()
    {
        if (Document is null) return;
        var before = CaptureState();
        foreach (var range in Document.Ranges.Where(r => r.IsConflicting))
            RangeStates[range.Index] = ResolutionState.AcceptTheirs.Instance;
        PushUndo(before);
        NotifyResolutionCountsChanged();
    }

    public void ApplyManualText(int rangeIndex, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        SetState(rangeIndex, new ResolutionState.Manual(text));
    }

    private void SetState(int rangeIndex, ResolutionState newState)
    {
        if (Document is null) return;
        var before = CaptureState();
        if (newState is ResolutionState.Unresolved)
            RangeStates.Remove(rangeIndex);
        else
            RangeStates[rangeIndex] = newState;
        PushUndo(before);
        NotifyResolutionCountsChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_undoStack.Count == 0) return;
        var entry = _undoStack.Pop();
        _redoStack.Push(new ResolutionUndoEntry(CaptureState()));
        RestoreState(entry.Snapshot);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        NotifyResolutionCountsChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (_redoStack.Count == 0) return;
        var entry = _redoStack.Pop();
        _undoStack.Push(new ResolutionUndoEntry(CaptureState()));
        RestoreState(entry.Snapshot);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        NotifyResolutionCountsChanged();
    }

    private Dictionary<int, ResolutionState> CaptureState() => new(RangeStates);

    private void RestoreState(IReadOnlyDictionary<int, ResolutionState> snapshot)
    {
        RangeStates.Clear();
        foreach (var (k, v) in snapshot) RangeStates[k] = v;
    }

    private void PushUndo(Dictionary<int, ResolutionState> snapshot)
    {
        _undoStack.Push(new ResolutionUndoEntry(snapshot));
        _redoStack.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private bool IsResolved(ModifiedBaseRange range)
    {
        if (!range.IsConflicting) return true;
        return RangeStates.TryGetValue(range.Index, out var state)
            && state is not ResolutionState.Unresolved;
    }

    private void NotifyResolutionCountsChanged()
    {
        OnPropertyChanged(nameof(UnresolvedConflictCount));
        OnPropertyChanged(nameof(ResolvedConflictCount));
        OnPropertyChanged(nameof(IsFullyResolved));
        OnPropertyChanged(nameof(CanMarkResolved));
        OnPropertyChanged(nameof(ComposedText));
    }

    // ─── Side-picker escape hatches (available even in engine-error state) ─

    [RelayCommand]
    private async Task UseOursAsync()
    {
        if (SelectedConflict == null) return;
        IsResolving = true;
        try
        {
            await _gitService.ResolveConflictWithOursAsync(_repoPath, SelectedConflict.FilePath,
                SessionToken).ConfigureAwait(true);
            SelectedConflict.IsResolved = true;
            await _gitService.SaveStoredMergeConflictFilesAsync(_repoPath,
                Conflicts.Where(c => !c.IsResolved).Select(c => c.FilePath), SessionToken)
                .ConfigureAwait(true);
            NotifyFileCountsChanged();
            AutoAdvance();
        }
        finally { IsResolving = false; }
    }

    [RelayCommand]
    private async Task UseTheirsAsync()
    {
        if (SelectedConflict == null) return;
        IsResolving = true;
        try
        {
            await _gitService.ResolveConflictWithTheirsAsync(_repoPath, SelectedConflict.FilePath,
                SessionToken).ConfigureAwait(true);
            SelectedConflict.IsResolved = true;
            await _gitService.SaveStoredMergeConflictFilesAsync(_repoPath,
                Conflicts.Where(c => !c.IsResolved).Select(c => c.FilePath), SessionToken)
                .ConfigureAwait(true);
            NotifyFileCountsChanged();
            AutoAdvance();
        }
        finally { IsResolving = false; }
    }

    // ─── Mark resolved / complete / abort ─────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanMarkResolved))]
    private async Task MarkResolvedAsync()
    {
        if (SelectedConflict == null || Document == null) return;
        IsResolving = true;
        try
        {
            var filePath = SelectedConflict.FilePath;
            var composed = ComposedText;
            var fullPath = System.IO.Path.Combine(_repoPath, filePath);
            await System.IO.File.WriteAllTextAsync(fullPath, composed, SessionToken)
                .ConfigureAwait(true);
            await _gitService.MarkConflictResolvedAsync(_repoPath, filePath, SessionToken)
                .ConfigureAwait(true);
            SelectedConflict.IsResolved = true;
            SelectedConflict.MergedContent = composed;
            await _gitService.SaveStoredMergeConflictFilesAsync(_repoPath,
                Conflicts.Where(c => !c.IsResolved).Select(c => c.FilePath), SessionToken)
                .ConfigureAwait(true);
            NotifyFileCountsChanged();
            AutoAdvance();
        }
        finally { IsResolving = false; }
    }

    [RelayCommand(CanExecute = nameof(CanCompleteMerge))]
    private async Task CompleteMergeAsync()
    {
        IsResolving = true;
        try
        {
            // Use git's default merge-commit message — git auto-generates one from the
            // merging branches. A future enhancement could surface a custom-message
            // dialog here.
            await _gitService.CompleteMergeAsync(_repoPath, commitMessage: string.Empty, SessionToken)
                .ConfigureAwait(true);
            MergeCompleted?.Invoke(this, true);
        }
        finally { IsResolving = false; }
    }

    [RelayCommand]
    private async Task AbortMergeAsync()
    {
        IsResolving = true;
        try
        {
            await _gitService.AbortMergeAsync(_repoPath, SessionToken).ConfigureAwait(true);
            MergeCompleted?.Invoke(this, false);
        }
        finally { IsResolving = false; }
    }

    [RelayCommand]
    private void CopyComposedText()
    {
        if (Document == null) return;
        _clipboardService.SetText(ComposedText);
    }

    private void AutoAdvance()
    {
        var next = Conflicts.FirstOrDefault(c => !c.IsResolved);
        if (next != null) SelectedConflict = next;
    }

    private void NotifyFileCountsChanged()
    {
        OnPropertyChanged(nameof(ResolvedFiles));
        OnPropertyChanged(nameof(RemainingFiles));
        OnPropertyChanged(nameof(CanCompleteMerge));
    }

    public void Dispose()
    {
        _buildCts?.Cancel();
        _buildCts?.Dispose();
        _buildCts = null;
    }

    private readonly record struct ResolutionUndoEntry(Dictionary<int, ResolutionState> Snapshot);
}
