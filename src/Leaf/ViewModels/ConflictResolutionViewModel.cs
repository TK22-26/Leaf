using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Collections;
using Leaf.Controls.Merge;
using Leaf.Models;
using Leaf.Models.Merge;
using Leaf.Services;
using Leaf.Services.Merge;
using Leaf.Utils;

namespace Leaf.ViewModels;

/// <summary>
/// ViewModel for the merge conflict resolution view.
/// Supports per-hunk and per-line conflict resolution with auto-merge,
/// undo/redo, accept-both, collapse-resolved, and auto-advance.
/// </summary>
public partial class ConflictResolutionViewModel : ObservableObject
{
    private readonly IGitService _gitService;
    private readonly IClipboardService _clipboardService;
    private readonly IThreeWayMergeService _mergeService;
    private readonly IMergeUiLogger _logger;
    private readonly string _repoPath;
    private int _currentRegionIndex = -1;
    private readonly ResolutionUndoStack _undoStack = new();

    public event EventHandler<int>? RequestScrollToRegion;

    [ObservableProperty]
    private string _sourceBranch = string.Empty;

    [ObservableProperty]
    private string _targetBranch = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ConflictInfo> _conflicts = [];

    [ObservableProperty]
    private ObservableCollection<ConflictInfo> _conflictedConflicts = [];

    [ObservableProperty]
    private ObservableCollection<ConflictInfo> _resolvedConflicts = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedConflict))]
    [NotifyPropertyChangedFor(nameof(HasUnresolvedConflicts))]
    [NotifyPropertyChangedFor(nameof(CanMarkResolved))]
    private ConflictInfo? _selectedConflict;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnresolvedConflicts))]
    [NotifyPropertyChangedFor(nameof(CanMarkResolved))]
    [NotifyPropertyChangedFor(nameof(ConflictRegions))]
    [NotifyPropertyChangedFor(nameof(CurrentFileRegionCount))]
    [NotifyPropertyChangedFor(nameof(CurrentFileResolvedRegionCount))]
    private FileMergeResult? _currentMergeResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMarkResolved))]
    private string _mergedContent = string.Empty;

    [ObservableProperty]
    private BulkObservableCollection<MergedLine> _mergedLines = [];

    [ObservableProperty]
    private ConflictSideLineMapping? _oursLineMapping;

    [ObservableProperty]
    private string _oursFileContent = string.Empty;

    [ObservableProperty]
    private ConflictSideLineMapping? _theirsLineMapping;

    [ObservableProperty]
    private string _theirsFileContent = string.Empty;

    private DispatcherTimer? _mergedContentDebounceTimer;

    private readonly HashSet<SelectableLine> _wiredSelectableLines = [];
    private readonly HashSet<MergedLine> _wiredMergedLines = [];
    private CancellationTokenSource? _buildMergeCts;
    private string? _lastBuiltFilePath;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isResolving;

    [ObservableProperty]
    private bool _isCompactFileList;

    [ObservableProperty]
    private bool _isSyncScrollEnabled = true;

    [ObservableProperty]
    private bool _isBinaryConflict;

    [ObservableProperty]
    private bool _isLargeFile;

    [ObservableProperty]
    private int _largeFileLineCount;

    [ObservableProperty]
    private bool _continueLargeFile;

    /// <summary>
    /// Set when <c>git merge-file</c> or the parser refuses to process a file
    /// (e.g. malformed zdiff3 output). UI should surface this via an overlay
    /// so the user is not left staring at a blank panel.
    /// </summary>
    [ObservableProperty]
    private bool _isEngineError;

    /// <summary>
    /// Human-readable description of the most recent engine error.
    /// </summary>
    [ObservableProperty]
    private string _engineErrorMessage = string.Empty;

    [ObservableProperty]
    private string _conflictNavigationLabel = string.Empty;

    /// <summary>
    /// Number of resolved files.
    /// </summary>
    public int ResolvedCount => Conflicts.Count(c => c.IsResolved);

    /// <summary>
    /// Total number of conflicting files.
    /// </summary>
    public int TotalCount => Conflicts.Count;

    /// <summary>
    /// Number of remaining (unresolved) files.
    /// </summary>
    public int RemainingCount => TotalCount - ResolvedCount;

    /// <summary>
    /// True if all files have been resolved.
    /// </summary>
    public bool CanCompleteMerge => Conflicts.Count > 0 && Conflicts.All(c => c.IsResolved);

    public bool HasSelectedConflict => SelectedConflict != null;

    public bool HasUnresolvedConflicts => CurrentMergeResult?.UnresolvedCount > 0;

    /// <summary>
    /// Only the conflict regions for UI display.
    /// </summary>
    public IEnumerable<MergeRegion> ConflictRegions => CurrentMergeResult?.Regions.Where(r => r.IsConflict) ?? [];

    /// <summary>
    /// Total conflict regions in current file.
    /// </summary>
    public int CurrentFileRegionCount => CurrentMergeResult?.ConflictCount ?? 0;

    /// <summary>
    /// Resolved conflict regions in current file.
    /// </summary>
    public int CurrentFileResolvedRegionCount => CurrentMergeResult?.ResolvedCount ?? 0;

    public bool CanMarkResolved => (CurrentMergeResult?.IsFullyResolved == true) || IsMergedContentResolved;

    private bool IsMergedContentResolved => !string.IsNullOrWhiteSpace(MergedContent) && !ContainsConflictMarkers(MergedContent);

    public bool CanUndo => _undoStack.CanUndo;
    public bool CanRedo => _undoStack.CanRedo;

    /// <summary>
    /// File-level progress as a percentage (0-100).
    /// </summary>
    public double FileProgressPercent => TotalCount > 0 ? (double)ResolvedCount / TotalCount * 100 : 0;

    /// <summary>
    /// Region-level progress for current file as percentage.
    /// </summary>
    public double RegionProgressPercent => CurrentFileRegionCount > 0 ? (double)CurrentFileResolvedRegionCount / CurrentFileRegionCount * 100 : 0;

    public event EventHandler<bool>? MergeCompleted;

    /// <summary>
    /// Returns the current repository's cancellation token. Set by
    /// MainViewModel so this VM's background git calls abort when the
    /// session is disposed on repo switch.
    /// </summary>
    public Func<CancellationToken>? GetSessionToken { get; set; }

    private CancellationToken SessionToken => GetSessionToken?.Invoke() ?? CancellationToken.None;

    public ConflictResolutionViewModel(
        IGitService gitService,
        IClipboardService clipboardService,
        IDispatcherService dispatcherService,
        string repoPath)
        : this(gitService, clipboardService, new ThreeWayMergeService(), dispatcherService, repoPath)
    {
    }

    public ConflictResolutionViewModel(
        IGitService gitService,
        IClipboardService clipboardService,
        IThreeWayMergeService mergeService,
        IDispatcherService dispatcherService,
        string repoPath)
    {
        _gitService = gitService;
        _clipboardService = clipboardService;
        _mergeService = mergeService;
        _repoPath = repoPath;
        _logger = new MergeUiLogger();

        _undoStack.StackChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
        };
    }

    public async Task LoadConflictsAsync(bool showLoading = true)
    {
        try
        {
            if (showLoading)
                IsLoading = true;

            Log.Info("Merge", $"LoadConflicts: repo={System.IO.Path.GetFileName(_repoPath)}");
            var latestConflicts = await _gitService.GetConflictsAsync(_repoPath, cancellationToken: SessionToken);
            foreach (var conflict in latestConflicts)
                conflict.IsResolved = false;

            var resolvedFiles = await _gitService.GetResolvedMergeFilesAsync(_repoPath, cancellationToken: SessionToken);
            var latestByPath = new Dictionary<string, ConflictInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var conflict in latestConflicts)
                latestByPath[conflict.FilePath] = conflict;

            foreach (var resolved in resolvedFiles)
            {
                if (!latestByPath.ContainsKey(resolved.FilePath))
                    latestByPath[resolved.FilePath] = resolved;
            }

            var existingByPath = Conflicts.ToDictionary(c => c.FilePath, StringComparer.OrdinalIgnoreCase);

            foreach (var latest in latestByPath.Values)
            {
                if (existingByPath.TryGetValue(latest.FilePath, out var existing))
                {
                    existing.FilePath = latest.FilePath;
                    existing.BaseContent = latest.BaseContent;
                    existing.OursContent = latest.OursContent;
                    existing.TheirsContent = latest.TheirsContent;
                    existing.IsResolved = latest.IsResolved;
                }
                else
                {
                    Conflicts.Add(latest);
                }
            }

            foreach (var existing in Conflicts)
            {
                if (!latestByPath.ContainsKey(existing.FilePath))
                    existing.IsResolved = true;
            }

            if (SelectedConflict == null || !Conflicts.Contains(SelectedConflict))
                SelectedConflict = Conflicts.FirstOrDefault(c => !c.IsResolved) ?? Conflicts.FirstOrDefault();

            UpdateCounts();
            await _gitService.SaveStoredMergeConflictFilesAsync(_repoPath, latestByPath.Keys, cancellationToken: SessionToken);

            OnPropertyChanged(nameof(ConflictedConflicts));
            OnPropertyChanged(nameof(ResolvedConflicts));
        }
        finally
        {
            if (showLoading)
                IsLoading = false;
        }
    }

    private async Task BuildMergeResultForSelectedConflict()
    {
        if (SelectedConflict == null)
        {
            CurrentMergeResult = null;
            MergedContent = string.Empty;
            MergedLines.Clear();
            _lastBuiltFilePath = null;
            IsBinaryConflict = false;
            IsLargeFile = false;
            return;
        }

        var filePath = SelectedConflict.FilePath;
        var baseContent = SelectedConflict.BaseContent;
        var oursContent = SelectedConflict.OursContent;
        var theirsContent = SelectedConflict.TheirsContent;

        // Clear any stale engine-error state from a previous file before we start.
        IsEngineError = false;
        EngineErrorMessage = string.Empty;

        // Skip if already built
        if (_lastBuiltFilePath == filePath && CurrentMergeResult != null)
        {
            Log.Info("Merge", $"BuildMergeResult: skipping redundant build for {filePath}");
            return;
        }

        // Binary detection
        if (Utils.ContentUtils.IsBinaryContent(oursContent ?? "") || Utils.ContentUtils.IsBinaryContent(theirsContent ?? ""))
        {
            IsBinaryConflict = true;
            IsLargeFile = false;
            _logger.BinaryFile(filePath);
            _lastBuiltFilePath = filePath;
            CurrentMergeResult = null;
            MergedContent = string.Empty;
            MergedLines.Clear();
            return;
        }

        IsBinaryConflict = false;

        // Large file detection
        var totalLines = Utils.ContentUtils.CountLines(oursContent ?? "") + Utils.ContentUtils.CountLines(theirsContent ?? "");
        if (totalLines > 5000 && !ContinueLargeFile)
        {
            IsLargeFile = true;
            LargeFileLineCount = totalLines;
            _logger.LargeFile(filePath, totalLines);
            _lastBuiltFilePath = filePath;
            CurrentMergeResult = null;
            MergedContent = string.Empty;
            MergedLines.Clear();
            return;
        }

        IsLargeFile = false;

        // Cancel any previous build, dispose its CTS, install a fresh one.
        // The previous pattern (`Cancel()` then overwrite) leaked the old CTS
        // for the life of the process — see plan §1.5.
        var ct = CancellationTokenSourceExtensions.ReplaceAndCancel(ref _buildMergeCts).Token;

        Log.Info("Merge", $"BuildMergeResult: file={filePath} base={baseContent?.Length ?? 0}chars ours={oursContent?.Length ?? 0}chars theirs={theirsContent?.Length ?? 0}chars");

        FileMergeResult result;
        try
        {
            // The Phase 1 engine is async-I/O bound (it shells out to `git merge-file`),
            // so no Task.Run wrap is needed — the call doesn't block the UI thread.
            result = await _mergeService.PerformMergeAsync(
                filePath,
                baseContent ?? string.Empty,
                oursContent ?? string.Empty,
                theirsContent ?? string.Empty,
                ignoreWhitespace: false,
                cancellationToken: ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Log.Info("Merge", $"BuildMergeResult: cancelled for {filePath}");
            return;
        }
        catch (Leaf.Services.Merge.MergeEngineException ex)
        {
            // Engine refused this file. Surface the error to the user via a dedicated
            // UI state — the view binds `IsEngineError`/`EngineErrorMessage` to an overlay
            // offering the external-merge-tool escape hatch (§5.2).
            Log.Error("Merge", $"BuildMergeResult: engine error for {filePath}: {ex.Message}", ex);
            CurrentMergeResult = null;
            MergedContent = string.Empty;
            MergedLines.Clear();
            IsEngineError = true;
            EngineErrorMessage = ex.Message;
            _lastBuiltFilePath = filePath;
            return;
        }

        if (ct.IsCancellationRequested || SelectedConflict?.FilePath != filePath)
        {
            Log.Info("Merge", $"BuildMergeResult: discarded (selection changed) for {filePath}");
            return;
        }

        _lastBuiltFilePath = filePath;
        Log.Info("Merge", $"BuildMergeResult: {result.Regions.Count} regions, unresolved={result.UnresolvedCount}");

        CurrentMergeResult = result;
        _currentRegionIndex = result.GetFirstUnresolvedConflictIndex();
        _undoStack.Clear();
        UpdateResolutionProperties();
        WireConflictLineEvents(result);
        BuildLineMappings(result);

        if (SelectedConflict != null && SelectedConflict.IsResolved && !string.IsNullOrWhiteSpace(SelectedConflict.MergedContent))
        {
            MergedLines.Clear();
            UpdateMergedLinesFromText(SelectedConflict.MergedContent);
        }
        else
        {
            RefreshMergedLines();
        }

        UpdateConflictNavigationLabel();

        if (_currentRegionIndex >= 0)
            RequestScrollToRegion?.Invoke(this, _currentRegionIndex);
    }

    partial void OnSelectedConflictChanged(ConflictInfo? value)
    {
        if (value != null)
        {
            ContinueLargeFile = false;
            _logger.FileTabSelected(value.FileName, value.IsResolved);
            BuildMergeResultForSelectedConflict()
                .FireAndForget(nameof(BuildMergeResultForSelectedConflict), isUserAction: true);
        }
    }

    // --- Resolution Commands ---

    [RelayCommand]
    private void AcceptAllOurs()
    {
        if (CurrentMergeResult == null) return;

        var batch = new List<ResolutionAction>();
        foreach (var region in CurrentMergeResult.Regions.Where(r => r.IsConflict))
        {
            var prev = region.Resolution;
            region.SelectAllOurs();
            if (prev != region.Resolution)
                batch.Add(new ResolutionAction(region.Index, prev, region.Resolution));
        }
        if (batch.Count > 0) _undoStack.PushBatch(batch.ToArray());

        UpdateResolutionProperties();
    }

    [RelayCommand]
    private void AcceptAllTheirs()
    {
        if (CurrentMergeResult == null) return;

        var batch = new List<ResolutionAction>();
        foreach (var region in CurrentMergeResult.Regions.Where(r => r.IsConflict))
        {
            var prev = region.Resolution;
            region.SelectAllTheirs();
            if (prev != region.Resolution)
                batch.Add(new ResolutionAction(region.Index, prev, region.Resolution));
        }
        if (batch.Count > 0) _undoStack.PushBatch(batch.ToArray());

        UpdateResolutionProperties();
    }

    [RelayCommand]
    private void AcceptAllBoth()
    {
        if (CurrentMergeResult == null) return;

        var batch = new List<ResolutionAction>();
        foreach (var region in CurrentMergeResult.Regions.Where(r => r.IsConflict))
        {
            var prev = region.Resolution;
            region.SelectAllBoth();
            if (prev != region.Resolution)
                batch.Add(new ResolutionAction(region.Index, prev, region.Resolution));
            _logger.TakeBothHunk(region.Index, region.OursLines.Count, region.TheirsLines.Count);
        }
        if (batch.Count > 0) _undoStack.PushBatch(batch.ToArray());

        UpdateResolutionProperties();
    }

    [RelayCommand]
    private void TakeOursHunk(MergeRegion? region)
    {
        if (region == null) return;
        var prev = region.Resolution;
        region.SelectAllOurs();
        _undoStack.Push(region.Index, prev, region.Resolution);
        _logger.RegionResolved(region.Index, region.Resolution);
        UpdateResolutionProperties();
    }

    [RelayCommand]
    private void TakeTheirsHunk(MergeRegion? region)
    {
        if (region == null) return;
        var prev = region.Resolution;
        region.SelectAllTheirs();
        _undoStack.Push(region.Index, prev, region.Resolution);
        _logger.RegionResolved(region.Index, region.Resolution);
        UpdateResolutionProperties();
    }

    [RelayCommand]
    private void TakeBothHunk(MergeRegion? region)
    {
        if (region == null) return;
        var prev = region.Resolution;
        region.SelectAllBoth();
        _undoStack.Push(region.Index, prev, region.Resolution);
        _logger.TakeBothHunk(region.Index, region.OursLines.Count, region.TheirsLines.Count);
        _logger.RegionResolved(region.Index, region.Resolution);
        UpdateResolutionProperties();
    }

    // --- Undo/Redo ---

    [RelayCommand]
    private void Undo()
    {
        var actions = _undoStack.Undo();
        if (actions == null || CurrentMergeResult == null) return;

        foreach (var action in actions)
        {
            var region = CurrentMergeResult.Regions.FirstOrDefault(r => r.Index == action.RegionIndex);
            if (region != null)
                ApplyResolution(region, action.PreviousChoice);
        }
        _logger.UndoAction($"reverted {actions.Length} region(s)");
        UpdateResolutionProperties();
    }

    [RelayCommand]
    private void Redo()
    {
        var actions = _undoStack.Redo();
        if (actions == null || CurrentMergeResult == null) return;

        foreach (var action in actions)
        {
            var region = CurrentMergeResult.Regions.FirstOrDefault(r => r.Index == action.RegionIndex);
            if (region != null)
                ApplyResolution(region, action.NewChoice);
        }
        _logger.RedoAction($"restored {actions.Length} region(s)");
        UpdateResolutionProperties();
    }

    private static void ApplyResolution(MergeRegion region, ConflictResolution resolution)
    {
        switch (resolution)
        {
            case ConflictResolution.UseOurs:
                region.SelectAllOurs();
                break;
            case ConflictResolution.UseTheirs:
                region.SelectAllTheirs();
                break;
            case ConflictResolution.UseBoth:
                region.SelectAllBoth();
                break;
            case ConflictResolution.Unresolved:
                region.InitializeSelectableLines();
                if (region.OursSelectableLines != null)
                    foreach (var l in region.OursSelectableLines) l.IsSelected = false;
                if (region.TheirsSelectableLines != null)
                    foreach (var l in region.TheirsSelectableLines) l.IsSelected = false;
                region.Resolution = ConflictResolution.Unresolved;
                break;
        }
    }

    // --- Conflict Navigation ---

    [RelayCommand]
    private void NextRegionConflict()
    {
        if (CurrentMergeResult == null) return;

        var nextIndex = CurrentMergeResult.GetNextUnresolvedConflictIndex(_currentRegionIndex);
        if (nextIndex >= 0)
        {
            _currentRegionIndex = nextIndex;
            UpdateConflictNavigationLabel();
            RequestScrollToRegion?.Invoke(this, nextIndex);
        }
    }

    [RelayCommand]
    private void PreviousRegionConflict()
    {
        if (CurrentMergeResult == null) return;

        var prevIndex = CurrentMergeResult.GetPreviousUnresolvedConflictIndex(_currentRegionIndex);
        if (prevIndex >= 0)
        {
            _currentRegionIndex = prevIndex;
            UpdateConflictNavigationLabel();
            RequestScrollToRegion?.Invoke(this, prevIndex);
        }
    }

    private void UpdateConflictNavigationLabel()
    {
        if (CurrentMergeResult == null || CurrentFileRegionCount == 0)
        {
            ConflictNavigationLabel = string.Empty;
            return;
        }

        // Find which conflict number we're on (1-based among conflict regions)
        var conflictRegions = CurrentMergeResult.Regions.Where(r => r.IsConflict).ToList();
        var currentPos = _currentRegionIndex >= 0
            ? conflictRegions.FindIndex(r => r.Index == _currentRegionIndex) + 1
            : 0;

        ConflictNavigationLabel = currentPos > 0
            ? $"Conflict {currentPos} of {conflictRegions.Count}"
            : $"{conflictRegions.Count} conflicts";
    }

    // --- File Navigation ---

    [RelayCommand]
    private void PreviousConflict()
    {
        if (SelectedConflict == null || Conflicts.Count == 0) return;

        var currentIndex = Conflicts.IndexOf(SelectedConflict);
        if (currentIndex > 0)
            SelectedConflict = Conflicts[currentIndex - 1];
    }

    [RelayCommand]
    private void NextConflict()
    {
        if (SelectedConflict == null || Conflicts.Count == 0) return;

        var currentIndex = Conflicts.IndexOf(SelectedConflict);
        if (currentIndex < Conflicts.Count - 1)
            SelectedConflict = Conflicts[currentIndex + 1];
    }

    // --- File Resolution ---

    [RelayCommand]
    private void CopyMergedResult()
    {
        if (CurrentMergeResult == null) return;

        var content = CurrentMergeResult.GetMergedContent();
        _clipboardService.SetText(content);
    }

    [RelayCommand]
    private async Task UseOursAsync()
    {
        if (SelectedConflict == null) return;

        try
        {
            IsResolving = true;
            await _gitService.ResolveConflictWithOursAsync(_repoPath, SelectedConflict.FilePath, cancellationToken: SessionToken);

            SelectedConflict.MergedContent = SelectedConflict.OursContent;
            SelectedConflict.IsResolved = true;
            UpdateCounts();
            AutoAdvanceToNextFile();
        }
        finally
        {
            IsResolving = false;
        }
    }

    [RelayCommand]
    private async Task UseTheirsAsync()
    {
        if (SelectedConflict == null) return;

        try
        {
            IsResolving = true;
            await _gitService.ResolveConflictWithTheirsAsync(_repoPath, SelectedConflict.FilePath, cancellationToken: SessionToken);

            SelectedConflict.MergedContent = SelectedConflict.TheirsContent;
            SelectedConflict.IsResolved = true;
            UpdateCounts();
            AutoAdvanceToNextFile();
        }
        finally
        {
            IsResolving = false;
        }
    }

    [RelayCommand]
    private async Task MarkResolvedAsync()
    {
        if (SelectedConflict == null || !CanMarkResolved)
            return;

        try
        {
            IsResolving = true;
            Log.Info("Merge", $"MarkResolved: file={SelectedConflict.FilePath}");

            var mergedContent = MergedContent;
            if (string.IsNullOrWhiteSpace(mergedContent) && CurrentMergeResult != null)
                mergedContent = CurrentMergeResult.GetMergedContent();

            await System.IO.File.WriteAllTextAsync(
                System.IO.Path.Combine(_repoPath, SelectedConflict.FilePath),
                mergedContent);

            await _gitService.MarkConflictResolvedAsync(_repoPath, SelectedConflict.FilePath, cancellationToken: SessionToken);

            SelectedConflict.MergedContent = mergedContent;
            SelectedConflict.IsResolved = true;
            UpdateCounts();
            AutoAdvanceToNextFile();
        }
        finally
        {
            IsResolving = false;
        }
    }

    [RelayCommand]
    private async Task CompleteMergeAsync()
    {
        if (!CanCompleteMerge) return;

        try
        {
            IsResolving = true;
            var commitMessage = $"Merge branch '{SourceBranch}' into {TargetBranch}";
            Log.Info("Merge", $"CompleteMerge: message={commitMessage}");
            await _gitService.CompleteMergeAsync(_repoPath, commitMessage, cancellationToken: SessionToken);
            Log.Info("Merge", "CompleteMerge: success");
            Cleanup();
            MergeCompleted?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            Log.Error("Merge", $"CompleteMerge: {ex.Message}", ex);
            Cleanup();
            MergeCompleted?.Invoke(this, false);
            throw;
        }
        finally
        {
            IsResolving = false;
        }
    }

    [RelayCommand]
    private async Task AbortMergeAsync()
    {
        try
        {
            IsResolving = true;
            Log.Info("Merge", "AbortMerge (from ConflictResolutionVM)");
            await _gitService.AbortMergeAsync(_repoPath, cancellationToken: SessionToken);
            Log.Info("Merge", "AbortMerge: completed");
            Cleanup();
            MergeCompleted?.Invoke(this, false);
        }
        finally
        {
            IsResolving = false;
        }
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        _lastBuiltFilePath = null;
        await LoadConflictsAsync(showLoading: false);
        await BuildMergeResultForSelectedConflict();
    }

    [RelayCommand]
    private void ContinueLargeFileLoad()
    {
        ContinueLargeFile = true;
        IsLargeFile = false;
        _lastBuiltFilePath = null;
        BuildMergeResultForSelectedConflict()
            .FireAndForget(nameof(BuildMergeResultForSelectedConflict), isUserAction: true);
    }

    [RelayCommand]
    private async Task UnresolveConflictAsync(ConflictInfo? conflict)
    {
        if (conflict == null || !conflict.IsResolved)
            return;

        try
        {
            IsResolving = true;
            await _gitService.ReopenConflictAsync(_repoPath, conflict.FilePath, conflict.BaseContent, conflict.OursContent, conflict.TheirsContent, cancellationToken: SessionToken);

            conflict.IsResolved = false;
            UpdateCounts();

            _lastBuiltFilePath = null;

            if (SelectedConflict != conflict)
                SelectedConflict = conflict;
            else
                await BuildMergeResultForSelectedConflict();
        }
        finally
        {
            IsResolving = false;
        }
    }

    // --- Auto-advance ---

    private void AutoAdvanceToNextFile()
    {
        var currentFile = SelectedConflict;
        var nextUnresolved = Conflicts.FirstOrDefault(c => !c.IsResolved);
        if (nextUnresolved != null && nextUnresolved != currentFile)
        {
            _logger.AutoAdvance(currentFile?.FileName ?? "?", nextUnresolved.FileName);
            SelectedConflict = nextUnresolved;
        }
    }

    // --- Merged Line Building ---

    private void RefreshMergedLines()
    {
        if (CurrentMergeResult == null)
        {
            MergedContent = string.Empty;
            MergedLines.Clear();
            return;
        }

        _wiredMergedLines.Clear();

        var newLines = new List<MergedLine>();
        foreach (var region in CurrentMergeResult.Regions)
        {
            var lines = GetRegionLines(region);
            foreach (var (line, source) in lines)
                newLines.Add(new MergedLine { Content = line, Source = source });
        }

        MergedLines.ReplaceAll(newLines);
        UpdateMergedContentFromLines();

        _logger.ProgressUpdate(ResolvedCount, TotalCount,
            CurrentFileResolvedRegionCount, CurrentFileRegionCount);
    }

    private List<(string line, MergedLineSource source)> GetRegionLines(MergeRegion region)
    {
        if (region.Type != MergeRegionType.Conflict)
        {
            var contentLines = SplitLines(region.Content);
            var source = region.Type switch
            {
                MergeRegionType.OursOnly => MergedLineSource.Ours,
                MergeRegionType.TheirsOnly => MergedLineSource.Theirs,
                _ => MergedLineSource.None
            };
            return contentLines.Select(l => (l, source)).ToList();
        }

        return region.Resolution switch
        {
            ConflictResolution.UseOurs => region.OursLines.Select(l => (l, MergedLineSource.Ours)).ToList(),
            ConflictResolution.UseTheirs => region.TheirsLines.Select(l => (l, MergedLineSource.Theirs)).ToList(),
            ConflictResolution.UseBoth => GetBothLines(region),
            ConflictResolution.UseCustom => GetCustomSelectedLines(region),
            ConflictResolution.UseManual => SplitLines(region.ManualEditContent)
                .Select(l => (l, MergedLineSource.Manual)).ToList(),
            _ => GetEmptyConflictLines(region).Select(l => (l, MergedLineSource.None)).ToList()
        };
    }

    private static List<(string line, MergedLineSource source)> GetBothLines(MergeRegion region)
    {
        var lines = new List<(string line, MergedLineSource source)>();
        lines.AddRange(region.OursLines.Select(l => (l, MergedLineSource.Ours)));
        lines.AddRange(region.TheirsLines.Select(l => (l, MergedLineSource.Theirs)));
        return lines;
    }

    private static List<(string line, MergedLineSource source)> GetCustomSelectedLines(MergeRegion region)
    {
        var lines = new List<(string line, MergedLineSource source)>();
        if (region.OursSelectableLines != null)
        {
            lines.AddRange(region.OursSelectableLines
                .Where(l => l.IsSelected)
                .Select(l => (l.Content, MergedLineSource.Ours)));
        }
        if (region.TheirsSelectableLines != null)
        {
            lines.AddRange(region.TheirsSelectableLines
                .Where(l => l.IsSelected)
                .Select(l => (l.Content, MergedLineSource.Theirs)));
        }
        return lines;
    }

    private static List<string> GetEmptyConflictLines(MergeRegion region)
    {
        var count = Math.Max(region.OursLines.Count, region.TheirsLines.Count);
        if (count <= 0) count = 1;
        return Enumerable.Repeat(string.Empty, count).ToList();
    }

    private static List<string> SplitLines(string content)
    {
        if (content == null) return [];
        if (content.Length == 0) return [string.Empty];
        return content.Split('\n').ToList();
    }

    private void WireConflictLineEvents(FileMergeResult result)
    {
        _wiredSelectableLines.Clear();

        foreach (var region in result.Regions.Where(r => r.IsConflict))
        {
            region.InitializeSelectableLines();

            if (region.OursSelectableLines != null)
            {
                foreach (var line in region.OursSelectableLines)
                {
                    if (_wiredSelectableLines.Add(line))
                    {
                        line.PropertyChanged += (s, args) =>
                        {
                            if (args.PropertyName == nameof(SelectableLine.IsSelected))
                            {
                                region.UpdateResolutionFromSelection();
                                UpdateResolutionProperties();
                            }
                        };
                    }
                }
            }

            if (region.TheirsSelectableLines != null)
            {
                foreach (var line in region.TheirsSelectableLines)
                {
                    if (_wiredSelectableLines.Add(line))
                    {
                        line.PropertyChanged += (s, args) =>
                        {
                            if (args.PropertyName == nameof(SelectableLine.IsSelected))
                            {
                                region.UpdateResolutionFromSelection();
                                UpdateResolutionProperties();
                            }
                        };
                    }
                }
            }
        }
    }

    // --- State Updates ---

    private void UpdateCounts()
    {
        OnPropertyChanged(nameof(ResolvedCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(RemainingCount));
        OnPropertyChanged(nameof(CanCompleteMerge));
        OnPropertyChanged(nameof(FileProgressPercent));
        RefreshConflictBuckets();
        // Persist merge-conflict resolution state — if this faults, the user
        // loses recovery state across a restart, so it's strictly a user-action
        // error regardless of how UpdateCounts was triggered.
        _gitService.SaveStoredMergeConflictFilesAsync(_repoPath, Conflicts.Select(c => c.FilePath), cancellationToken: SessionToken)
            .FireAndForget(nameof(_gitService.SaveStoredMergeConflictFilesAsync), isUserAction: true);
    }

    private void UpdateResolutionProperties()
    {
        CurrentMergeResult?.NotifyResolutionChanged();
        OnPropertyChanged(nameof(HasUnresolvedConflicts));
        OnPropertyChanged(nameof(CanMarkResolved));
        OnPropertyChanged(nameof(CurrentFileRegionCount));
        OnPropertyChanged(nameof(CurrentFileResolvedRegionCount));
        OnPropertyChanged(nameof(RegionProgressPercent));
        OnPropertyChanged(nameof(ConflictRegions));
        UpdateConflictNavigationLabel();
        RefreshMergedLines();
    }

    private void UpdateMergedContentFromLines()
    {
        foreach (var line in MergedLines)
        {
            if (_wiredMergedLines.Add(line))
            {
                line.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(MergedLine.Content))
                    {
                        if (line.Source != MergedLineSource.Manual)
                            line.Source = MergedLineSource.Manual;
                        DebounceMergedContentUpdate();
                    }
                };
            }
        }

        MergedContent = string.Join("\n", MergedLines.Select(l => l.Content));
        OnPropertyChanged(nameof(CanMarkResolved));
    }

    private void DebounceMergedContentUpdate()
    {
        _mergedContentDebounceTimer?.Stop();
        _mergedContentDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _mergedContentDebounceTimer.Tick -= OnMergedContentDebounceTimer;
        _mergedContentDebounceTimer.Tick += OnMergedContentDebounceTimer;
        _mergedContentDebounceTimer.Start();
    }

    private void OnMergedContentDebounceTimer(object? sender, EventArgs e)
    {
        _mergedContentDebounceTimer?.Stop();
        MergedContent = string.Join("\n", MergedLines.Select(l => l.Content));
        OnPropertyChanged(nameof(CanMarkResolved));
    }

    /// <summary>
    /// Stops and cleans up timer and cancellation token resources.
    /// Call before raising MergeCompleted to prevent DispatcherTimer rooting the ViewModel.
    /// </summary>
    public void Cleanup()
    {
        _mergedContentDebounceTimer?.Stop();
        _mergedContentDebounceTimer = null;
        CancellationTokenSourceExtensions.DisposeAndClear(ref _buildMergeCts);
    }

    public void UpdateMergedLinesFromText(string text)
    {
        var lines = text.Split('\n');
        if (lines.Length == 0)
            lines = [string.Empty];

        for (int i = 0; i < lines.Length; i++)
        {
            if (i < MergedLines.Count)
            {
                if (!string.Equals(MergedLines[i].Content, lines[i], StringComparison.Ordinal))
                {
                    MergedLines[i].Content = lines[i];
                    if (MergedLines[i].Source != MergedLineSource.Manual)
                        MergedLines[i].Source = MergedLineSource.Manual;
                }
            }
            else
            {
                MergedLines.Add(new MergedLine { Content = lines[i], Source = MergedLineSource.Manual });
            }
        }

        while (MergedLines.Count > lines.Length)
            MergedLines.RemoveAt(MergedLines.Count - 1);

        MergedContent = text;
        OnPropertyChanged(nameof(CanMarkResolved));
    }

    // --- Line Mapping Building ---

    private void BuildLineMappings(FileMergeResult result)
    {
        var (oursMapping, oursContent, theirsMapping, theirsContent) =
            ConflictSideLineMapping.BuildAligned(result);

        Debug.Assert(oursMapping.TotalLines == theirsMapping.TotalLines,
            $"Line count mismatch: ours={oursMapping.TotalLines} theirs={theirsMapping.TotalLines}");

        // Row-by-row structural alignment check
        for (int i = 1; i <= oursMapping.TotalLines; i++)
        {
            var oursKind = oursMapping.GetLineKind(i);
            var theirsKind = theirsMapping.GetLineKind(i);

            if (oursKind == ConflictViewLineKind.Header)
                Debug.Assert(theirsKind == ConflictViewLineKind.Header,
                    $"Line {i}: ours=Header but theirs={theirsKind}");

            if (oursKind == ConflictViewLineKind.Spacer)
            {
                Debug.Assert(theirsKind == ConflictViewLineKind.Content,
                    $"Line {i}: ours=Spacer but theirs={theirsKind}");
                Debug.Assert(oursMapping.GetRegionForLine(i) == theirsMapping.GetRegionForLine(i),
                    $"Line {i}: Spacer/Content region mismatch");
            }
            if (theirsKind == ConflictViewLineKind.Spacer)
            {
                Debug.Assert(oursKind == ConflictViewLineKind.Content,
                    $"Line {i}: theirs=Spacer but ours={oursKind}");
                Debug.Assert(oursMapping.GetRegionForLine(i) == theirsMapping.GetRegionForLine(i),
                    $"Line {i}: Content/Spacer region mismatch");
            }
        }

        // Set content before mappings — the view listens for mapping changes
        // and reads content at that point, so content must be populated first
        OursFileContent = oursContent;
        TheirsFileContent = theirsContent;
        OursLineMapping = oursMapping;
        TheirsLineMapping = theirsMapping;

        Log.Info("Merge", $"BuildLineMappings: aligned={oursMapping.TotalLines} lines, conflicts={oursMapping.AllConflictRanges.Count}");
    }

    /// <summary>
    /// Commit gate: content contains an unresolved zdiff3 conflict iff it has the full
    /// structural triad — an opener, a separator, and a close marker in order.
    /// A lone marker-lookalike (e.g. user documentation that contains <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c>
    /// but no <c>=======</c>/<c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c>) is content, not unresolved state.
    /// </summary>
    private static bool ContainsConflictMarkers(string content)
    {
        bool sawOpen = false;
        bool sawSeparator = false;
        foreach (var line in content.Split('\n'))
        {
            if (!sawOpen)
            {
                if (line.StartsWith("<<<<<<<", StringComparison.Ordinal))
                {
                    sawOpen = true;
                }
                continue;
            }

            if (!sawSeparator)
            {
                if (line == "=======")
                {
                    sawSeparator = true;
                }
                continue;
            }

            if (line.StartsWith(">>>>>>>", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private void RefreshConflictBuckets()
    {
        ConflictedConflicts.Clear();
        ResolvedConflicts.Clear();

        foreach (var conflict in Conflicts)
        {
            if (conflict.IsResolved)
                ResolvedConflicts.Add(conflict);
            else
                ConflictedConflicts.Add(conflict);
        }
    }
}
