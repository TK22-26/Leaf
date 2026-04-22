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
    private readonly IAiMergeAssistant? _aiAssistant;
    private readonly IImageMergeService? _imageService;
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
        : this(gitService, clipboardService, engine, new WordDiffService(), aiAssistant: null,
               imageService: null, repoPath)
    {
    }

    /// <summary>
    /// DI-friendly primary constructor. The convenience overload above creates
    /// a default <see cref="WordDiffService"/>; production code goes through
    /// <c>IServiceProvider</c> which resolves the singleton registered in
    /// <c>ServiceRegistry</c>. Tests can inject a fake here.
    /// <paramref name="aiAssistant"/> is nullable: production code always
    /// passes the DI-registered <see cref="McpMergeAssistant"/> (which itself
    /// returns <c>null</c> when disabled/consent-missing), but tests that
    /// don't exercise the AI path can pass <c>null</c> to opt out entirely.
    /// <paramref name="imageService"/> is nullable for the same reason:
    /// production injects the singleton, unit tests that don't exercise
    /// binary/image conflicts pass <c>null</c>.
    /// </summary>
    public MergeEditorViewModel(
        IGitService gitService,
        IClipboardService clipboardService,
        IMergeEngine engine,
        IWordDiffService wordDiffService,
        IAiMergeAssistant? aiAssistant,
        IImageMergeService? imageService,
        string repoPath)
    {
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _wordDiffService = wordDiffService ?? throw new ArgumentNullException(nameof(wordDiffService));
        _aiAssistant = aiAssistant;
        _imageService = imageService;
        _repoPath = repoPath ?? throw new ArgumentNullException(nameof(repoPath));
    }

    public event EventHandler<bool>? MergeCompleted;

    /// <summary>
    /// Fires whenever <see cref="RangeStates"/> has been mutated (via an
    /// accept / unresolve / undo / redo / AcceptAll operation). RangeStates is
    /// a plain <see cref="Dictionary{TKey,TValue}"/> so WPF doesn't notice
    /// mutations via the normal <see cref="System.ComponentModel.INotifyPropertyChanged"/>
    /// pipeline; panes subscribe here and call <c>InvalidateVisual</c>.
    /// </summary>
    public event EventHandler? RangeStatesChanged;

    /// <summary>Set by the host VM; returns a token cancelled when the repo session ends.</summary>
    public Func<CancellationToken>? GetSessionToken { get; set; }
    private CancellationToken SessionToken => GetSessionToken?.Invoke() ?? CancellationToken.None;

    [ObservableProperty]
    private string _sourceBranch = string.Empty;

    [ObservableProperty]
    private string _targetBranch = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ConflictInfo> _conflicts = new();

    /// <summary>
    /// Unresolved files — bucket consumed by the MergeStatusView sidebar's
    /// "Conflicted Files" list. Kept in sync with <see cref="Conflicts"/> by
    /// <see cref="RefreshConflictBuckets"/>.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ConflictInfo> _conflictedConflicts = new();

    /// <summary>
    /// Resolved files — bucket consumed by the MergeStatusView sidebar's
    /// "Resolved Files" list. Kept in sync with <see cref="Conflicts"/> by
    /// <see cref="RefreshConflictBuckets"/>.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ConflictInfo> _resolvedConflicts = new();

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
    [NotifyPropertyChangedFor(nameof(OursLines))]
    [NotifyPropertyChangedFor(nameof(TheirsLines))]
    [NotifyPropertyChangedFor(nameof(CanRequestAiResolution))]
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

    /// <summary>
    /// Loaded ours/theirs/base bytes for the currently-selected conflict when it
    /// is an image (binary with recognised magic bytes). Null otherwise.
    /// Drives the Phase 6 image conflict pane.
    /// </summary>
    [ObservableProperty]
    private ImageConflictPayload? _imagePayload;

    /// <summary>
    /// Shared zoom/pan/mode state for the image conflict pane. Kept on the VM
    /// so the state survives across mode toggles, and so per-conflict panes
    /// can each have an independent viewport.
    /// </summary>
    [ObservableProperty]
    private Leaf.Controls.Merge.ImageViewportState _imageViewport = new();

    public bool HasSelectedConflict => SelectedConflict != null;
    public bool HasDocument => Document != null && !IsEngineError;

    public int ConflictCount => Document?.ConflictCount ?? 0;

    public int UnresolvedConflictCount =>
        Document?.Ranges.Count(r => r.IsConflicting && !IsResolved(r)) ?? 0;

    public int ResolvedConflictCount => ConflictCount - UnresolvedConflictCount;

    /// <summary>
    /// Number of conflicts with an AI resolution proposal in flight or awaiting
    /// user action. Today this is a 0-or-1 signal tracking the single
    /// <see cref="IsAiRequestInFlight"/> guard; the header pill binds to it
    /// already so when C6 adds persistent per-range AI state the pill count
    /// picks that up without any view changes.
    /// </summary>
    public int AiPendingConflictCount => IsAiRequestInFlight ? 1 : 0;

    public bool IsFullyResolved => Document != null && UnresolvedConflictCount == 0;

    public int TotalFiles => Conflicts.Count;
    public int ResolvedFiles => Conflicts.Count(c => c.IsResolved);
    public int RemainingFiles => TotalFiles - ResolvedFiles;
    public bool CanCompleteMerge => TotalFiles > 0 && ResolvedFiles == TotalFiles;

    // Legacy-compatible aliases used by MainWindow.xaml bindings that predate Phase 2c.
    public int TotalCount => TotalFiles;
    public int ResolvedCount => ResolvedFiles;
    public int RemainingCount => RemainingFiles;

    /// <summary>UI toggle; pass-through from settings. Used by MainWindow binding.</summary>
    [ObservableProperty]
    private bool _isCompactFileList;
    public bool CanMarkResolved => SelectedConflict != null && (IsFullyResolved || IsEngineError);

    /// <summary>
    /// The text the user is about to commit — produced by applying
    /// <see cref="RangeStates"/> to <see cref="MergeDocument"/>.
    /// Uses the file's original line-ending style.
    /// </summary>
    public string ComposedText => Document?.ComposeResolvedText(RangeStates) ?? string.Empty;

    /// <summary>Ours-side lines for the current document (pass-through for pane binding).</summary>
    public IReadOnlyList<string> OursLines => Document?.OursLines ?? Array.Empty<string>();

    /// <summary>Theirs-side lines for the current document (pass-through for pane binding).</summary>
    public IReadOnlyList<string> TheirsLines => Document?.TheirsLines ?? Array.Empty<string>();

    /// <summary>Shared font/metrics layout for all panes. Owned by the VM so the UI binds once.</summary>
    public Leaf.TextEdit.MergePaneGlyphLayout Layout { get; } = new Leaf.TextEdit.MergePaneGlyphLayout();

    /// <summary>
    /// Ctrl+K command palette backing. The view hosts a shared
    /// <see cref="Leaf.Views.CommandPaletteView"/> with this as DataContext;
    /// <see cref="OpenPaletteCommand"/> populates it from
    /// <see cref="MergeCommandCatalog"/>.
    /// </summary>
    public MergeCommandPaletteViewModel Palette { get; } = new();

    /// <summary>
    /// Ctrl+K handler. Toggles the palette — if it's already open, pressing
    /// Ctrl+K again dismisses it (matches VS Code / Sublime Merge) instead of
    /// re-rebuilding the catalog and stealing focus again.
    /// </summary>
    [RelayCommand]
    private void OpenPalette()
    {
        if (Palette.IsOpen)
        {
            Palette.Close();
            return;
        }
        Palette.Open(MergeCommandCatalog.BuildFor(this));
    }

    /// <summary>
    /// Per-conflict-range word-level diff on the ours side (see
    /// <see cref="Leaf.Controls.Merge.ReadOnlyMergePane.WordDiffs"/>).
    /// Populated by <see cref="BuildWordDiffs"/> whenever <see cref="Document"/> changes.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyDictionary<int, IReadOnlyList<TokenLine>> _oursWordDiffs =
        new Dictionary<int, IReadOnlyList<TokenLine>>();

    /// <summary>Per-conflict-range word-level diff on the theirs side.</summary>
    [ObservableProperty]
    private IReadOnlyDictionary<int, IReadOnlyList<TokenLine>> _theirsWordDiffs =
        new Dictionary<int, IReadOnlyList<TokenLine>>();

    private readonly IWordDiffService _wordDiffService;

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
            OnPropertyChanged(nameof(TotalCount));
            NotifyFileCountsChanged();

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
        // Route through FireAndForget so any unexpected exception surfaces
        // through Leaf's AsyncErrorHandler (log + notification) instead of
        // silently faulting the Task and freezing the panes on stale content.
        BuildDocumentForSelectedAsync().FireAndForget(
            nameof(BuildDocumentForSelectedAsync), isUserAction: true);
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
            // Phase 6: hydrate the ImagePayload and reset the viewport for the
            // new file. Clear them first so a load failure below doesn't leave
            // stale state from a prior file on screen. The image-bytes load is
            // cheap (git show + magic-byte sniff, no decode) so no Task.Run
            // needed — decoding happens inside the pane's Payload setter.
            ImagePayload = null;
            ImageViewport = new Leaf.Controls.Merge.ImageViewportState();
            if (_imageService is not null)
            {
                try
                {
                    ImagePayload = _imageService.Load(_repoPath, filePath);
                }
                catch (Exception ex)
                {
                    Log.Warn("Merge", $"Image payload load failed for {filePath}: {ex.Message}");
                }
            }
            return;
        }
        IsBinaryConflict = false;
        ImagePayload = null;

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
        // Point the keyboard-nav cursor at the first conflicting range so F8
        // navigates from there rather than from a stale index.
        CurrentConflictIndex = 0;
        // Build word-level diffs off the UI thread: a large conflict block (e.g.
        // a regenerated package-lock.json with 1000+ lines) would otherwise
        // freeze the UI for ~1s per file-select. Check cancellation before
        // assigning results so rapid file-switching doesn't flicker between
        // stale + fresh dicts.
        _ = ComputeWordDiffsAsync(doc, ct);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        NotifyResolutionCountsChanged();
    }

    private async Task ComputeWordDiffsAsync(MergeDocument doc, CancellationToken ct)
    {
        try
        {
            var (ours, theirs) = await Task.Run(() => BuildWordDiffs(doc, ct), ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested || Document != doc) return;
            OursWordDiffs = ours;
            TheirsWordDiffs = theirs;
        }
        catch (OperationCanceledException)
        {
            // Selection changed mid-compute; the newer call will replace the dicts.
        }
    }

    /// <summary>
    /// Compute per-conflict-range word-level diffs. For each conflicting range,
    /// pair ours-line-i with theirs-line-i and diff at the token level; the
    /// extra lines on whichever side is longer are emitted as pure adds.
    /// </summary>
    private (Dictionary<int, IReadOnlyList<TokenLine>> Ours,
             Dictionary<int, IReadOnlyList<TokenLine>> Theirs)
        BuildWordDiffs(MergeDocument doc, CancellationToken ct)
    {
        var ours = new Dictionary<int, IReadOnlyList<TokenLine>>();
        var theirs = new Dictionary<int, IReadOnlyList<TokenLine>>();
        foreach (var range in doc.Ranges)
        {
            ct.ThrowIfCancellationRequested();
            if (!range.IsConflicting) continue;
            var oursLines = new List<TokenLine>(range.OursLines.Count);
            var theirsLines = new List<TokenLine>(range.TheirsLines.Count);
            int paired = Math.Min(range.OursLines.Count, range.TheirsLines.Count);
            for (int i = 0; i < paired; i++)
            {
                var (l, r) = _wordDiffService.DiffLines(range.OursLines[i], range.TheirsLines[i]);
                oursLines.Add(new TokenLine(range.OursLines[i], l));
                theirsLines.Add(new TokenLine(range.TheirsLines[i], r));
            }
            for (int i = paired; i < range.OursLines.Count; i++)
            {
                var line = range.OursLines[i];
                oursLines.Add(new TokenLine(line, line.Length == 0
                    ? Array.Empty<TokenSegment>()
                    : new[] { new TokenSegment(1, line.Length + 1, TokenKind.Removed, line) }));
            }
            for (int i = paired; i < range.TheirsLines.Count; i++)
            {
                var line = range.TheirsLines[i];
                theirsLines.Add(new TokenLine(line, line.Length == 0
                    ? Array.Empty<TokenSegment>()
                    : new[] { new TokenSegment(1, line.Length + 1, TokenKind.Added, line) }));
            }
            ours[range.Index] = oursLines;
            theirs[range.Index] = theirsLines;
        }
        return (ours, theirs);
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

    /// <summary>
    /// Raised when the user clicks the CodeLens "Compare" link on a conflict
    /// range. The view subscribes and smooth-scrolls both the Ours and Theirs
    /// panes to the range's respective start line so the user can see both
    /// versions side-by-side before choosing.
    /// </summary>
    public event EventHandler<int>? CompareRequested;

    [RelayCommand]
    private void CompareConflict(int rangeIndex)
    {
        if (Document is null) return;
        if (Document.Ranges.All(r => r.Index != rangeIndex)) return;
        CompareRequested?.Invoke(this, rangeIndex);
    }

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
        RaiseUndoRedoChanged();
        NotifyResolutionCountsChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (_redoStack.Count == 0) return;
        var entry = _redoStack.Pop();
        _undoStack.Push(new ResolutionUndoEntry(CaptureState()));
        RestoreState(entry.Snapshot);
        RaiseUndoRedoChanged();
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
        RaiseUndoRedoChanged();
    }

    /// <summary>
    /// Fire property-changed for CanUndo/CanRedo AND refresh the command
    /// CanExecute state. [RelayCommand]-generated commands don't auto-wire
    /// their CanExecute to the property notifications, so buttons bound to
    /// the commands stay at their initial IsEnabled state without this call.
    /// </summary>
    private void RaiseUndoRedoChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
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
        // [RelayCommand] does not auto-re-evaluate CanExecute on property
        // changes — it needs an explicit NotifyCanExecuteChanged to refresh
        // the button's IsEnabled binding. Without this, Mark Resolved stays
        // disabled after the user's clicks flip IsFullyResolved to true.
        MarkResolvedCommand.NotifyCanExecuteChanged();
        RangeStatesChanged?.Invoke(this, EventArgs.Empty);
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

            // Final gate: never write content containing an unresolved zdiff3 triad
            // to disk. Phase 2c's Result pane is read-only so this should be unreachable
            // via the current UI — but a future re-enabled manual-edit path would
            // silently corrupt without this check, so it's defence-in-depth per the
            // Engineering-Software "fail loudly" policy.
            if (ContainsConflictMarkers(composed))
            {
                Log.Warn("Merge", $"MarkResolved refused: composed text still contains zdiff3 markers for {filePath}");
                EngineErrorMessage = "Cannot mark resolved — the merged result still contains " +
                    "unresolved conflict markers. Resolve every conflict region or use " +
                    "'Use Ours' / 'Use Theirs' to force a side.";
                IsEngineError = true;
                return;
            }

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

    /// <summary>
    /// Commit-gate helper: <c>true</c> iff <paramref name="content"/> contains the full
    /// zdiff3 structural triad (opener → separator → closer) in order. Tolerates CRLF
    /// line endings (AvalonEdit preserves them on Windows). A lone opener without the
    /// separator and closer is treated as user content (e.g. documentation that mentions
    /// conflict markers), not unresolved state. Ported from the pre-Phase-2c
    /// <c>ConflictResolutionViewModel.ContainsConflictMarkers</c>.
    /// </summary>
    internal static bool ContainsConflictMarkers(string content)
    {
        bool sawOpen = false;
        bool sawSeparator = false;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Length > 0 && rawLine[rawLine.Length - 1] == '\r'
                ? rawLine.Substring(0, rawLine.Length - 1)
                : rawLine;

            if (!sawOpen)
            {
                if (line.StartsWith("<<<<<<<", StringComparison.Ordinal)) sawOpen = true;
                continue;
            }

            if (!sawSeparator)
            {
                if (line == "=======") sawSeparator = true;
                continue;
            }

            if (line.StartsWith(">>>>>>>", StringComparison.Ordinal)) return true;
        }
        return false;
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

    // ── Keyboard-driven conflict navigation + current-range commands ──────

    /// <summary>
    /// 0-based index of the conflict range the user is currently "on"
    /// (for keyboard shortcuts). Advanced by <see cref="NextConflictCommand"/>
    /// and <see cref="PreviousConflictCommand"/>. Starts at the first
    /// unresolved range when a document loads.
    /// </summary>
    [ObservableProperty]
    private int _currentConflictIndex;

    [RelayCommand]
    private void NextConflict()
    {
        if (Document is null) return;
        var conflicting = Document.Ranges.Where(r => r.IsConflicting).ToList();
        if (conflicting.Count == 0) return;
        // Prefer jumping to the next UNRESOLVED range so the keyboard flow matches
        // the user's natural "resolve everything" rhythm. Fall back to a simple
        // wrap-around if all ranges are already resolved.
        CurrentConflictIndex = FindNextUnresolvedOrWrap(conflicting, +1);
    }

    [RelayCommand]
    private void PreviousConflict()
    {
        if (Document is null) return;
        var conflicting = Document.Ranges.Where(r => r.IsConflicting).ToList();
        if (conflicting.Count == 0) return;
        CurrentConflictIndex = FindNextUnresolvedOrWrap(conflicting, -1);
    }

    /// <summary>
    /// C2 secondary cursor. 0-based index into the current conflict's
    /// <see cref="ModifiedBaseRange.OursDiffs"/> + <see cref="ModifiedBaseRange.TheirsDiffs"/>
    /// (concatenated, Ours first) — identifies which change span inside the
    /// current range the user is focused on. The Alt+Left / Alt+Right
    /// commands advance and retreat the cursor; wrapping past an edge
    /// advances <see cref="CurrentConflictIndex"/> to the next conflict's
    /// first or last span.
    /// </summary>
    [ObservableProperty]
    private int _currentChangeSpanIndex;

    [RelayCommand]
    private void NextChangeSpan()
    {
        if (Document is null) return;
        var conflicting = Document.Ranges.Where(r => r.IsConflicting).ToList();
        if (conflicting.Count == 0) return;
        var range = conflicting[Math.Clamp(CurrentConflictIndex, 0, conflicting.Count - 1)];
        var spanCount = range.OursDiffs.Count + range.TheirsDiffs.Count;
        if (spanCount == 0 || CurrentChangeSpanIndex >= spanCount - 1)
        {
            // Past the last span of this conflict — fall into the next conflict's
            // first span. Use NextConflictCommand's wrap semantics for consistency.
            CurrentConflictIndex = (CurrentConflictIndex + 1) % conflicting.Count;
            CurrentChangeSpanIndex = 0;
        }
        else
        {
            CurrentChangeSpanIndex++;
        }
    }

    [RelayCommand]
    private void PreviousChangeSpan()
    {
        if (Document is null) return;
        var conflicting = Document.Ranges.Where(r => r.IsConflicting).ToList();
        if (conflicting.Count == 0) return;
        if (CurrentChangeSpanIndex <= 0)
        {
            // At or before the first span — retreat to the previous conflict
            // and land on its last span.
            var prevIdx = (CurrentConflictIndex - 1 + conflicting.Count) % conflicting.Count;
            CurrentConflictIndex = prevIdx;
            var prev = conflicting[prevIdx];
            var prevSpanCount = prev.OursDiffs.Count + prev.TheirsDiffs.Count;
            CurrentChangeSpanIndex = Math.Max(0, prevSpanCount - 1);
        }
        else
        {
            CurrentChangeSpanIndex--;
        }
    }

    /// <summary>
    /// 0-based cursor into <see cref="ModifiedBaseRange"/>s that are NOT
    /// conflicting — Git auto-resolved them, but the UI still surfaces them
    /// for context. Used by <see cref="NextAutoMergedRegionCommand"/> and
    /// <see cref="PreviousAutoMergedRegionCommand"/>.
    /// </summary>
    [ObservableProperty]
    private int _currentAutoMergedRegionIndex;

    [RelayCommand]
    private void NextAutoMergedRegion()
    {
        if (Document is null) return;
        var auto = Document.Ranges.Where(r => !r.IsConflicting).ToList();
        if (auto.Count == 0) return;
        CurrentAutoMergedRegionIndex = (CurrentAutoMergedRegionIndex + 1) % auto.Count;
    }

    [RelayCommand]
    private void PreviousAutoMergedRegion()
    {
        if (Document is null) return;
        var auto = Document.Ranges.Where(r => !r.IsConflicting).ToList();
        if (auto.Count == 0) return;
        CurrentAutoMergedRegionIndex = (CurrentAutoMergedRegionIndex - 1 + auto.Count) % auto.Count;
    }

    private int FindNextUnresolvedOrWrap(List<ModifiedBaseRange> conflicting, int delta)
    {
        var n = conflicting.Count;
        for (int step = 1; step <= n; step++)
        {
            var idx = ((CurrentConflictIndex + delta * step) % n + n) % n;
            if (!IsResolved(conflicting[idx])) return idx;
        }
        // All resolved — wrap normally so the user can still re-review.
        return ((CurrentConflictIndex + delta) % n + n) % n;
    }

    [RelayCommand]
    private void AcceptCurrentConflictOurs()
    {
        var range = CurrentConflictRange();
        if (range is null) return;
        SetState(range.Index, ResolutionState.AcceptOurs.Instance);
    }

    [RelayCommand]
    private void AcceptCurrentConflictTheirs()
    {
        var range = CurrentConflictRange();
        if (range is null) return;
        SetState(range.Index, ResolutionState.AcceptTheirs.Instance);
    }

    [RelayCommand]
    private void AcceptCurrentConflictBoth()
    {
        var range = CurrentConflictRange();
        if (range is null) return;
        SetState(range.Index, new ResolutionState.AcceptBoth(FirstOurs: true, SmartCombine: true));
    }

    private ModifiedBaseRange? CurrentConflictRange()
    {
        if (Document is null) return null;
        var conflicting = Document.Ranges.Where(r => r.IsConflicting).ToList();
        if (conflicting.Count == 0) return null;
        var idx = Math.Clamp(CurrentConflictIndex, 0, conflicting.Count - 1);
        return conflicting[idx];
    }

    [RelayCommand]
    private async Task UnresolveConflictAsync(ConflictInfo? conflict)
    {
        if (conflict is null) return;
        IsResolving = true;
        try
        {
            await _gitService.ReopenConflictAsync(_repoPath, conflict.FilePath,
                conflict.BaseContent ?? string.Empty,
                conflict.OursContent ?? string.Empty,
                conflict.TheirsContent ?? string.Empty,
                SessionToken).ConfigureAwait(true);
            conflict.IsResolved = false;
            conflict.MergedContent = string.Empty;
            NotifyFileCountsChanged();
        }
        finally { IsResolving = false; }
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
        OnPropertyChanged(nameof(ResolvedCount));
        OnPropertyChanged(nameof(RemainingCount));
        // See the comment in NotifyResolutionCountsChanged — [RelayCommand]
        // CanExecute needs explicit NotifyCanExecuteChanged to refresh button
        // enablement after state mutations.
        CompleteMergeCommand.NotifyCanExecuteChanged();
        MarkResolvedCommand.NotifyCanExecuteChanged();
        RefreshConflictBuckets();
    }

    /// <summary>
    /// Partition <see cref="Conflicts"/> into the two sidebar lists. Called
    /// whenever the set of conflicts or their resolved state changes.
    /// </summary>
    private void RefreshConflictBuckets()
    {
        ConflictedConflicts.Clear();
        ResolvedConflicts.Clear();
        foreach (var c in Conflicts)
        {
            if (c.IsResolved) ResolvedConflicts.Add(c);
            else ConflictedConflicts.Add(c);
        }
    }

    public void Dispose()
    {
        _buildCts?.Cancel();
        _buildCts?.Dispose();
        _buildCts = null;
    }

    /// <summary>
    /// Legacy-compat alias for <see cref="Dispose"/> called from MainViewModel's
    /// per-repo teardown. Pre-Phase-2c <c>ConflictResolutionViewModel</c> exposed
    /// <c>Cleanup()</c>; keeping the name means MainViewModel doesn't need a
    /// parallel change.
    /// </summary>
    public void Cleanup() => Dispose();

    private readonly record struct ResolutionUndoEntry(Dictionary<int, ResolutionState> Snapshot);
}
