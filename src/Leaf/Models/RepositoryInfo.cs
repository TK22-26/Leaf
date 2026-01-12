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
    /// Last time this repository was accessed in the app.
    /// </summary>
    public DateTimeOffset LastAccessed { get; set; }

    /// <summary>
    /// ID of the custom group this repo belongs to (if any).
    /// </summary>
    public string? GroupId { get; set; }

    /// <summary>
    /// Current branch name (refreshed on open).
    /// </summary>
    [JsonIgnore]
    [ObservableProperty]
    private string _currentBranch = string.Empty;

    /// <summary>
    /// True if working directory has uncommitted changes.
    /// </summary>
    [JsonIgnore]
    [ObservableProperty]
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
    public bool Exists => Directory.Exists(Path) && Directory.Exists(System.IO.Path.Combine(Path, ".git"));

    /// <summary>
    /// Local branches in this repository.
    /// </summary>
    [JsonIgnore]
    [ObservableProperty]
    private ObservableCollection<BranchInfo> _localBranches = [];

    /// <summary>
    /// Remote branches in this repository.
    /// </summary>
    [JsonIgnore]
    [ObservableProperty]
    private ObservableCollection<BranchInfo> _remoteBranches = [];

    /// <summary>
    /// Whether this repo item is expanded in the tree view.
    /// </summary>
    [JsonIgnore]
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Whether branches have been loaded for this repository.
    /// </summary>
    [JsonIgnore]
    public bool BranchesLoaded { get; set; }

    /// <summary>
    /// Branch categories for tree display (Local/Remote).
    /// </summary>
    [JsonIgnore]
    [ObservableProperty]
    private ObservableCollection<BranchCategory> _branchCategories = [];

    /// <summary>
    /// True if a merge is currently in progress.
    /// </summary>
    [JsonIgnore]
    [ObservableProperty]
    private bool _isMergeInProgress;

    /// <summary>
    /// The branch being merged (from MERGE_HEAD).
    /// </summary>
    [JsonIgnore]
    [ObservableProperty]
    private string _mergingBranch = string.Empty;

    /// <summary>
    /// Number of files with merge conflicts.
    /// </summary>
    [JsonIgnore]
    [ObservableProperty]
    private int _conflictCount;
}
