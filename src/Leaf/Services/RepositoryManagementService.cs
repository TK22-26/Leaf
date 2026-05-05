using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for managing repositories - CRUD operations, persistence, and quick access.
/// </summary>
public class RepositoryManagementService : IRepositoryManagementService
{
    private readonly SettingsService _settingsService;
    private readonly RepositorySection _pinnedSection = new() { Name = "PINNED" };
    private readonly RepositorySection _recentSection = new() { Name = "MOST RECENT" };

    public ObservableCollection<RepositoryGroup> RepositoryGroups { get; } = [];
    public ObservableCollection<RepositoryInfo> PinnedRepositories { get; } = [];
    public ObservableCollection<RepositoryInfo> RecentRepositories { get; } = [];
    public ObservableCollection<object> RepositoryRootItems { get; } = [];

    public event EventHandler<RepositoryInfo>? RepositoryAdded;
    public event EventHandler<RepositoryInfo>? RepositoryRemoved;

    public RepositoryManagementService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public Task<string?> LoadRepositoriesAsync()
    {
        var sw = Log.StartTimer();
        var data = _settingsService.LoadRepositories();
        var needsSave = false;

        // Order matters now that AddRepositoryToGroups nests children
        // under their parent: if a child arrives before its parent
        // is in the list, FindRepository(parentPath) returns null and
        // the child falls through to a folder-group entry instead of
        // getting nested. Sort by parent-chain depth so every parent
        // is added before any of its descendants. Repos without a
        // parent (the common case) sort to depth 0 first.
        var ordered = data.Repositories
            .OrderBy(r => CountParentDepth(r.ParentRepositoryPath, data.Repositories))
            .ToList();

        foreach (var repo in ordered)
        {
            // Only add if the repo still exists on disk
            if (!repo.Exists)
                continue;

            // Skip secondary worktrees - add their main worktree instead
            if (repo.IsSecondaryWorktree && repo.MainWorktreePath != null)
            {
                var mainPath = repo.MainWorktreePath;

                // Check if main worktree already exists in loaded repos
                if (!ContainsRepository(mainPath))
                {
                    var mainRepo = new RepositoryInfo
                    {
                        Path = mainPath,
                        Name = Path.GetFileName(mainPath.TrimEnd(
                            Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar)),
                        // Preserve settings from the secondary worktree entry
                        IsPinned = repo.IsPinned,
                        LastAccessed = repo.LastAccessed,
                        Tags = repo.Tags,
                        GroupId = repo.GroupId
                    };
                    AddRepositoryToGroups(mainRepo, save: false, raiseEvent: false);
                }
                needsSave = true; // Mark that we need to clean up saved data
                continue;
            }

            AddRepositoryToGroups(repo, save: false, raiseEvent: false);
        }

        // Also load custom groups
        foreach (var group in data.CustomGroups)
        {
            if (!RepositoryGroups.Any(g => g.Id == group.Id))
            {
                RepositoryGroups.Add(group);
            }
        }

        SortAllRepositories();
        RefreshQuickAccess();

        // If we migrated any secondary worktrees, save the cleaned-up repo list
        if (needsSave)
        {
            SaveRepositories();
        }

        Log.Perf("RepoMgmt", $"Loaded {RepositoryGroups.SelectMany(g => g.Repositories).Count()} repositories", sw.ElapsedMilliseconds);

        // Return the last selected repository path
        var settings = _settingsService.LoadSettings();
        return Task.FromResult(settings.LastSelectedRepositoryPath);
    }

    public void SaveRepositories()
    {
        // Walk AllRepositories so child repos nested under a parent
        // (Phase E) get persisted — they don't live in any folder
        // group's Repositories collection any more.
        var allRepos = AllRepositories()
            .DistinctBy(r => r.Path)
            .ToList();

        var minimalRepos = allRepos
            .Select(CreateRepoSnapshot)
            .ToList();

        var customGroups = RepositoryGroups
            .Where(g => g.Type == GroupType.Custom)
            .Select(CreateGroupSnapshot)
            .ToList();

        var data = new RepositoryData
        {
            Repositories = minimalRepos,
            CustomGroups = customGroups
        };

        _settingsService.SaveRepositories(data);
    }

    public void AddRepository(RepositoryInfo repo, bool save = true)
    {
        // If this is a secondary worktree, add the main worktree instead
        if (repo.IsSecondaryWorktree && repo.MainWorktreePath != null)
        {
            var mainPath = repo.MainWorktreePath;

            // Check if main worktree already exists
            if (!ContainsRepository(mainPath))
            {
                // Create RepositoryInfo for the main worktree
                var mainRepo = new RepositoryInfo
                {
                    Path = mainPath,
                    Name = System.IO.Path.GetFileName(mainPath.TrimEnd(
                        System.IO.Path.DirectorySeparatorChar,
                        System.IO.Path.AltDirectorySeparatorChar))
                };
                AddRepositoryToGroups(mainRepo, save, raiseEvent: true);
            }

            // Don't add the secondary worktree as a separate entry
            return;
        }

        // Promote existing auto-added entry when the user explicitly
        // re-adds the same path. AddRepository's contract is "the user
        // wants this in the list" — passing IsUserAdded=true on the
        // incoming repo signals user intent. The submodule-open path
        // bypasses this by setting IsUserAdded=false on its incoming
        // RepositoryInfo, which preserves the auto-added flag on existing
        // entries.
        var existing = FindRepository(repo.Path);
        if (existing != null && repo.IsUserAdded && !existing.IsUserAdded)
        {
            existing.IsUserAdded = true;
            if (save) SaveRepositories();
            return;
        }

        AddRepositoryToGroups(repo, save, raiseEvent: true);
    }

    /// <summary>
    /// Resolves the actual repository path to add (handles secondary worktrees).
    /// </summary>
    public string ResolveRepositoryPath(string path)
    {
        var gitFilePath = System.IO.Path.Combine(path, ".git");
        if (File.Exists(gitFilePath) && !Directory.Exists(gitFilePath))
        {
            // This is a secondary worktree, find main
            try
            {
                var content = File.ReadAllText(gitFilePath).Trim();
                if (content.StartsWith("gitdir: "))
                {
                    var gitDir = content["gitdir: ".Length..].Trim();
                    var worktreesDir = System.IO.Path.GetDirectoryName(gitDir);
                    if (worktreesDir != null && System.IO.Path.GetFileName(worktreesDir) == "worktrees")
                    {
                        var mainGitDir = System.IO.Path.GetDirectoryName(worktreesDir);
                        if (mainGitDir != null)
                        {
                            return System.IO.Path.GetDirectoryName(mainGitDir) ?? path;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("RepoMgmt", $"Failed to resolve worktree path for '{path}': {ex.Message}");
            }
        }
        return path;
    }

    public void RemoveRepository(RepositoryInfo repo)
    {
        Log.Info("RepoMgmt", $"Repository removed: {repo.Name} ({repo.Path})");

        // Cascade rule: handle every entry whose ParentRepositoryPath
        // points at the one being removed.
        //   • IsUserAdded=false → recursive cascade (it only existed
        //     because the user drilled into the parent; it goes too).
        //   • IsUserAdded=true  → promote to top-level (clear
        //     ParentRepositoryPath; the user explicitly added this
        //     entry at some point and we keep their work).
        // We snapshot the children up-front because cascading removes
        // mutate RepositoryGroups underneath us. Walk AllRepositories
        // so we catch children that live inside the parent's
        // ChildRepositories collection (Phase E nested rendering) as
        // well as anything still flat at the folder-group level
        // (e.g. legacy entries pre-Phase-E whose ParentRepositoryPath
        // points at the soon-to-be-removed repo).
        var children = AllRepositories()
            .Where(r => !string.IsNullOrEmpty(r.ParentRepositoryPath)
                     && string.Equals(r.ParentRepositoryPath, repo.Path, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var child in children)
        {
            if (child.IsUserAdded)
            {
                // Promote: clear the parent link, then re-home the
                // entry in a folder group. Without the re-add the
                // child is stuck inside the about-to-be-removed
                // parent's ChildRepositories collection — it would
                // vanish along with the parent because nothing in
                // RemoveRepositoryFromGroups iterates the soon-to-be-
                // detached parent's children. Re-add via the normal
                // path: AddRepositoryToGroups now sees no parent path
                // and routes to the folder group.
                child.ParentRepositoryPath = null;
                if (FindRepository(child.Path) != null && children.Contains(child))
                {
                    // Detach from the old parent's collection first so
                    // AddRepositoryToGroups doesn't trip over the
                    // dedup check (the child is technically already in
                    // the list via the parent's ChildRepositories).
                    repo.ChildRepositories.Remove(child);
                }
                AddRepositoryToGroups(child, save: false, raiseEvent: false);
                Log.Info("RepoMgmt", $"Repository promoted to top-level (parent removed): {child.Name} ({child.Path})");
            }
            else
            {
                // Cascade: recurse, which will handle this child's own
                // descendants the same way. Option A from the design —
                // we do NOT re-parent this child's user-added grandchildren
                // up to the deleted repo's parent; we rebuild relationships
                // from scratch each level down.
                RemoveRepository(child);
            }
        }

        RemoveRepositoryFromGroups(repo);
        SaveRepositories();
        RepositoryRemoved?.Invoke(this, repo);
    }

    public void TogglePinRepository(RepositoryInfo repo)
    {
        repo.IsPinned = !repo.IsPinned;
        SaveRepositories();
        RefreshQuickAccess();
    }

    public void MarkAsRecentlyAccessed(RepositoryInfo repo)
    {
        repo.LastAccessed = DateTimeOffset.Now;
        SaveRepositories();
        UpdateRecentSection();
    }

    public bool ContainsRepository(string path)
    {
        var normalizedPath = NormalizePath(path);
        return AllRepositories()
            .Any(r => NormalizePath(r.Path).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Walks every repository in the list, regardless of where it
    /// lives in the tree (folder groups, child-of-parent nesting,
    /// future kinds). Used by lookup methods so a child repo
    /// nested under its parent is still discoverable by path.
    /// </summary>
    private IEnumerable<RepositoryInfo> AllRepositories()
    {
        foreach (var group in RepositoryGroups)
        {
            foreach (var repo in group.Repositories)
            {
                yield return repo;
                foreach (var child in repo.ChildRepositories)
                {
                    yield return child;
                    // Recursive nesting: a child can have its own
                    // children. Two-level walk is enough for the
                    // current tree depth, but recurse defensively
                    // so a deeper chain still gets found.
                    foreach (var grandchild in EnumerateDescendants(child))
                    {
                        yield return grandchild;
                    }
                }
            }
        }
    }

    private static IEnumerable<RepositoryInfo> EnumerateDescendants(RepositoryInfo repo)
    {
        foreach (var child in repo.ChildRepositories)
        {
            yield return child;
            foreach (var descendant in EnumerateDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// How many parent-chain hops separate a repo from a top-level
    /// entry. Top-level (no parent path) is depth 0. Used by
    /// <see cref="LoadRepositoriesAsync"/> to ensure parents are
    /// added to the tree before their descendants.
    /// </summary>
    /// <remarks>
    /// Linear scan over <paramref name="all"/> per hop; cheap because
    /// repository lists stay small. Defends against a hand-edited
    /// repositories.json with a parent-loop by capping at the list
    /// length — a cycle would otherwise spin forever.
    /// </remarks>
    private static int CountParentDepth(string? parentPath, List<RepositoryInfo> all)
    {
        var depth = 0;
        var current = parentPath;
        var max = all.Count;
        while (!string.IsNullOrEmpty(current) && depth <= max)
        {
            var parent = all.FirstOrDefault(r => string.Equals(r.Path, current, StringComparison.OrdinalIgnoreCase));
            if (parent == null) break;
            depth++;
            current = parent.ParentRepositoryPath;
        }
        return depth;
    }

    /// <summary>
    /// Normalizes a path for comparison (removes trailing slashes, converts to full path).
    /// </summary>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        // Get full path to resolve any relative components and normalize slashes
        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            Log.Warn("RepoMgmt", $"Path normalization failed for '{path}': {ex.Message}");
        }

        // Remove trailing directory separators
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public RepositoryInfo? FindRepository(string path)
    {
        // Use the same normalization that AddRepositoryToGroups uses
        // for its dedup check — otherwise they can disagree on trailing
        // separators / mixed slashes / relative segments, leading to
        // FindRepository "not in list" + AddRepository "already in list,
        // skipping" and the caller silently losing whatever mutation it
        // intended to apply.
        //
        // Walks AllRepositories so child repos nested under a parent
        // (Phase E sidebar nesting) are still findable by path.
        var normalized = NormalizePath(path);
        return AllRepositories()
            .FirstOrDefault(r => NormalizePath(r.Path).Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public void RefreshQuickAccess()
    {
        // Pinning is independent of nesting (Q3 in the design
        // discussion: "show in both places"), so the pinned list
        // walks AllRepositories — a child repo nested under its
        // parent can still be pinned and surface at the top.
        // MOST RECENT also walks AllRepositories so children are
        // reachable as quick-access shortcuts when recently used.
        var allRepos = AllRepositories()
            .DistinctBy(r => r.Path)
            .ToList();

        // Update pinned repositories
        PinnedRepositories.Clear();
        foreach (var repo in allRepos.Where(r => r.IsPinned))
        {
            PinnedRepositories.Add(repo);
        }

        // Update recent repositories
        RecentRepositories.Clear();
        foreach (var repo in allRepos
                     .OrderByDescending(r => r.LastAccessed)
                     .Take(5))
        {
            RecentRepositories.Add(repo);
        }

        // Update pinned section with wrapper items
        _pinnedSection.Items.Clear();
        foreach (var repo in PinnedRepositories)
        {
            _pinnedSection.Items.Add(new QuickAccessItem(repo));
        }

        // Update recent section with wrapper items
        _recentSection.Items.Clear();
        foreach (var repo in RecentRepositories)
        {
            _recentSection.Items.Add(new QuickAccessItem(repo));
        }

        // Rebuild root items
        RebuildRootItems();
    }

    /// <summary>
    /// Updates only the MOST RECENT section items without rebuilding RepositoryRootItems.
    /// This preserves TreeView selection on folder groups while keeping the recent list current.
    /// </summary>
    private void UpdateRecentSection()
    {
        var allRepos = RepositoryGroups
            .SelectMany(g => g.Repositories)
            .DistinctBy(r => r.Path)
            .ToList();

        var newRecent = allRepos
            .OrderByDescending(r => r.LastAccessed)
            .Take(5)
            .ToList();

        // Update RecentRepositories backing collection
        RecentRepositories.Clear();
        foreach (var repo in newRecent)
            RecentRepositories.Add(repo);

        // Update _recentSection.Items - only this section refreshes in the TreeView,
        // folder groups and their selection remain untouched
        _recentSection.Items.Clear();
        foreach (var repo in newRecent)
            _recentSection.Items.Add(new QuickAccessItem(repo));

        // Ensure MOST RECENT section is visible in RepositoryRootItems if it has items
        if (_recentSection.Items.Count > 0 && !RepositoryRootItems.Contains(_recentSection))
        {
            var insertIndex = RepositoryRootItems.Contains(_pinnedSection)
                ? RepositoryRootItems.IndexOf(_pinnedSection) + 1
                : 0;
            RepositoryRootItems.Insert(insertIndex, _recentSection);
        }
        else if (_recentSection.Items.Count == 0 && RepositoryRootItems.Contains(_recentSection))
        {
            RepositoryRootItems.Remove(_recentSection);
        }
    }

    private void AddRepositoryToGroups(RepositoryInfo repo, bool save, bool raiseEvent)
    {
        // Submodule child? Nest under the parent — single source of
        // truth, no duplicate folder-group entry. (User explicitly
        // chose "only nested" in the design discussion.) When the
        // parent is missing from the list (rare, defensive), we fall
        // through to normal folder-group handling so the entry stays
        // visible somewhere.
        if (!string.IsNullOrEmpty(repo.ParentRepositoryPath))
        {
            var parent = FindRepository(repo.ParentRepositoryPath);
            if (parent != null)
            {
                // If the path is already represented somewhere in the
                // tree — flat in a folder group OR already nested
                // under this same parent — don't add a duplicate.
                // The submodule-open path's "if exists, back-fill
                // ParentRepositoryPath" logic in MainViewModel.Submodule
                // already handles surfacing the relationship; we just
                // need to not double-register.
                var existing = FindRepository(repo.Path);
                if (existing == null)
                {
                    parent.ChildRepositories.Add(repo);
                    if (save) SaveRepositories();
                    if (raiseEvent)
                    {
                        Log.Info("RepoMgmt", $"Repository nested under parent: {repo.Name} (under {parent.Name})");
                        RepositoryAdded?.Invoke(this, repo);
                    }
                }
                RefreshQuickAccess();
                return;
            }
        }

        // Find or create folder-based group
        var folderGroup = RepositoryGroups.FirstOrDefault(g =>
            g.Type == GroupType.Folder && g.Name == repo.FolderGroup);

        if (folderGroup == null)
        {
            folderGroup = new RepositoryGroup
            {
                Name = repo.FolderGroup,
                Type = GroupType.Folder
            };
            RepositoryGroups.Add(folderGroup);
        }

        // Add repo if not already present (use normalized paths for comparison)
        var normalizedRepoPath = NormalizePath(repo.Path);
        if (!folderGroup.Repositories.Any(r => NormalizePath(r.Path).Equals(normalizedRepoPath, StringComparison.OrdinalIgnoreCase)))
        {
            folderGroup.Repositories.Add(repo);
            SortRepositories(folderGroup);

            if (save)
            {
                SaveRepositories();
            }

            if (raiseEvent)
            {
                Log.Info("RepoMgmt", $"Repository added: {repo.Name} ({repo.Path})");
                RepositoryAdded?.Invoke(this, repo);
            }
        }

        RefreshQuickAccess();
    }

    private void RemoveRepositoryFromGroups(RepositoryInfo repo)
    {
        var emptyGroups = new List<RepositoryGroup>();

        foreach (var group in RepositoryGroups)
        {
            var existing = group.Repositories.FirstOrDefault(r => r.Path == repo.Path);
            if (existing != null)
            {
                group.Repositories.Remove(existing);
            }

            if (group.Repositories.Count == 0)
            {
                emptyGroups.Add(group);
            }
        }

        foreach (var group in emptyGroups)
        {
            RepositoryGroups.Remove(group);
        }

        // Also detach from a parent's ChildRepositories collection if
        // this repo lived nested under one. Walk every potential parent
        // (cheap — repository lists are small) so we don't have to
        // resolve ParentRepositoryPath for an entry that may have had
        // it cleared during a promotion.
        foreach (var group in RepositoryGroups)
        {
            foreach (var maybeParent in group.Repositories)
            {
                if (maybeParent.ChildRepositories.Count == 0) continue;
                var existing = maybeParent.ChildRepositories.FirstOrDefault(c => c.Path == repo.Path);
                if (existing != null)
                {
                    maybeParent.ChildRepositories.Remove(existing);
                }
            }
        }

        SortAllRepositories();
        RefreshQuickAccess();
        SaveRepositories();
    }

    private void RebuildRootItems()
    {
        var desired = new List<object>();

        if (_pinnedSection.Items.Count > 0)
        {
            desired.Add(_pinnedSection);
        }

        if (_recentSection.Items.Count > 0)
        {
            desired.Add(_recentSection);
        }

        foreach (var group in RepositoryGroups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            desired.Add(group);
        }

        RepositoryRootItems.Clear();
        foreach (var item in desired)
        {
            RepositoryRootItems.Add(item);
        }
    }

    private void SortAllRepositories()
    {
        foreach (var group in RepositoryGroups)
        {
            SortRepositories(group);
        }
    }

    private static void SortRepositories(RepositoryGroup group)
    {
        var sorted = group.Repositories
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        group.Repositories.Clear();
        foreach (var repo in sorted)
        {
            group.Repositories.Add(repo);
        }

        foreach (var child in group.Children)
        {
            SortRepositories(child);
        }
    }

    private static RepositoryInfo CreateRepoSnapshot(RepositoryInfo repo)
    {
        return new RepositoryInfo
        {
            Path = repo.Path,
            Name = repo.Name,
            Tags = repo.Tags.ToList(),
            HiddenBranchNames = repo.HiddenBranchNames.ToList(),
            SoloBranchNames = repo.SoloBranchNames.ToList(),
            // §5.14 branch colour overrides — kept in the snapshot so saves
            // initiated from any management-service path (rename, pin,
            // group, tag) preserve them. Defensive copy with the
            // OrdinalIgnoreCase comparer; comparer is lost on JSON
            // round-trip, but BranchColorService normalises keys to
            // lowercase so the comparer ends up unused at lookup time.
            BranchColorOverrides = new Dictionary<string, string>(
                repo.BranchColorOverrides,
                StringComparer.OrdinalIgnoreCase),
            LastAccessed = repo.LastAccessed,
            GroupId = repo.GroupId,
            IsPinned = repo.IsPinned,
            // Phase A/B: persist the submodule-relationship + provenance
            // fields. Without these in the snapshot, the back-button +
            // sidebar nesting silently lose their state across saves —
            // every reload demotes children to top-level entries.
            ParentRepositoryPath = repo.ParentRepositoryPath,
            IsUserAdded = repo.IsUserAdded,
        };
    }

    private static RepositoryGroup CreateGroupSnapshot(RepositoryGroup group)
    {
        var snapshot = new RepositoryGroup
        {
            Id = group.Id,
            Name = group.Name,
            Type = group.Type,
            IsExpanded = group.IsExpanded
        };

        foreach (var repo in group.Repositories)
        {
            snapshot.Repositories.Add(CreateRepoSnapshot(repo));
        }

        foreach (var child in group.Children)
        {
            snapshot.Children.Add(CreateGroupSnapshot(child));
        }

        return snapshot;
    }
}
