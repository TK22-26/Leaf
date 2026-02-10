using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.ViewModels;

public enum PaletteMode
{
    Repository,
    Branch
}

public record HighlightSegment(string Text, bool IsMatch);

public class CommandPaletteItem
{
    public string DisplayName { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string? StatusIcon { get; init; }
    public object? Tag { get; init; }
    public List<HighlightSegment> NameSegments { get; set; } = [];
}

public partial class CommandPaletteViewModel : ObservableObject
{
    private readonly IRepositoryManagementService _repositoryService;
    private readonly Func<RepositoryInfo?> _currentRepoGetter;
    private readonly Action<RepositoryInfo> _repoSelectedCallback;
    private readonly Action<BranchInfo> _branchSelectedCallback;

    private List<CommandPaletteItem> _allItems = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<CommandPaletteItem> _filteredResults = [];

    [ObservableProperty]
    private CommandPaletteItem? _selectedResult;

    [ObservableProperty]
    private int _selectedIndex = -1;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private PaletteMode _currentMode = PaletteMode.Repository;

    [ObservableProperty]
    private string _placeholderText = "Switch to repository...";

    [ObservableProperty]
    private string? _emptyMessage;

    public CommandPaletteViewModel(
        IRepositoryManagementService repositoryService,
        Func<RepositoryInfo?> currentRepoGetter,
        Action<RepositoryInfo> repoSelectedCallback,
        Action<BranchInfo> branchSelectedCallback)
    {
        _repositoryService = repositoryService;
        _currentRepoGetter = currentRepoGetter;
        _repoSelectedCallback = repoSelectedCallback;
        _branchSelectedCallback = branchSelectedCallback;
    }

    public void Open()
    {
        SearchText = string.Empty;
        CurrentMode = PaletteMode.Repository;
        PlaceholderText = "Switch to repository...";
        EmptyMessage = null;
        BuildRepositoryItems();
        UpdateFilter();
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        SearchText = string.Empty;
        CurrentMode = PaletteMode.Repository;
    }

    public void MoveUp()
    {
        if (FilteredResults.Count == 0) return;

        if (SelectedIndex <= 0)
            SelectedIndex = FilteredResults.Count - 1;
        else
            SelectedIndex--;

        SelectedResult = FilteredResults[SelectedIndex];
    }

    public void MoveDown()
    {
        if (FilteredResults.Count == 0) return;

        if (SelectedIndex >= FilteredResults.Count - 1)
            SelectedIndex = 0;
        else
            SelectedIndex++;

        SelectedResult = FilteredResults[SelectedIndex];
    }

    public void Confirm()
    {
        if (SelectedResult?.Tag is RepositoryInfo repo)
        {
            Close();
            _repoSelectedCallback(repo);
        }
        else if (SelectedResult?.Tag is BranchInfo branch)
        {
            Close();
            _branchSelectedCallback(branch);
        }
    }

    public bool HandleEscape()
    {
        if (CurrentMode == PaletteMode.Branch)
        {
            // Switch back to repo mode
            SearchText = string.Empty;
            return true; // handled, don't close
        }

        Close();
        return true;
    }

    partial void OnSearchTextChanged(string value)
    {
        UpdateFilter();
    }

    private void UpdateFilter()
    {
        // Detect mode from prefix
        if (SearchText.StartsWith('#'))
        {
            if (CurrentMode != PaletteMode.Branch)
            {
                CurrentMode = PaletteMode.Branch;
                var repo = _currentRepoGetter();
                if (repo != null)
                {
                    PlaceholderText = $"Checkout branch in {repo.Name}...";
                    BuildBranchItems(repo);
                }
                else
                {
                    PlaceholderText = "Checkout branch...";
                    _allItems = [];
                    EmptyMessage = "No repository selected";
                }
            }

            var query = SearchText.Length > 1 ? SearchText[1..] : string.Empty;
            ApplyFilter(query);
        }
        else
        {
            if (CurrentMode != PaletteMode.Repository)
            {
                CurrentMode = PaletteMode.Repository;
                PlaceholderText = "Switch to repository...";
                BuildRepositoryItems();
            }

            ApplyFilter(SearchText);
        }
    }

    private void ApplyFilter(string query)
    {
        var filtered = string.IsNullOrEmpty(query)
            ? _allItems
            : _allItems.Where(item => item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        // Build highlight segments for each item
        foreach (var item in filtered)
        {
            item.NameSegments = BuildHighlightSegments(item.DisplayName, query);
        }

        FilteredResults = new ObservableCollection<CommandPaletteItem>(filtered);

        // Update empty message
        if (FilteredResults.Count == 0)
        {
            EmptyMessage = CurrentMode == PaletteMode.Repository
                ? (_allItems.Count == 0 ? "No repositories" : "No matching repositories")
                : (_allItems.Count == 0 ? "No repository selected" : "No matching branches");
        }
        else
        {
            EmptyMessage = null;
        }

        // Select first item
        if (FilteredResults.Count > 0)
        {
            SelectedIndex = 0;
            SelectedResult = FilteredResults[0];
        }
        else
        {
            SelectedIndex = -1;
            SelectedResult = null;
        }
    }

    private void BuildRepositoryItems()
    {
        var items = new List<CommandPaletteItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Pinned repos first
        foreach (var repo in _repositoryService.PinnedRepositories)
        {
            if (!repo.Exists || !seen.Add(repo.Path)) continue;
            items.Add(CreateRepoItem(repo));
        }

        // Recent repos next
        foreach (var repo in _repositoryService.RecentRepositories)
        {
            if (!repo.Exists || !seen.Add(repo.Path)) continue;
            items.Add(CreateRepoItem(repo));
        }

        // All other repos alphabetically
        var remaining = new List<(RepositoryInfo Repo, CommandPaletteItem Item)>();
        foreach (var group in _repositoryService.RepositoryGroups)
        {
            foreach (var repo in group.Repositories)
            {
                if (!repo.Exists || !seen.Add(repo.Path)) continue;
                remaining.Add((repo, CreateRepoItem(repo)));
            }
        }

        remaining.Sort((a, b) => string.Compare(a.Repo.Name, b.Repo.Name, StringComparison.OrdinalIgnoreCase));
        items.AddRange(remaining.Select(r => r.Item));

        _allItems = items;
    }

    private static CommandPaletteItem CreateRepoItem(RepositoryInfo repo)
    {
        var detail = repo.CurrentBranch ?? string.Empty;
        string? statusIcon = null;

        if (repo.IsDirty)
            statusIcon = "\u270E"; // ✎

        if (repo.AheadBy > 0 && repo.BehindBy > 0)
            detail += $"  \u2191{repo.AheadBy} \u2193{repo.BehindBy}";
        else if (repo.AheadBy > 0)
            detail += $"  \u2191{repo.AheadBy}";
        else if (repo.BehindBy > 0)
            detail += $"  \u2193{repo.BehindBy}";

        return new CommandPaletteItem
        {
            DisplayName = repo.Name,
            Detail = detail,
            StatusIcon = statusIcon,
            Tag = repo,
            NameSegments = [new HighlightSegment(repo.Name, false)]
        };
    }

    private void BuildBranchItems(RepositoryInfo repo)
    {
        var items = new List<CommandPaletteItem>();

        var localCategory = repo.BranchCategories.FirstOrDefault(c => c.IsLocalCategory);
        if (localCategory == null)
        {
            _allItems = items;
            return;
        }

        // Current branch first
        var currentBranch = localCategory.Branches.FirstOrDefault(b => b.IsCurrent);
        if (currentBranch != null)
        {
            items.Add(CreateBranchItem(currentBranch));
        }

        // All other branches alphabetically
        var others = localCategory.Branches
            .Where(b => !b.IsCurrent)
            .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CreateBranchItem);

        items.AddRange(others);
        _allItems = items;
    }

    private static CommandPaletteItem CreateBranchItem(BranchInfo branch)
    {
        var detail = string.Empty;
        string? statusIcon = null;

        if (branch.IsCurrent)
            statusIcon = "\u2713"; // ✓

        if (branch.AheadBy > 0 && branch.BehindBy > 0)
            detail = $"\u2191{branch.AheadBy} \u2193{branch.BehindBy}";
        else if (branch.AheadBy > 0)
            detail = $"\u2191{branch.AheadBy}";
        else if (branch.BehindBy > 0)
            detail = $"\u2193{branch.BehindBy}";

        return new CommandPaletteItem
        {
            DisplayName = branch.Name,
            Detail = detail,
            StatusIcon = statusIcon,
            Tag = branch,
            NameSegments = [new HighlightSegment(branch.Name, false)]
        };
    }

    private static List<HighlightSegment> BuildHighlightSegments(string text, string query)
    {
        if (string.IsNullOrEmpty(query))
            return [new HighlightSegment(text, false)];

        var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return [new HighlightSegment(text, false)];

        var segments = new List<HighlightSegment>();

        if (index > 0)
            segments.Add(new HighlightSegment(text[..index], false));

        segments.Add(new HighlightSegment(text[index..(index + query.Length)], true));

        if (index + query.Length < text.Length)
            segments.Add(new HighlightSegment(text[(index + query.Length)..], false));

        return segments;
    }
}
