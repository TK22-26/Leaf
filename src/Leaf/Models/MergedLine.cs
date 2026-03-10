using CommunityToolkit.Mvvm.ComponentModel;

namespace Leaf.Models;

public enum MergedLineSource
{
    None,
    Ours,
    Theirs,
    Manual
}

public partial class MergedLine : ObservableObject
{
    [ObservableProperty]
    private string _content = string.Empty;

    public MergedLineSource Source { get; set; } = MergedLineSource.None;
}
