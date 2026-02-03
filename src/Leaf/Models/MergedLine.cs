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

    /// <summary>
    /// True if this line is shown for context (before/after the conflict), not part of the conflict itself.
    /// </summary>
    public bool IsContextLine { get; set; }

    public SelectableLine? SourceLine { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (SourceLine != null)
        {
            SourceLine.IsSelected = value;
        }
    }
}
