using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Leaf.Models;

/// <summary>
/// Top-level section for quick access repository lists (PINNED, MOST RECENT).
/// Uses QuickAccessItem wrappers to ensure unique object instances for TreeView selection.
/// </summary>
public partial class RepositorySection : ObservableObject
{
    public string Name { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private ObservableCollection<QuickAccessItem> _items = [];
}
