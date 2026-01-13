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

public partial class ConflictDisplayLine : ObservableObject
{
    [ObservableProperty]
    private int _lineNumber;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private bool _isSelectable;

    [ObservableProperty]
    private bool _isSelected;

    public SelectableLine? SourceLine { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (SourceLine != null)
        {
            SourceLine.IsSelected = value;
        }
    }
}
