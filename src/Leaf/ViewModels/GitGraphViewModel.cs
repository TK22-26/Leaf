using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Graph;
using Leaf.Models;
using Leaf.Services;
using Leaf.Utils;

namespace Leaf.ViewModels;

/// <summary>
/// ViewModel for the Git graph and commit list view.
/// </summary>
public partial class GitGraphViewModel : ObservableObject
{
    /// <summary>
    /// Special SHA value indicating working changes are selected.
    /// </summary>
    public const string WorkingChangesSha = "WORKING_CHANGES";

    private readonly IGitService _gitService;

    // The active builder that owns colour state for the currently-displayed
    // graph. Swapped atomically with <see cref="Nodes"/> and
    // <see cref="ColorResolver"/> at the end of a background build so the
    // canvas can never render nodes from one repository against another
    // repository's colour context.
    private GraphBuilder _graphBuilder = new();

    // Context deferred from SetGitFlowContext until the next LoadRepositoryAsync
    // consumes it and atomically swaps the builder. _hasPendingContext
    // distinguishes "caller hasn't set context yet" from "caller explicitly
    // set context to null" (which happens on every non-GitFlow repo).
    private bool _hasPendingContext;
    private GitFlowConfig? _pendingGitFlowConfig;
    private IReadOnlyList<string>? _pendingRemoteNames;

    // The context currently baked into _graphBuilder. Used by pagination and
    // tooltip preview builders so they inherit the same context without racing
    // with SetGitFlowContext stores.
    private GitFlowConfig? _activeGitFlowConfig;
    private IReadOnlyList<string>? _activeRemoteNames;

    private readonly Dictionary<string, Task<MergeCommitTooltipViewModel?>> _mergeTooltipTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _branchTips = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hiddenBranchNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _soloBranchNames = new(StringComparer.OrdinalIgnoreCase);
    private List<CommitInfo> _allCommits = [];
    private string? _currentBranchName;

    // Lazy loading state
    private int _loadedCommitCount;
    private bool _hasMoreCommits = true;
    private HashSet<string> _loadedCommitShas = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _graphBuildCts;

    /// <summary>
    /// Cancels any in-flight graph build and returns a token for the new one.
    /// Delegates to the shared <see cref="CancellationTokenSourceExtensions"/>
    /// helper so every CTS-replace site in the codebase leaks consistently — or,
    /// rather, doesn't.
    /// </summary>
    private CancellationToken BeginGraphBuild()
        => CancellationTokenSourceExtensions.ReplaceAndCancel(ref _graphBuildCts).Token;
    private const int BatchSize = 1000;  // Large batch to minimize O(n) skip cost
    private const int InitialBatchSize = 200;

    /// <summary>
    /// Per-repository colour resolver. Bound by <c>GitGraphCanvas</c> and the
    /// tooltip canvas; updated atomically with <see cref="Nodes"/> when a new
    /// repository load completes so the canvas never resolves colours against
    /// the wrong context.
    /// </summary>
    [ObservableProperty]
    private IBranchColorResolver _colorResolver;

    [ObservableProperty]
    private string? _repositoryPath;

    [ObservableProperty]
    private ObservableCollection<CommitInfo> _commits = [];

    [ObservableProperty]
    private ObservableCollection<GitTreeNode> _nodes = [];

    [ObservableProperty]
    private CommitInfo? _selectedCommit;

    [ObservableProperty]
    private string? _selectedSha;

    [ObservableProperty]
    private string? _hoveredSha;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isSearchActive;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _isLoadingMore;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _maxLane;

    [ObservableProperty]
    private double _rowHeight = 28.0;

    [ObservableProperty]
    private double _totalHeight;

    /// <summary>
    /// Working directory changes (staged and unstaged files).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkingChanges))]
    private WorkingChangesInfo? _workingChanges;

    /// <summary>
    /// True if working changes node is currently selected.
    /// </summary>
    [ObservableProperty]
    private bool _isWorkingChangesSelected;

    /// <summary>
    /// True if HEAD is detached (not on a branch).
    /// </summary>
    [ObservableProperty]
    private bool _isDetachedHead;

    /// <summary>
    /// SHA of the detached HEAD commit (null if on a branch).
    /// </summary>
    [ObservableProperty]
    private string? _detachedHeadSha;

    /// <summary>
    /// True if there are any working directory changes.
    /// </summary>
    public bool HasWorkingChanges => WorkingChanges?.HasChanges ?? false;

    /// <summary>
    /// Stashes in the repository.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStashes))]
    private ObservableCollection<StashInfo> _stashes = [];

    /// <summary>
    /// Currently selected stash (if any).
    /// </summary>
    [ObservableProperty]
    private StashInfo? _selectedStash;

    /// <summary>
    /// True if there are any stashes.
    /// </summary>
    public bool HasStashes => Stashes.Count > 0;

    public GitGraphViewModel(IGitService gitService)
    {
        _gitService = gitService;
        _colorResolver = _graphBuilder;
    }

    /// <summary>
    /// Creates synthetic CommitInfo entries for stashes.
    /// Each stash points directly to its actual parent commit (the commit HEAD was on when stashed).
    /// </summary>
    private static List<CommitInfo> CreateStashPseudoCommits(IReadOnlyList<StashInfo> stashes)
    {
        if (stashes.Count == 0)
            return [];

        var pseudoCommits = new List<CommitInfo>(stashes.Count);

        foreach (var stash in stashes)
        {
            pseudoCommits.Add(new CommitInfo
            {
                Sha = stash.Sha,
                Message = stash.Message,
                MessageShort = stash.MessageShort,
                Author = stash.Author,
                AuthorEmail = string.Empty,
                Date = stash.Date,
                ParentShas = string.IsNullOrEmpty(stash.ParentSha) ? [] : [stash.ParentSha],
                IsStash = true,
                StashIndex = stash.Index
            });
        }

        return pseudoCommits;
    }

    /// <summary>
    /// Merges stash pseudo-commits into the commit list positioned directly above their parent commit.
    /// This matches GitKraken behavior — stashes are children of their parent in the DAG,
    /// so they appear as a spur immediately before the commit HEAD was on when stashed.
    /// </summary>
    private List<CommitInfo> MergeStashPseudoCommits(List<CommitInfo> commits)
    {
        var pseudoCommits = CreateStashPseudoCommits(Stashes);
        if (pseudoCommits.Count == 0)
            return commits;

        // Group stashes by parent SHA; within each group, sort by StashIndex ascending
        // so index 0 (newest) is farthest from parent, matching topo-order output
        var stashesByParent = new Dictionary<string, List<CommitInfo>>();
        var orphanStashes = new List<CommitInfo>();

        foreach (var stash in pseudoCommits)
        {
            var parentSha = stash.ParentShas.Count > 0 ? stash.ParentShas[0] : null;
            if (string.IsNullOrEmpty(parentSha))
            {
                orphanStashes.Add(stash);
            }
            else
            {
                if (!stashesByParent.TryGetValue(parentSha, out var group))
                {
                    group = [];
                    stashesByParent[parentSha] = group;
                }
                group.Add(stash);
            }
        }

        foreach (var group in stashesByParent.Values)
            group.Sort((a, b) => a.StashIndex.CompareTo(b.StashIndex));

        orphanStashes.Sort((a, b) => a.StashIndex.CompareTo(b.StashIndex));

        // Build result: insert stash groups immediately before their parent commit
        var result = new List<CommitInfo>(pseudoCommits.Count + commits.Count);

        // Orphaned stashes (no parent or parent not loaded) go to the top
        result.AddRange(orphanStashes);

        foreach (var commit in commits)
        {
            // If this commit is a parent of stashes, insert them before it
            if (stashesByParent.Remove(commit.Sha, out var group))
                result.AddRange(group);

            result.Add(commit);
        }

        // Any remaining stashes whose parent wasn't found in the commit list go to the top
        foreach (var group in stashesByParent.Values)
            result.InsertRange(0, group);

        return result;
    }

    /// <summary>
    /// Stores new GitFlow + remote-name context for the next graph build to
    /// consume. Does NOT mutate the active builder — if it did, the canvas
    /// would briefly render still-displayed nodes against the new colour
    /// context. The pending values are applied atomically inside
    /// <see cref="LoadRepositoryAsync"/> together with the new node list.
    /// </summary>
    public void SetGitFlowContext(GitFlowConfig? config, IReadOnlyCollection<string> remoteNames)
    {
        // Defensive copy — the config we receive may be long-lived and
        // mutated by its owner before LoadRepositoryAsync runs.
        _pendingGitFlowConfig = config?.Clone();
        _pendingRemoteNames = remoteNames != null ? remoteNames.ToList() : null;
        _hasPendingContext = true;
    }

    /// <summary>
    /// Load commits for a repository.
    /// </summary>
    [RelayCommand]
    public async Task LoadRepositoryAsync(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        Log.Info("Graph", $"Loading repository graph: {path}");
        var sw = Log.StartTimer();

        try
        {
            if (!string.Equals(RepositoryPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _mergeTooltipTasks.Clear();
                _hiddenBranchNames.Clear();
                _soloBranchNames.Clear();
                _branchTips.Clear();

                // Reset lazy loading state on repo change
                _loadedCommitCount = 0;
                _hasMoreCommits = true;
                IsLoadingMore = false;
                _loadedCommitShas.Clear();
            }

            // Only show loading overlay on initial load (no existing data)
            // This prevents flashing when refreshing
            bool isInitialLoad = Commits.Count == 0;
            if (isInitialLoad)
            {
                IsLoading = true;
            }

            ErrorMessage = null;
            RepositoryPath = path;

            // Load working changes, commits, and stashes in parallel
            var workingChangesTask = _gitService.GetWorkingChangesAsync(path);
            var commitsTask = _gitService.GetCommitHistoryAsync(path, InitialBatchSize);
            var stashesTask = _gitService.GetStashesAsync(path);

            await Task.WhenAll(workingChangesTask, commitsTask, stashesTask);

            var workingChanges = await workingChangesTask;
            var commits = await commitsTask;
            var stashes = await stashesTask;

            // When detached, don't set a current branch name - let graph builder use default priority
            if (workingChanges?.IsDetachedHead == true)
            {
                _currentBranchName = null;
            }
            else
            {
                _currentBranchName = workingChanges?.BranchName;
                if (string.IsNullOrWhiteSpace(_currentBranchName))
                {
                    _currentBranchName = commits
                        .SelectMany(c => c.BranchLabels)
                        .FirstOrDefault(l => l.IsCurrent)?.Name;
                }
            }

            // Capture selection state BEFORE RebuildGraphFromFilters clears it
            // (RebuildGraphFromFilters clears SelectedCommit when old instance isn't in new Commits)
            bool wasWorkingChangesSelected = IsWorkingChangesSelected;
            var wasSelectedStashIndex = SelectedStash?.Index;
            var wasSelectedCommitSha = SelectedCommit?.Sha;

            _allCommits = commits.ToList();
            WorkingChanges = workingChanges;
            IsDetachedHead = workingChanges?.IsDetachedHead ?? false;
            DetachedHeadSha = workingChanges?.DetachedHeadSha;
            Stashes = new ObservableCollection<StashInfo>(stashes);

            // Initialize lazy loading state
            _loadedCommitCount = commits.Count;
            _loadedCommitShas = new HashSet<string>(commits.Select(c => c.Sha), StringComparer.OrdinalIgnoreCase);
            _hasMoreCommits = commits.Count == InitialBatchSize;
            IsLoadingMore = false;

            // Build graph on background thread (heavy computation)
            var visibleCommits = GetVisibleCommits();
            var commitsWithStashes = MergeStashPseudoCommits(visibleCommits);
            var currentBranch = _currentBranchName;

            // Cancel any in-flight build; if a second LoadRepositoryAsync
            // starts before the first completes, only the most recent one
            // will swap its results into the UI.
            var ct = BeginGraphBuild();

            // Consume pending GitFlow/remote context for the new builder.
            // If SetGitFlowContext has been called since the last load,
            // adopt those values (even when they are null — that's the
            // correct context for a repository without GitFlow). Otherwise
            // stay on whatever the previous load applied.
            GitFlowConfig? effectiveConfig;
            IReadOnlyList<string>? effectiveRemotes;
            if (_hasPendingContext)
            {
                effectiveConfig = _pendingGitFlowConfig;
                effectiveRemotes = _pendingRemoteNames;
                _pendingGitFlowConfig = null;
                _pendingRemoteNames = null;
                _hasPendingContext = false;
            }
            else
            {
                effectiveConfig = _activeGitFlowConfig;
                effectiveRemotes = _activeRemoteNames;
            }

            // Build the new builder on the UI thread (cheap — just defensive
            // copies) so the config snapshot is taken before any awaiting.
            var newBuilder = new GraphBuilder(effectiveConfig, effectiveRemotes);

            var (graphNodes, graphMaxLane) = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var builtNodes = newBuilder.BuildGraph(commitsWithStashes, currentBranch);
                return (builtNodes, newBuilder.MaxLane);
            }, ct);

            ct.ThrowIfCancellationRequested();

            // Atomic swap: builder + resolver + nodes flip together so the
            // canvas can never render new nodes against the old resolver or
            // old nodes against the new resolver.
            _graphBuilder = newBuilder;
            _activeGitFlowConfig = effectiveConfig;
            _activeRemoteNames = effectiveRemotes;
            ColorResolver = newBuilder;
            Nodes = new ObservableCollection<GitTreeNode>(graphNodes);
            Commits = new ObservableCollection<CommitInfo>(commitsWithStashes);
            MaxLane = graphMaxLane;

            int rowCount = Commits.Count + (HasWorkingChanges ? 1 : 0);
            TotalHeight = rowCount * RowHeight;

            SelectedCommit = null;
            SelectedStash = wasSelectedStashIndex.HasValue && wasSelectedStashIndex.Value < stashes.Count
                ? stashes[wasSelectedStashIndex.Value]
                : null;
            SelectedSha = wasWorkingChangesSelected ? WorkingChangesSha : wasSelectedCommitSha;
            IsWorkingChangesSelected = wasWorkingChangesSelected && HasWorkingChanges;

            // Try to restore the previously selected commit
            if (!string.IsNullOrEmpty(wasSelectedCommitSha) && !wasWorkingChangesSelected)
            {
                var restoredCommit = _allCommits.FirstOrDefault(c => c.Sha == wasSelectedCommitSha);
                if (restoredCommit != null)
                {
                    SelectedCommit = restoredCommit;
                }
            }

            // Auto-select working changes or first commit when loading a new repository
            // (only if nothing was preserved from previous selection)
            if (!IsWorkingChangesSelected && SelectedStash == null && SelectedCommit == null)
            {
                if (HasWorkingChanges)
                {
                    IsWorkingChangesSelected = true;
                }
                else if (_allCommits.Count > 0)
                {
                    SelectedCommit = _allCommits[0];
                }
            }

            ApplySearchFilter(SearchText);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a later LoadRepositoryAsync — drop our results
            // silently; the newer call will update the UI.
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load repository: {ex.Message}";
        }
        finally
        {
            Log.Perf("Graph", $"LoadRepositoryAsync for {path}", sw.ElapsedMilliseconds);
            IsLoading = false;
        }
    }

    /// <summary>
    /// Fast refresh after branch/tag checkout. Patches IsCurrent/IsHead flags in-place
    /// and refreshes only working changes — no commit re-fetch or graph rebuild.
    /// Always does a full reload to ensure branch labels are on the correct commits.
    /// </summary>
    public async Task RefreshAfterCheckoutAsync(string? newBranchName, string? detachedHeadSha)
    {
        if (string.IsNullOrEmpty(RepositoryPath))
            return;

        await LoadRepositoryAsync(RepositoryPath);
    }

    /// <summary>
    /// Refresh working changes only (faster than full reload).
    /// </summary>
    public async Task RefreshWorkingChangesAsync()
    {
        if (string.IsNullOrEmpty(RepositoryPath))
            return;

        try
        {
            WorkingChanges = await _gitService.GetWorkingChangesAsync(RepositoryPath);

            // Recalculate total height (stashes are included in Commits)
            int rowCount = Commits.Count;
            if (HasWorkingChanges)
            {
                rowCount += 1;
            }
            TotalHeight = rowCount * RowHeight;
        }
        catch
        {
            // Silently fail - don't disrupt the UI
        }
    }

    /// <summary>
    /// Select the working changes node.
    /// </summary>
    [RelayCommand]
    public void SelectWorkingChanges()
    {
        // Deselect any selected commit or stash
        if (SelectedCommit != null)
        {
            SelectedCommit.IsSelected = false;
            SelectedCommit = null;
        }
        if (SelectedStash != null)
        {
            SelectedStash.IsSelected = false;
            SelectedStash = null;
        }

        IsWorkingChangesSelected = true;
        SelectedSha = WorkingChangesSha;
    }

    /// <summary>
    /// Select a stash.
    /// </summary>
    [RelayCommand]
    public void SelectStash(StashInfo? stash)
    {
        // Deselect any selected commit or working changes
        if (SelectedCommit != null)
        {
            SelectedCommit.IsSelected = false;
            SelectedCommit = null;
        }
        IsWorkingChangesSelected = false;

        // Deselect previously selected stash
        if (SelectedStash != null)
        {
            SelectedStash.IsSelected = false;
        }

        SelectedStash = stash;
        SelectedSha = stash?.Sha;

        // Mark the new stash as selected
        if (stash != null)
        {
            stash.IsSelected = true;
        }
    }

    /// <summary>
    /// Refresh the current repository.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadRepositoryAsync(RepositoryPath);
    }

    /// <summary>
    /// Load more commits when scrolling near bottom.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLoadMoreCommits))]
    private async Task LoadMoreCommitsAsync()
    {
        if (IsLoadingMore || !_hasMoreCommits || string.IsNullOrEmpty(RepositoryPath))
            return;

        IsLoadingMore = true;

        // Cancel any in-flight graph build and dispose its CTS (prevents the
        // leak flagged in §1.5).
        var ct = BeginGraphBuild();

        try
        {
            var moreCommits = await _gitService.GetCommitHistoryAsync(
                RepositoryPath,
                BatchSize,
                skip: _loadedCommitCount);

            // Check if we've reached the end
            if (moreCommits.Count < BatchSize)
            {
                _hasMoreCommits = false;
            }

            if (moreCommits.Count == 0)
            {
                return;
            }

            // Dedupe by SHA - filter out already-loaded commits
            var newCommits = moreCommits
                .Where(c => !_loadedCommitShas.Contains(c.Sha))
                .ToList();

            // Append to collections (fast, no graph yet)
            foreach (var commit in newCommits)
            {
                _allCommits.Add(commit);
                _loadedCommitShas.Add(commit.Sha);
            }

            _loadedCommitCount += moreCommits.Count;

            // Clean up ancestor-fallback labels whose real tip commits are now loaded.
            // When the initial batch placed labels on ancestor commits (because tips were
            // outside the loaded range), and a later batch now includes the real tip commit,
            // remove the stale fallback labels to prevent duplicates.
            foreach (var commit in _allCommits)
            {
                commit.BranchLabels.RemoveAll(l =>
                    l.IsAncestorFallback &&
                    !string.IsNullOrEmpty(l.TipSha) &&
                    _loadedCommitShas.Contains(l.TipSha));
            }

            // Capture state for background work
            var visibleCommits = GetVisibleCommits();
            var commitsWithStashes = MergeStashPseudoCommits(visibleCommits);
            var currentBranch = _currentBranchName;

            // Build graph on background thread with a fresh GraphBuilder
            // bound to the currently-active context (pagination never changes
            // colour context — only LoadRepositoryAsync does). Isolated
            // instance means MaxLane mutation cannot race the UI-thread
            // builder.
            var tempBuilder = new GraphBuilder(_activeGitFlowConfig, _activeRemoteNames);
            var (nodes, maxLane) = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var graphNodes = tempBuilder.BuildGraph(commitsWithStashes, currentBranch);
                return (graphNodes, tempBuilder.MaxLane);
            }, ct);

            ct.ThrowIfCancellationRequested();

            // Atomic swap of builder + resolver + nodes keeps rendering
            // consistent even though the context itself did not change.
            _graphBuilder = tempBuilder;
            ColorResolver = tempBuilder;
            Nodes = new ObservableCollection<GitTreeNode>(nodes);
            Commits = new ObservableCollection<CommitInfo>(commitsWithStashes);
            MaxLane = maxLane;

            // Recalculate height (stashes are included in Commits)
            int rowCount = Commits.Count + (HasWorkingChanges ? 1 : 0);
            TotalHeight = rowCount * RowHeight;

            // Handle selection (use SHA comparison - new list has new instances)
            if (SelectedCommit != null && !Commits.Any(c => c.Sha == SelectedCommit.Sha))
            {
                SelectedCommit.IsSelected = false;
                SelectedCommit = null;
                SelectedSha = null;
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled - another load started, ignore
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    private bool CanLoadMoreCommits() =>
        _hasMoreCommits &&
        !IsLoadingMore &&
        !IsSearchActive;  // Don't load during search - causes jarring reorders

    /// <summary>
    /// Select a commit by SHA.
    /// </summary>
    [RelayCommand]
    public void SelectCommit(CommitInfo? commit)
    {
        // Clear working changes selection when selecting a commit
        IsWorkingChangesSelected = false;

        // If selecting a stash pseudo-commit, also set SelectedStash
        if (commit?.IsStash == true)
        {
            // Find the matching StashInfo
            var matchingStash = Stashes.FirstOrDefault(s => s.Index == commit.StashIndex);
            if (SelectedStash != null)
                SelectedStash.IsSelected = false;
            SelectedStash = matchingStash;
            if (matchingStash != null)
                matchingStash.IsSelected = true;
        }
        else
        {
            if (SelectedStash != null)
            {
                SelectedStash.IsSelected = false;
                SelectedStash = null;
            }
        }

        SelectedCommit = commit;
        SelectedSha = commit?.Sha;
    }

    /// <summary>
    /// Select a commit by index (for list selection).
    /// </summary>
    public void SelectCommitByIndex(int index)
    {
        if (index >= 0 && index < Commits.Count)
        {
            SelectCommit(Commits[index]);
        }
    }

    partial void OnSelectedCommitChanged(CommitInfo? oldValue, CommitInfo? newValue)
    {
        // Update IsSelected on old and new commits
        if (oldValue != null)
            oldValue.IsSelected = false;
        if (newValue != null)
            newValue.IsSelected = true;

        SelectedSha = newValue?.Sha;

        // Update canvas to redraw trails with correct opacity
        UpdateNodeSearchState();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplySearchFilter(value);
    }

    /// <summary>
    /// Apply search filter to commits and nodes.
    /// </summary>
    public void ApplySearchFilter(string searchText)
    {
        var trimmed = searchText?.Trim() ?? string.Empty;
        bool hasSearch = !string.IsNullOrEmpty(trimmed);
        CommitInfo? firstMatch = null;

        foreach (var commit in Commits)
        {
            if (hasSearch)
            {
                // Check if commit matches search (message or SHA)
                bool matches = commit.MessageShort.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                               commit.Message.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                               commit.Sha.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) ||
                               commit.Author.Contains(trimmed, StringComparison.OrdinalIgnoreCase);

                commit.IsSearchHighlighted = matches;
                commit.IsDimmed = !matches;

                // Track first match (any type) for auto-scroll
                if (firstMatch == null && matches)
                {
                    firstMatch = commit;
                }
            }
            else
            {
                // No search active - clear all flags
                commit.IsSearchHighlighted = false;
                commit.IsDimmed = false;
            }
        }

        // Update nodes for canvas trail rendering
        UpdateNodeSearchState();

        // Set IsSearchActive AFTER updating data, so canvas renders with correct state
        IsSearchActive = hasSearch;

        // Auto-select first match to trigger scroll (only when nothing is selected)
        if (firstMatch != null && SelectedCommit == null)
        {
            SelectCommit(firstMatch);
        }
    }

    /// <summary>
    /// Apply branch filters to the graph (hidden/solo branches).
    /// </summary>
    public void ApplyBranchFilters(
        IEnumerable<string> hiddenBranchNames,
        IEnumerable<string> soloBranchNames,
        IDictionary<string, string> branchTips)
    {
        _hiddenBranchNames.Clear();
        _soloBranchNames.Clear();
        _branchTips.Clear();

        if (hiddenBranchNames != null)
        {
            foreach (var name in hiddenBranchNames)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _hiddenBranchNames.Add(name);
                }
            }
        }

        if (soloBranchNames != null)
        {
            foreach (var name in soloBranchNames)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _soloBranchNames.Add(name);
                }
            }
        }

        if (branchTips != null)
        {
            foreach (var (name, sha) in branchTips)
            {
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(sha))
                {
                    _branchTips[name] = sha;
                }
            }
        }

        RebuildGraphFromFilters();
        ApplySearchFilter(SearchText);
    }

    private void RebuildGraphFromFilters()
    {
        if (_allCommits.Count == 0)
        {
            Nodes = [];
            Commits = [];
            MaxLane = 0;
            TotalHeight = (HasWorkingChanges ? 1 : 0) * RowHeight;
            return;
        }

        var visibleCommits = GetVisibleCommits();
        var commitsWithStashes = MergeStashPseudoCommits(visibleCommits);

        var nodes = _graphBuilder.BuildGraph(commitsWithStashes, _currentBranchName);
        Nodes = new ObservableCollection<GitTreeNode>(nodes);
        Commits = new ObservableCollection<CommitInfo>(commitsWithStashes);
        MaxLane = _graphBuilder.MaxLane;

        // Calculate total height (stashes are included in Commits)
        int rowCount = Commits.Count;
        if (HasWorkingChanges)
        {
            rowCount += 1; // Add one row for working changes
        }
        TotalHeight = rowCount * RowHeight;

        if (SelectedCommit != null && !Commits.Contains(SelectedCommit))
        {
            SelectedCommit.IsSelected = false;
            SelectedCommit = null;
            SelectedSha = null;
        }
    }

    private List<CommitInfo> GetVisibleCommits()
    {
        bool hasFilters = _hiddenBranchNames.Count > 0 || _soloBranchNames.Count > 0;
        if (!hasFilters || _branchTips.Count == 0)
        {
            return _allCommits;
        }

        var visibleTips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_soloBranchNames.Count > 0)
        {
            foreach (var name in _soloBranchNames)
            {
                if (_branchTips.TryGetValue(name, out var tipSha) && !string.IsNullOrWhiteSpace(tipSha))
                {
                    visibleTips.Add(tipSha);
                }
            }
        }
        else
        {
            foreach (var (name, tipSha) in _branchTips)
            {
                if (_hiddenBranchNames.Contains(name))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(tipSha))
                {
                    visibleTips.Add(tipSha);
                }
            }
        }

        if (visibleTips.Count == 0)
        {
            return hasFilters ? [] : _allCommits;
        }

        var commitsBySha = _allCommits.ToDictionary(c => c.Sha, StringComparer.OrdinalIgnoreCase);
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>(visibleTips);

        while (stack.Count > 0)
        {
            var sha = stack.Pop();
            if (!reachable.Add(sha))
            {
                continue;
            }

            if (!commitsBySha.TryGetValue(sha, out var commit))
            {
                continue;
            }

            foreach (var parent in commit.ParentShas)
            {
                if (!string.IsNullOrWhiteSpace(parent) && commitsBySha.ContainsKey(parent))
                {
                    stack.Push(parent);
                }
            }
        }

        return _allCommits.Where(c => reachable.Contains(c.Sha)).ToList();
    }

    /// <summary>
    /// Update node search state for canvas rendering.
    /// </summary>
    private void UpdateNodeSearchState()
    {
        foreach (var node in Nodes)
        {
            // Find matching commit
            var commit = Commits.FirstOrDefault(c => c.Sha == node.Sha);
            if (commit != null)
            {
                // Node is highlighted if selected or search-highlighted
                node.IsSearchMatch = commit.IsSelected || commit.IsSearchHighlighted;
            }
            else
            {
                node.IsSearchMatch = false;
            }
        }
    }

    /// <summary>
    /// Find and select the first matching commit.
    /// </summary>
    public CommitInfo? FindFirstMatch(string searchText)
    {
        var trimmed = searchText?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
            return null;

        return Commits.FirstOrDefault(c =>
            c.MessageShort.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
            c.Message.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
            c.Sha.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) ||
            c.Author.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Select a commit by its SHA hash.
    /// If the commit is filtered out, clears filters to make it visible.
    /// </summary>
    public void SelectCommitBySha(string sha)
    {
        if (string.IsNullOrEmpty(sha))
            return;

        // First try to find in current visible commits
        var commit = Commits.FirstOrDefault(c => c.Sha == sha || c.Sha.StartsWith(sha));
        if (commit != null)
        {
            SelectCommit(commit);
            return;
        }

        // Not in visible commits - check if it exists in all commits
        var allCommit = _allCommits.FirstOrDefault(c => c.Sha == sha || c.Sha.StartsWith(sha));
        if (allCommit == null)
            return;

        // Commit exists but is filtered out - clear filters to make it visible
        _hiddenBranchNames.Clear();
        _soloBranchNames.Clear();
        RebuildGraphFromFilters();

        // Now find and select the commit in the rebuilt list
        commit = Commits.FirstOrDefault(c => c.Sha == sha || c.Sha.StartsWith(sha));
        if (commit != null)
        {
            SelectCommit(commit);
        }
    }

    public bool TryGetMergeTooltip(string sha, out MergeCommitTooltipViewModel? tooltip)
    {
        if (_mergeTooltipTasks.TryGetValue(sha, out var task) && task.IsCompletedSuccessfully)
        {
            tooltip = task.Result;
            return tooltip != null;
        }

        tooltip = null;
        return false;
    }

    public Task<MergeCommitTooltipViewModel?> GetMergeTooltipAsync(CommitInfo commit)
    {
        if (!commit.IsMerge || string.IsNullOrWhiteSpace(RepositoryPath))
        {
            return Task.FromResult<MergeCommitTooltipViewModel?>(null);
        }

        if (_mergeTooltipTasks.TryGetValue(commit.Sha, out var existing))
        {
            return existing;
        }

        var task = BuildMergeTooltipAsync(commit, RepositoryPath);
        _mergeTooltipTasks[commit.Sha] = task;
        return task;
    }

    private async Task<MergeCommitTooltipViewModel?> BuildMergeTooltipAsync(CommitInfo commit, string repoPath)
    {
        var mergeCommits = await _gitService.GetMergeCommitsAsync(repoPath, commit.Sha);
        if (mergeCommits.Count == 0)
        {
            return null;
        }

        var existingCommits = Commits.ToDictionary(c => c.Sha, StringComparer.OrdinalIgnoreCase);
        foreach (var mergeCommit in mergeCommits)
        {
            if (existingCommits.TryGetValue(mergeCommit.Sha, out var existing))
            {
                mergeCommit.BranchNames = new List<string>(existing.BranchNames);
                mergeCommit.BranchLabels = new List<BranchLabel>(existing.BranchLabels);
                mergeCommit.TagNames = new List<string>(existing.TagNames);
            }
        }

        // Tooltip preview uses a short-lived builder bound to the active
        // GitFlow + remote-name context so colours match the main graph.
        var tooltipGraphBuilder = new GraphBuilder(_activeGitFlowConfig, _activeRemoteNames);
        var visibleCommits = mergeCommits.Take(10).ToList();
        var nodes = tooltipGraphBuilder.BuildGraph(visibleCommits);

        // Pass the tooltip's own builder as the resolver — its colour cache
        // contains exactly the branches in the preview, and it will live for
        // the tooltip's lifetime without racing the main builder.
        return new MergeCommitTooltipViewModel(
            new ObservableCollection<CommitInfo>(mergeCommits),
            new ObservableCollection<GitTreeNode>(nodes),
            tooltipGraphBuilder.MaxLane,
            RowHeight,
            tooltipGraphBuilder);
    }
}
