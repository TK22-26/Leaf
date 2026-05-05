using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Leaf.Models;

/// <summary>
/// Metadata about a tracked Git repository.
///
/// <para><b>Threading contract</b> (plan §3.7): every mutation of an
/// <c>[ObservableProperty]</c> backing field on this type, and every
/// mutation of the <c>ObservableCollection</c> properties
/// (<see cref="LocalBranches"/>, <see cref="RemoteBranches"/>,
/// <see cref="BranchCategories"/>, <see cref="Worktrees"/>,
/// <see cref="SelectedBranches"/>), must happen on the WPF UI thread.
/// Services and background tasks that compute new values must marshal
/// the assignment through <c>IDispatcherService.InvokeAsync</c> before
/// touching this object, or build a replacement collection off-thread
/// and publish the reference inside a dispatcher call. An audit of all
/// current mutation sites confirms this invariant is upheld — the
/// <c>_dispatcherService.InvokeAsync</c> wraps in <c>MainViewModel</c>'s
/// <c>BranchLoading</c>, <c>Worktree</c>, <c>FileWatcher</c>,
/// <c>Repository</c>, and <c>Sync</c> partials are the canonical pattern.</para>
///
/// <para>Collection mutations in particular are non-negotiable: WPF's
/// <c>CollectionView</c> requires UI-thread access and raises
/// <c>NotSupportedException</c> otherwise.</para>
/// </summary>
public partial class RepositoryInfo : ObservableObject
{
    /// <summary>
    /// Full path to the repository root directory.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Display name (defaults to folder name).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Custom tags assigned by the user for grouping.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Branch names hidden from the graph.
    /// </summary>
    public List<string> HiddenBranchNames { get; set; } = [];

    /// <summary>
    /// Branch names soloed in the graph.
    /// </summary>
    public List<string> SoloBranchNames { get; set; } = [];

    /// <summary>
    /// User-overridden colours for branches in this repository (plan §5.14).
    /// Key is the normalised branch name (remote prefix stripped — see
    /// <see cref="Services.BranchColorService"/>). Value is a hex colour
    /// string (<c>#RRGGBB</c> or <c>#AARRGGBB</c>). Branches without an
    /// entry resolve through the GitFlow / palette layers in
    /// <see cref="Services.IBranchColorService"/>.
    /// </summary>
    public Dictionary<string, string> BranchColorOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Last time this repository was accessed in the app.
    /// </summary>
    public DateTimeOffset LastAccessed { get; set; }

    /// <summary>
    /// True if this repository is pinned to the top list.
    /// </summary>
    [ObservableProperty]
    private bool _isPinned;

    /// <summary>
    /// ID of the custom group this repo belongs to (if any).
    /// </summary>
    public string? GroupId { get; set; }

    /// <summary>
    /// Path of the repository this entry was discovered as a submodule of,
    /// or <c>null</c> when the user added / cloned this repo independently.
    /// Set by <c>OpenSubmoduleAsRepositoryAsync</c> when the user drills
    /// into a submodule from the parent's sidebar; persisted to
    /// <c>repositories.json</c> so the relationship survives across sessions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Drives two pieces of UX:
    /// </para>
    /// <list type="bullet">
    ///   <item>The "← {Parent}" back-button at the top of the branch pane
    ///   (visible only when this is non-null and the parent is still in
    ///   the repository list).</item>
    ///   <item>Sidebar nesting — repos with a non-null parent render
    ///   indented under that parent in whatever group the parent lives in.</item>
    /// </list>
    /// <para>
    /// When the parent is removed from the list, every child whose
    /// <see cref="IsUserAdded"/> is <c>false</c> (i.e. only got into the
    /// list because the user drilled into it) cascades-removes; children
    /// with <see cref="IsUserAdded"/> <c>true</c> have their
    /// <c>ParentRepositoryPath</c> cleared and survive as top-level entries.
    /// </para>
    /// </remarks>
    public string? ParentRepositoryPath { get; set; }

    /// <summary>
    /// Distinguishes "the user explicitly added this entry" (Add Repository,
    /// Clone, drag-and-drop) from "this entry was auto-discovered when the
    /// user drilled into a submodule from a parent." Drives the cascade
    /// behaviour when a parent is removed: auto-discovered children
    /// disappear with their parent; user-added children get promoted to
    /// the top level (<see cref="ParentRepositoryPath"/> cleared) and stay.
    /// </summary>
    /// <remarks>
    /// Default <c>true</c> so that legacy entries (saved before this
    /// property existed) are treated as user-affirmed — safest assumption
    /// for the migration. New entries created by the submodule-open path
    /// must explicitly flip it to <c>false</c>.
    /// </remarks>
    public bool IsUserAdded { get; set; } = true;

    /// <summary>
    /// Repositories that point at this one via <see cref="ParentRepositoryPath"/> —
    /// i.e. submodules of this repo that the user has at some point
    /// opened-as-repo. Maintained by <c>RepositoryManagementService</c>;
    /// callers should not mutate this directly. Surfaced in the sidebar
    /// as nested children under their parent (single source of truth —
    /// child repos do NOT also appear flat in folder groups).
    /// </summary>
    [JsonIgnore]
    public ObservableCollection<RepositoryInfo> ChildRepositories { get; } = [];

    /// <summary>
    /// Combined sidebar children — worktrees (when there are multiple)
    /// followed by submodule child repositories. Bound by the
    /// <c>HierarchicalDataTemplate</c> for <see cref="RepositoryInfo"/>
    /// so the tree expands to show both kinds of descendant in one
    /// place. Returns null when there's nothing to show, which keeps
    /// the parent row non-expandable in WPF's TreeView (matches the
    /// pre-nesting behaviour for repos with neither extra worktrees
    /// nor child repos).
    /// </summary>
    /// <remarks>
    /// Notified through <see cref="OnChildRepositoriesChanged"/> when
    /// child repos or extra worktrees come and go, so the tree
    /// re-renders without forcing a full sidebar rebuild.
    /// </remarks>
    [JsonIgnore]
    public IEnumerable<object>? TreeViewChildren
    {
        get
        {
            EnsureChildRepositoriesSubscribed();
            EnsureWorktreesSubscribed();
            var hasWorktrees = Worktrees.Count > 1;
            var hasChildRepos = ChildRepositories.Count > 0;
            if (!hasWorktrees && !hasChildRepos) return null;

            // Materialise once per call. Cheap (these collections stay
            // small) and keeps the binding stable — yield-return would
            // produce a fresh enumerator each access and confuse
            // TreeViewItem expansion state.
            var items = new List<object>(
                (hasWorktrees ? Worktrees.Count : 0) + ChildRepositories.Count);
            if (hasWorktrees)
            {
                foreach (var wt in Worktrees) items.Add(wt);
            }
            foreach (var child in ChildRepositories) items.Add(child);
            return items;
        }
    }

    private bool _childRepositoriesSubscribed;

    private void EnsureChildRepositoriesSubscribed()
    {
        if (_childRepositoriesSubscribed) return;
        ChildRepositories.CollectionChanged += OnChildRepositoriesChanged;
        _childRepositoriesSubscribed = true;
    }

    private void OnChildRepositoriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(TreeViewChildren));
    }

    /// <summary>
    /// Current branch name (refreshed on open).
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private string _currentBranch = string.Empty;

    /// <summary>
    /// True if working directory has uncommitted changes.
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isDirty;

    /// <summary>
    /// Number of commits ahead of tracking branch.
    /// </summary>
    [JsonIgnore]
    public int AheadBy { get; set; }

    /// <summary>
    /// Number of commits behind tracking branch.
    /// </summary>
    [JsonIgnore]
    public int BehindBy { get; set; }

    /// <summary>
    /// Auto-detected folder group name based on parent directory.
    /// </summary>
    [JsonIgnore]
    public string FolderGroup => System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Path) ?? string.Empty);

    /// <summary>
    /// True if the repository exists on disk.
    /// </summary>
    [JsonIgnore]
    public bool Exists
    {
        get
        {
            if (!Directory.Exists(Path))
                return false;

            var gitPath = System.IO.Path.Combine(Path, ".git");
            return Directory.Exists(gitPath) || File.Exists(gitPath);
        }
    }

    /// <summary>
    /// True if this repository path is a secondary worktree (has .git FILE, not directory).
    /// </summary>
    [JsonIgnore]
    public bool IsSecondaryWorktree
    {
        get
        {
            var gitPath = System.IO.Path.Combine(Path, ".git");
            return File.Exists(gitPath) && !Directory.Exists(gitPath);
        }
    }

    /// <summary>
    /// Gets the main worktree path if this is a secondary worktree.
    /// Returns null if this is the main worktree or a regular repo.
    /// </summary>
    [JsonIgnore]
    public string? MainWorktreePath
    {
        get
        {
            if (!IsSecondaryWorktree) return null;

            var gitFilePath = System.IO.Path.Combine(Path, ".git");
            try
            {
                // .git file contains: gitdir: /path/to/main/.git/worktrees/name
                var content = File.ReadAllText(gitFilePath).Trim();
                if (content.StartsWith("gitdir: "))
                {
                    var gitDir = content["gitdir: ".Length..].Trim();
                    // Navigate from .git/worktrees/name to the main repo
                    // The main .git directory is parent of "worktrees" folder
                    var worktreesDir = System.IO.Path.GetDirectoryName(gitDir);
                    if (worktreesDir != null && System.IO.Path.GetFileName(worktreesDir) == "worktrees")
                    {
                        var mainGitDir = System.IO.Path.GetDirectoryName(worktreesDir);
                        if (mainGitDir != null)
                        {
                            // Main repo is parent of .git directory
                            return System.IO.Path.GetDirectoryName(mainGitDir);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException
                                    or UnauthorizedAccessException
                                    or ArgumentException
                                    or NotSupportedException)
            {
                // .git pointer unreadable or malformed — treat as a detached
                // worktree with no discoverable main repo.
                Leaf.Services.Log.Info("RepoInfo", $"MainWorktreePath read failed: {ex.GetType().Name}: {ex.Message}");
            }
            return null;
        }
    }

    /// <summary>
    /// Local branches in this repository.
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private ObservableCollection<BranchInfo> _localBranches = [];

    /// <summary>
    /// Remote branches in this repository.
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private ObservableCollection<BranchInfo> _remoteBranches = [];

    /// <summary>
    /// Whether this repo item is expanded in the tree view.
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isExpanded;

    /// <summary>
    /// Whether branches have been loaded for this repository.
    /// </summary>
    [JsonIgnore]
    public bool BranchesLoaded { get; set; }

    /// <summary>
    /// Branch categories for tree display (Local/Remote).
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private ObservableCollection<BranchCategory> _branchCategories = [];

    /// <summary>
    /// Worktrees associated with this repository.
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private ObservableCollection<WorktreeInfo> _worktrees = [];

    /// <summary>
    /// Tracks whether we've subscribed to the Worktrees CollectionChanged event.
    /// </summary>
    private bool _worktreesSubscribed;

    /// <summary>
    /// Returns worktrees only if there are multiple (for tree view binding).
    /// Returns null if there's only one worktree (the main one) to avoid unnecessary hierarchy.
    /// </summary>
    [JsonIgnore]
    public ObservableCollection<WorktreeInfo>? WorktreesIfMultiple
    {
        get
        {
            // Lazy subscribe to CollectionChanged (needed because field initializer bypasses setter)
            EnsureWorktreesSubscribed();
            return Worktrees.Count > 1 ? Worktrees : null;
        }
    }

    /// <summary>
    /// Ensures the Worktrees collection has its CollectionChanged event subscribed.
    /// </summary>
    private void EnsureWorktreesSubscribed()
    {
        if (!_worktreesSubscribed && Worktrees != null)
        {
            Worktrees.CollectionChanged += Worktrees_CollectionChanged;
            _worktreesSubscribed = true;
        }
    }

    /// <summary>
    /// Called when Worktrees collection is replaced.
    /// </summary>
    partial void OnWorktreesChanged(ObservableCollection<WorktreeInfo>? oldValue, ObservableCollection<WorktreeInfo> newValue)
    {
        if (oldValue != null)
        {
            oldValue.CollectionChanged -= Worktrees_CollectionChanged;
        }
        _worktreesSubscribed = false;
        if (newValue != null)
        {
            newValue.CollectionChanged += Worktrees_CollectionChanged;
            _worktreesSubscribed = true;
        }
        OnPropertyChanged(nameof(WorktreesIfMultiple));
    }

    private void Worktrees_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(WorktreesIfMultiple));
        OnPropertyChanged(nameof(TreeViewChildren));
    }

    /// <summary>
    /// Whether worktrees have been loaded for this repository.
    /// </summary>
    [JsonIgnore]
    public bool WorktreesLoaded { get; set; }

    /// <summary>
    /// Whether pull requests have been loaded for this repository.
    /// </summary>
    [JsonIgnore]
    public bool PullRequestsLoaded { get; set; }

    /// <summary>
    /// Whether the pull request category should show all pull requests or only open ones.
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _showAllPullRequests;

    /// <summary>
    /// Label for the pull request filter toggle.
    /// </summary>
    [JsonIgnore]
    public string PullRequestFilterLabel => ShowAllPullRequests ? "All" : "Open";

    /// <summary>
    /// Tooltip for the pull request filter toggle.
    /// </summary>
    [JsonIgnore]
    public string PullRequestFilterToolTip => ShowAllPullRequests
        ? "Showing all pull requests"
        : "Showing open pull requests only";

    /// <summary>
    /// Currently selected pull request in the tree view.
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private PullRequestInfo? _selectedPullRequest;

    /// <summary>
    /// True if a merge, cherry-pick, revert, or rebase is currently in progress.
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isMergeInProgress;

    /// <summary>
    /// The type of git operation in progress (Merge, CherryPick, Revert, Rebase, or None).
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private GitOperationType _operationType;

    /// <summary>
    /// The branch being merged (from MERGE_HEAD).
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private string _mergingBranch = string.Empty;

    /// <summary>
    /// Number of files with merge conflicts.
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private int _conflictCount;

    /// <summary>
    /// True if HEAD is detached (not on a branch).
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isDetachedHead;

    /// <summary>
    /// SHA of the detached HEAD commit (null if on a branch).
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private string? _detachedHeadSha;

    /// <summary>
    /// Currently selected branches in the tree view (supports multi-selection).
    /// </summary>
    [JsonIgnore]
    public ObservableCollection<BranchInfo> SelectedBranches { get; } = [];

    /// <summary>
    /// Clears all branch selections.
    /// </summary>
    public void ClearBranchSelection()
    {
        foreach (var branch in SelectedBranches.ToList())
        {
            branch.IsSelected = false;
        }
        SelectedBranches.Clear();
    }

    /// <summary>
    /// Clears the pull request selection.
    /// </summary>
    public void ClearPullRequestSelection()
    {
        if (SelectedPullRequest != null)
        {
            SelectedPullRequest.IsSelected = false;
            SelectedPullRequest = null;
        }

        foreach (var category in BranchCategories)
        {
            if (category.IsPullRequestsCategory)
            {
                foreach (var pr in category.PullRequests)
                    pr.IsSelected = false;
            }
        }
    }

    /// <summary>
    /// Clears the submodule selection. Mirrors the other clear-* methods
    /// so the cross-class selection model stays consistent: any class
    /// that gets selected first clears every other class.
    /// </summary>
    public void ClearSubmoduleSelection()
    {
        foreach (var category in BranchCategories)
        {
            if (category.IsSubmodulesCategory)
            {
                foreach (var sm in category.Submodules)
                    sm.IsSelected = false;
            }
        }
    }

    partial void OnShowAllPullRequestsChanged(bool value)
    {
        OnPropertyChanged(nameof(PullRequestFilterLabel));
        OnPropertyChanged(nameof(PullRequestFilterToolTip));
    }
}
