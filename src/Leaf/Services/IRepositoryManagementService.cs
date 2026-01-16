using System.Collections.ObjectModel;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Interface for repository management operations.
/// Handles repository CRUD, persistence, and quick access management.
/// </summary>
public interface IRepositoryManagementService
{
    /// <summary>
    /// All repository groups (folder-based and custom).
    /// </summary>
    ObservableCollection<RepositoryGroup> RepositoryGroups { get; }

    /// <summary>
    /// Pinned repositories for quick access.
    /// </summary>
    ObservableCollection<RepositoryInfo> PinnedRepositories { get; }

    /// <summary>
    /// Most recently accessed repositories.
    /// </summary>
    ObservableCollection<RepositoryInfo> RecentRepositories { get; }

    /// <summary>
    /// Root items for the repository tree view (sections + groups).
    /// </summary>
    ObservableCollection<object> RepositoryRootItems { get; }

    /// <summary>
    /// Event raised when a repository is added.
    /// </summary>
    event EventHandler<RepositoryInfo>? RepositoryAdded;

    /// <summary>
    /// Event raised when a repository is removed.
    /// </summary>
    event EventHandler<RepositoryInfo>? RepositoryRemoved;

    /// <summary>
    /// Load repositories from persistent storage.
    /// </summary>
    /// <returns>The last selected repository path, if any.</returns>
    Task<string?> LoadRepositoriesAsync();

    /// <summary>
    /// Save repositories to persistent storage.
    /// </summary>
    void SaveRepositories();

    /// <summary>
    /// Add a repository to the appropriate group.
    /// </summary>
    /// <param name="repo">Repository to add.</param>
    /// <param name="save">Whether to persist immediately.</param>
    void AddRepository(RepositoryInfo repo, bool save = true);

    /// <summary>
    /// Remove a repository from all groups.
    /// </summary>
    /// <param name="repo">Repository to remove.</param>
    void RemoveRepository(RepositoryInfo repo);

    /// <summary>
    /// Toggle the pinned state of a repository.
    /// </summary>
    /// <param name="repo">Repository to toggle.</param>
    void TogglePinRepository(RepositoryInfo repo);

    /// <summary>
    /// Mark a repository as recently accessed.
    /// </summary>
    /// <param name="repo">Repository that was accessed.</param>
    void MarkAsRecentlyAccessed(RepositoryInfo repo);

    /// <summary>
    /// Check if a repository already exists in the list.
    /// </summary>
    /// <param name="path">Repository path to check.</param>
    bool ContainsRepository(string path);

    /// <summary>
    /// Find a repository by path.
    /// </summary>
    /// <param name="path">Repository path to find.</param>
    RepositoryInfo? FindRepository(string path);

    /// <summary>
    /// Refresh the quick access sections (pinned and recent).
    /// </summary>
    void RefreshQuickAccess();
}
