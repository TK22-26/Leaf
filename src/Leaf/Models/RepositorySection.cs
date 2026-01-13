using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Leaf.Models;

/// <summary>
/// Top-level section for quick access repository lists.
/// </summary>
public partial class RepositorySection : ObservableObject
{
    public string Name { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private ObservableCollection<RepositoryInfo> _repositories = [];
}
