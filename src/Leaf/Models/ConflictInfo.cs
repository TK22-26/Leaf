namespace Leaf.Models;

/// <summary>
/// Represents a file with merge conflicts.
/// </summary>
public partial class ConflictInfo : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    /// <summary>
    /// Full path to the conflicting file.
    /// </summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _filePath = string.Empty;

    /// <summary>
    /// Just the file name (no path).
    /// </summary>
    public string FileName => System.IO.Path.GetFileName(FilePath);

    /// <summary>
    /// Whether this conflict has been resolved.
    /// </summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isResolved;

    partial void OnFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(FileName));
    }

    /// <summary>
    /// Content from the current branch (HEAD / "ours").
    /// </summary>
    public string OursContent { get; set; } = string.Empty;

    /// <summary>
    /// Content from the incoming branch ("theirs").
    /// </summary>
    public string TheirsContent { get; set; } = string.Empty;

    /// <summary>
    /// The base/ancestor content (before both branches diverged).
    /// </summary>
    public string BaseContent { get; set; } = string.Empty;

    /// <summary>
    /// The final resolved/merged content.
    /// </summary>
    public string MergedContent { get; set; } = string.Empty;

    /// <summary>
    /// Number of conflict regions in this file.
    /// </summary>
    public int ConflictCount { get; set; } = 1;
}
