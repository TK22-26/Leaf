using System.Collections.ObjectModel;
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
        var data = _settingsService.LoadRepositories();

        foreach (var repo in data.Repositories)
        {
            // Only add if the repo still exists on disk
            if (repo.Exists)
            {
                AddRepositoryToGroups(repo, save: false, raiseEvent: false);
            }
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

        // Return the last selected repository path
        var settings = _settingsService.LoadSettings();
        return Task.FromResult(settings.LastSelectedRepositoryPath);
    }

    public void SaveRepositories()
    {
        var allRepos = RepositoryGroups
            .SelectMany(g => g.Repositories)
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
        AddRepositoryToGroups(repo, save, raiseEvent: true);
    }

    public void RemoveRepository(RepositoryInfo repo)
    {
        RemoveRepositoryFromGroups(repo);
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
        // Don't call RefreshQuickAccess here - it recreates QuickAccessItem objects
        // which destroys TreeView selection. Quick access will refresh on next app start
        // or when repos are added/removed/pinned.
    }

    public bool ContainsRepository(string path)
    {
        return RepositoryGroups
            .SelectMany(g => g.Repositories)
            .Any(r => r.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
    }

    public RepositoryInfo? FindRepository(string path)
    {
        return RepositoryGroups
            .SelectMany(g => g.Repositories)
            .FirstOrDefault(r => r.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
    }

    public void RefreshQuickAccess()
    {
        var allRepos = RepositoryGroups
            .SelectMany(g => g.Repositories)
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

    private void AddRepositoryToGroups(RepositoryInfo repo, bool save, bool raiseEvent)
    {
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

        // Add repo if not already present
        if (!folderGroup.Repositories.Any(r => r.Path == repo.Path))
        {
            folderGroup.Repositories.Add(repo);
            SortRepositories(folderGroup);

            if (save)
            {
                SaveRepositories();
            }

            if (raiseEvent)
            {
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
            LastAccessed = repo.LastAccessed,
            GroupId = repo.GroupId,
            IsPinned = repo.IsPinned
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
