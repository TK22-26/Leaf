using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Leaf.Models;

/// <summary>
/// Metadata about a tracked Git repository.
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
            catch { /* Ignore read errors */ }
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
    }

    /// <summary>
    /// Whether worktrees have been loaded for this repository.
    /// </summary>
    [JsonIgnore]
    public bool WorktreesLoaded { get; set; }

    /// <summary>
    /// True if a merge is currently in progress.
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isMergeInProgress;

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
}
