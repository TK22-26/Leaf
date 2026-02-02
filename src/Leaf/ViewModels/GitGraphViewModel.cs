using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Graph;
using Leaf.Models;
using Leaf.Services;

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
    private readonly GraphBuilder _graphBuilder = new();
    private readonly Dictionary<string, Task<MergeCommitTooltipViewModel?>> _mergeTooltipTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _branchTips = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hiddenBranchNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _soloBranchNames = new(StringComparer.OrdinalIgnoreCase);
    private List<CommitInfo> _allCommits = [];
    private string? _currentBranchName;
    private GitFlowConfig? _gitFlowConfig;
    private IReadOnlyCollection<string> _remoteNames = Array.Empty<string>();

    // Lazy loading state
    private int _loadedCommitCount;
    private bool _hasMoreCommits = true;
    private HashSet<string> _loadedCommitShas = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _graphBuildCts;
    private const int BatchSize = 1000;  // Large batch to minimize O(n) skip cost
    private const int InitialBatchSize = 500;

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
    private bool _isLoading;

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
    }

    public void SetGitFlowContext(GitFlowConfig? config, IReadOnlyCollection<string> remoteNames)
    {
        _gitFlowConfig = config;
        _remoteNames = remoteNames;
        GraphBuilder.SetGitFlowContext(config, remoteNames);
        RebuildGraphFromFilters();
    }

    /// <summary>
    /// Load commits for a repository.
    /// </summary>
    [RelayCommand]
    public async Task LoadRepositoryAsync(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

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

            _currentBranchName = workingChanges?.BranchName;
            if (string.IsNullOrWhiteSpace(_currentBranchName))
            {
                _currentBranchName = commits
                    .SelectMany(c => c.BranchLabels)
                    .FirstOrDefault(l => l.IsCurrent)?.Name;
            }

            _allCommits = commits.ToList();
            WorkingChanges = workingChanges;
            Stashes = new ObservableCollection<StashInfo>(stashes);

            // Initialize lazy loading state
            _loadedCommitCount = commits.Count;
            _loadedCommitShas = new HashSet<string>(commits.Select(c => c.Sha), StringComparer.OrdinalIgnoreCase);
            _hasMoreCommits = commits.Count == InitialBatchSize;
            IsLoadingMore = false;

            RebuildGraphFromFilters();

            // Preserve selection if it was selected, otherwise clear
            // This prevents losing selection when file watcher triggers reload during staging
            bool wasWorkingChangesSelected = IsWorkingChangesSelected;
            var wasSelectedStashIndex = SelectedStash?.Index;

            SelectedCommit = null;
            SelectedStash = wasSelectedStashIndex.HasValue && wasSelectedStashIndex.Value < stashes.Count
                ? stashes[wasSelectedStashIndex.Value]
                : null;
            SelectedSha = wasWorkingChangesSelected ? WorkingChangesSha : null;
            IsWorkingChangesSelected = wasWorkingChangesSelected && HasWorkingChanges;

            ApplySearchFilter(SearchText);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load repository: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
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

            // Recalculate total height
            int rowCount = Commits.Count;
            if (HasWorkingChanges)
            {
                rowCount += 1;
            }
            rowCount += Stashes.Count; // Include stash rows
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

        // Cancel any pending graph build
        _graphBuildCts?.Cancel();
        _graphBuildCts = new CancellationTokenSource();
        var ct = _graphBuildCts.Token;

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

            // Capture state for background work
            var visibleCommits = GetVisibleCommits();
            var currentBranch = _currentBranchName;

            // Build graph on background thread with new GraphBuilder instance
            // (avoids race on instance state like MaxLane)
            var (nodes, maxLane) = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var tempBuilder = new GraphBuilder();
                var graphNodes = tempBuilder.BuildGraph(visibleCommits, currentBranch);
                return (graphNodes, tempBuilder.MaxLane);
            }, ct);

            ct.ThrowIfCancellationRequested();

            // Fast UI update (pointer swaps only)
            Nodes = new ObservableCollection<GitTreeNode>(nodes);
            Commits = new ObservableCollection<CommitInfo>(visibleCommits);
            MaxLane = maxLane;

            // Recalculate height
            int rowCount = Commits.Count + (HasWorkingChanges ? 1 : 0) + Stashes.Count;
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
        // Clear working changes and stash selection when selecting a commit
        IsWorkingChangesSelected = false;
        if (SelectedStash != null)
        {
            SelectedStash.IsSelected = false;
            SelectedStash = null;
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
        CommitInfo? shaMatch = null;

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

                // Track first SHA match for auto-scroll
                if (shaMatch == null && commit.Sha.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    shaMatch = commit;
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

        // Auto-select SHA matches to trigger scroll
        if (shaMatch != null && IsLikelyShaSearch(trimmed))
        {
            SelectCommit(shaMatch);
        }
    }

    /// <summary>
    /// Check if search text looks like a SHA (hex-only, 4+ chars).
    /// </summary>
    private static bool IsLikelyShaSearch(string text)
    {
        // SHA searches are hex-only and typically 4-40 chars
        return text.Length >= 4 &&
               text.Length <= 40 &&
               text.All(c => char.IsAsciiHexDigit(c));
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
            TotalHeight = ((HasWorkingChanges ? 1 : 0) + Stashes.Count) * RowHeight;
            return;
        }

        var visibleCommits = GetVisibleCommits();

        var nodes = _graphBuilder.BuildGraph(visibleCommits, _currentBranchName);
        Nodes = new ObservableCollection<GitTreeNode>(nodes);
        Commits = new ObservableCollection<CommitInfo>(visibleCommits);
        MaxLane = _graphBuilder.MaxLane;

        // Calculate total height including working changes and stash rows
        int rowCount = Commits.Count;
        if (HasWorkingChanges)
        {
            rowCount += 1; // Add one row for working changes
        }
        rowCount += Stashes.Count; // Add row for each stash
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
    /// </summary>
    public void SelectCommitBySha(string sha)
    {
        if (string.IsNullOrEmpty(sha))
            return;

        var commit = Commits.FirstOrDefault(c => c.Sha == sha || c.Sha.StartsWith(sha));
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

        var tooltipGraphBuilder = new GraphBuilder();
        var visibleCommits = mergeCommits.Take(10).ToList();
        var nodes = tooltipGraphBuilder.BuildGraph(visibleCommits);

        return new MergeCommitTooltipViewModel(
            new ObservableCollection<CommitInfo>(mergeCommits),
            new ObservableCollection<GitTreeNode>(nodes),
            tooltipGraphBuilder.MaxLane,
            RowHeight);
    }
}
