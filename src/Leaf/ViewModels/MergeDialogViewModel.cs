using CommunityToolkit.Mvvm.ComponentModel;
using Leaf.Models;

namespace Leaf.ViewModels;

/// <summary>
/// ViewModel for the merge dialog.
/// </summary>
public partial class MergeDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _sourceBranch = string.Empty;

    [ObservableProperty]
    private string _targetBranch = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMerge))]
    private string _commitMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSquashMerge))]
    [NotifyPropertyChangedFor(nameof(DialogTitle))]
    [NotifyPropertyChangedFor(nameof(MergeButtonText))]
    [NotifyPropertyChangedFor(nameof(CanMerge))]
    private MergeType _selectedMergeType = MergeType.Normal;

    [ObservableProperty]
    private bool _isMerging;

    /// <summary>
    /// True if squash merge is selected (for backward compatibility).
    /// </summary>
    public bool IsSquashMerge => SelectedMergeType == MergeType.Squash;

    /// <summary>
    /// Title for the dialog (changes based on merge type).
    /// </summary>
    public string DialogTitle => SelectedMergeType switch
    {
        MergeType.Squash => "Squash and Merge",
        MergeType.FastForwardOnly => "Fast-Forward Merge",
        _ => "Merge Branch"
    };

    /// <summary>
    /// Button text for the merge action.
    /// </summary>
    public string MergeButtonText => SelectedMergeType switch
    {
        MergeType.Squash => "Squash and Merge",
        MergeType.FastForwardOnly => "Fast-Forward",
        _ => "Merge"
    };

    /// <summary>
    /// True if the merge can proceed (non-empty message for squash merge).
    /// </summary>
    public bool CanMerge => !IsMerging && (SelectedMergeType != MergeType.Squash || !string.IsNullOrWhiteSpace(CommitMessage));
}
