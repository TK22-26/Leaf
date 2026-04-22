#nullable enable
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Leaf.ViewModels;

namespace Leaf.ViewModels.Merge;

/// <summary>
/// Command-palette view-model scoped to the merge editor. Mirrors the public
/// shape <see cref="Leaf.Views.CommandPaletteView"/> binds to — same
/// properties (SearchText / FilteredResults / SelectedResult / SelectedIndex /
/// IsOpen / PlaceholderText / EmptyMessage) and same command methods (MoveUp
/// / MoveDown / Confirm / HandleEscape / Close / ConfirmItem) — so the
/// existing palette control can host us without modification.
/// </summary>
/// <remarks>
/// <para>
/// The main-window palette (<see cref="CommandPaletteViewModel"/>) is
/// tightly coupled to repository / branch switching through its constructor
/// dependencies. Rather than retrofit that type with merge-command support,
/// C3 ships a sibling VM — same surface, different intent. The
/// <see cref="CommandPaletteItem.Tag"/> carries an <see cref="ICommand"/>
/// instance; <see cref="ConfirmItem"/> executes it and closes.
/// </para>
/// <para>
/// Filtering is ordinal-case-insensitive substring match against
/// <see cref="CommandPaletteItem.DisplayName"/> only — the Detail field
/// holds the keybinding, which users think of as the *answer* to "how do I
/// invoke it", not a search key.
/// </para>
/// </remarks>
public partial class MergeCommandPaletteViewModel : ObservableObject, ICommandPaletteHost
{
    private IReadOnlyList<CommandPaletteItem> _allItems = Array.Empty<CommandPaletteItem>();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<CommandPaletteItem> _filteredResults = new();

    [ObservableProperty]
    private CommandPaletteItem? _selectedResult;

    [ObservableProperty]
    private int _selectedIndex = -1;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _placeholderText = "Run a merge command…";

    [ObservableProperty]
    private string? _emptyMessage;

    /// <summary>
    /// Populate the palette with <paramref name="items"/> and surface it.
    /// Clears the search box so the user starts with an empty query against
    /// the full list.
    /// </summary>
    public void Open(IReadOnlyList<CommandPaletteItem> items)
    {
        _allItems = items ?? Array.Empty<CommandPaletteItem>();
        SearchText = string.Empty;
        EmptyMessage = _allItems.Count == 0 ? "No merge commands available" : null;
        UpdateFilter();
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        SearchText = string.Empty;
    }

    /// <summary>
    /// Merge palette has no sub-mode, so Escape always dismisses. Returns
    /// <c>true</c> to match <see cref="ICommandPaletteHost.HandleEscape"/>:
    /// the palette consumed the key and cleared itself.
    /// </summary>
    public bool HandleEscape()
    {
        Close();
        return true;
    }

    public void MoveUp()
    {
        if (FilteredResults.Count == 0) return;
        SelectedIndex = SelectedIndex <= 0 ? FilteredResults.Count - 1 : SelectedIndex - 1;
        SelectedResult = FilteredResults[SelectedIndex];
    }

    public void MoveDown()
    {
        if (FilteredResults.Count == 0) return;
        SelectedIndex = SelectedIndex >= FilteredResults.Count - 1 ? 0 : SelectedIndex + 1;
        SelectedResult = FilteredResults[SelectedIndex];
    }

    public void Confirm() => ConfirmItem(SelectedResult);

    public void ConfirmItem(CommandPaletteItem? item)
    {
        if (item?.Tag is ICommand command && command.CanExecute(null))
        {
            command.Execute(null);
        }
        Close();
    }

    partial void OnSearchTextChanged(string value) => UpdateFilter();

    private void UpdateFilter()
    {
        FilteredResults.Clear();
        var query = SearchText?.Trim() ?? string.Empty;
        if (_allItems.Count == 0)
        {
            SelectedIndex = -1;
            SelectedResult = null;
            return;
        }
        foreach (var item in _allItems)
        {
            if (query.Length == 0 ||
                item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                item.NameSegments = BuildHighlightSegments(item.DisplayName, query);
                FilteredResults.Add(item);
            }
        }
        SelectedIndex = FilteredResults.Count > 0 ? 0 : -1;
        SelectedResult = FilteredResults.Count > 0 ? FilteredResults[0] : null;
    }

    /// <summary>
    /// Split <paramref name="displayName"/> into highlight segments around
    /// the first ordinal-insensitive occurrence of <paramref name="query"/>.
    /// Empty query returns a single non-matching segment so the view still
    /// has valid input for its ItemsControl binding.
    /// </summary>
    private static List<HighlightSegment> BuildHighlightSegments(string displayName, string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return new List<HighlightSegment> { new(displayName, IsMatch: false) };
        }
        var segments = new List<HighlightSegment>();
        var idx = displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            segments.Add(new(displayName, IsMatch: false));
            return segments;
        }
        if (idx > 0) segments.Add(new(displayName[..idx], IsMatch: false));
        segments.Add(new(displayName.Substring(idx, query.Length), IsMatch: true));
        var tailStart = idx + query.Length;
        if (tailStart < displayName.Length) segments.Add(new(displayName[tailStart..], IsMatch: false));
        return segments;
    }
}
